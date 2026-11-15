using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Environment
{
    /// <summary>
    /// Turns spacecraft state into the view outside. The cockpit never moves: instead the world is
    /// rotated around it to match the vehicle's attitude, and the sky shader is told the altitude,
    /// the local vertical and where the Sun, Moon, stars and Earth's surface are.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class EnvironmentDirector : MonoBehaviour
    {
        private static readonly int SunDirId = Shader.PropertyToID("_PhainonSunDir");
        private static readonly int UpDirId = Shader.PropertyToID("_PhainonUpDir");
        private static readonly int MoonDirId = Shader.PropertyToID("_PhainonMoonDir");
        private static readonly int AltitudeId = Shader.PropertyToID("_PhainonAltitudeKm");
        private static readonly int AtmosphereId = Shader.PropertyToID("_PhainonAtmosphere");
        private static readonly int StarVisibilityId = Shader.PropertyToID("_PhainonStarVisibility");
        private static readonly int CloudTimeId = Shader.PropertyToID("_PhainonCloudTime");
        private static readonly int WorldToEarthId = Shader.PropertyToID("_PhainonWorldToEarth");
        private static readonly int WorldToInertialId = Shader.PropertyToID("_PhainonWorldToInertial");
        private static readonly int HazeColorId = Shader.PropertyToID("_PhainonHazeColorNow");
        private static readonly int SiteFadeId = Shader.PropertyToID("_PhainonSiteFade");
        private static readonly int BodyRadiusId = Shader.PropertyToID("_PhainonBodyRadiusKm");
        private static readonly int BodyId = Shader.PropertyToID("_PhainonBody");
        private static readonly int FeatureLonId = Shader.PropertyToID("_PhainonFeatureLon");
        private static readonly int MoonsId = Shader.PropertyToID("_PhainonMoons");

        // Io, Europa, Ganymede, Callisto: orbit radius (m), period (s), radius (m), and where each starts round its orbit (radians).
        private static readonly double[] MoonOrbit = { 4.217e8, 6.711e8, 1.0704e9, 1.8827e9 };
        private static readonly double[] MoonPeriod = { 152854.0, 306822.0, 618153.0, 1441931.0 };
        private static readonly double[] MoonRadius = { 1.8216e6, 1.5608e6, 2.6341e6, 2.4103e6 };
        private static readonly double[] MoonStart = { 0.6, 2.4, 4.0, 5.3 };
        private readonly Vector4[] moons = new Vector4[4];

        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal EnvironmentConfig config;
        [Tooltip("Parent of everything that belongs to the outside world. It is counter-rotated against the vehicle's attitude.")]
        [SerializeField] internal Transform worldRoot;
        [Tooltip("A frame fixed to the ground under the launch point. The launch site and the ascent trail live in it.")]
        [SerializeField] internal Transform groundFrame;
        [SerializeField] internal Transform launchSite;
        [SerializeField] internal Light sun;
        [Tooltip("A soft light inside the cabin, lit only while the Sun shines in through the forward windows.")]
        [SerializeField] internal Light cabinBounce;
        [Tooltip("The cabin as its metal and glass reflect it. Re-rendered now and then, not every frame.")]
        [SerializeField] internal ReflectionProbe cabinProbe;
        [Tooltip("How quickly the view follows attitude changes. Smooths the 50 Hz simulation for display.")]
        [SerializeField] internal float attitudeSmoothing = 14f;

        private Quaternion levelToEarthAtLaunch;
        private Vector3 sunInertial;
        private Vector3 moonInertial;
        private Quaternion shownAttitude;
        private bool hasAttitude;
        private float cloudTime;
        private float earthshineNow;
        private Vector3 probeSun;

        /// <summary>1 on the ground, falling to 0 in vacuum. Other effects use it to fade with the air.</summary>
        public float Atmosphere { get; private set; }
        /// <summary>The vehicle's velocity direction in cockpit (world) space; zero when stationary.</summary>
        public Vector3 AirflowDirection { get; private set; }
        public float Daylight { get; private set; }

        private void Awake()
        {
            // Earth-fixed axes: +Y is the north pole and longitude is measured from +X towards +Z.
            float lat = config.launchLatitude * Mathf.Deg2Rad;
            float lon = config.launchLongitude * Mathf.Deg2Rad;
            Vector3 siteUp = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
            Vector3 east = new Vector3(-Mathf.Sin(lon), 0f, Mathf.Cos(lon));
            // North at the site: the pole direction with its vertical component removed.
            Vector3 north = (Vector3.up - siteUp * Vector3.Dot(Vector3.up, siteUp)).normalized;

            float azimuth = config.launchAzimuth * Mathf.Deg2Rad;
            Vector3 downrange = (north * Mathf.Cos(azimuth) + east * Mathf.Sin(azimuth)).normalized;
            levelToEarthAtLaunch = Quaternion.LookRotation(downrange, siteUp);

            sunInertial = DirectionFromAngles(config.sunAzimuth, config.sunElevation);
            moonInertial = DirectionFromAngles(config.moonAzimuth, config.moonElevation);
        }

        /// <summary>Direction in the launch-site level frame: +Z downrange, +Y up, +X to the right of track.</summary>
        private static Vector3 DirectionFromAngles(float azimuthDegrees, float elevationDegrees)
        {
            float az = azimuthDegrees * Mathf.Deg2Rad;
            float el = elevationDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        }

        private void LateUpdate()
        {
            Spacecraft craft = spacecraft.Craft;
            FlightModel flight = craft.Flight;
            AttitudeController attitude = craft.Attitude;

            // Vehicle attitude in the local level frame (+Z downrange, +Y up). Unity's Euler order
            // applies yaw, then pitch, then roll, which is the order the angles are defined in.
            // The cabin is then tipped nose-down from the thrust axis by a fixed angle.
            Quaternion vehicle = Quaternion.Euler(-(float)attitude.PitchDegrees, (float)attitude.YawDegrees, -(float)attitude.RollDegrees)
                                 * Quaternion.Euler(config.cabinTiltDegrees, 0f, 0f);
            if (!hasAttitude) { shownAttitude = vehicle; hasAttitude = true; }
            shownAttitude = Quaternion.Slerp(shownAttitude, vehicle, 1f - Mathf.Exp(-attitudeSmoothing * Time.deltaTime));

            // The world is rotated the opposite way so the vehicle's nose stays along the cockpit's +Z.
            Quaternion levelToWorld = Quaternion.Inverse(shownAttitude);
            worldRoot.localRotation = levelToWorld;

            // Travelling downrange tips the local vertical forward relative to where it was at launch.
            // Reduced to one turn in double precision: after many orbits the raw angle is too big for a float.
            float downrangeDegrees = (float)(flight.DownrangeAngle * (180.0 / System.Math.PI) % 360.0);
            Quaternion levelToInertial = Quaternion.Euler(downrangeDegrees, 0f, 0f);
            Quaternion inertialToWorld = levelToWorld * Quaternion.Inverse(levelToInertial);

            ScenarioSpec scenario = craft.Scenario;
            bool away = scenario != null;
            BodySpec body = flight.Body;

            Vector3 up = levelToWorld * Vector3.up;
            Vector3 sunDirection = inertialToWorld * (away ? ScenarioSun(scenario) : sunInertial);
            Vector3 moonDirection = inertialToWorld * moonInertial;

            float altitudeKm = (float)(flight.AltitudeMeters / 1000.0);
            // A flight that starts in space has no air around it and no launch site below it.
            Atmosphere = away ? 0f : Mathf.Exp(-altitudeKm / Mathf.Max(0.1f, config.skyScaleHeightKm));
            float stars = away ? 1f : Mathf.InverseLerp(config.starsStartAltitudeKm, config.starsFullAltitudeKm, altitudeKm);
            // Sunlit unless the Sun is behind the Earth's disc. On the ground that disc is everything below
            // the horizon; from deep space it is a small circle, so the craft is almost never in shadow.
            float earthSine = (float)(body.radiusMeters / flight.Radius);
            float earthAngle = Mathf.Asin(Mathf.Clamp01(earthSine));
            float sunFromNadir = Vector3.Angle(sunDirection, -up) * Mathf.Deg2Rad;
            // Dusk is soft through air and sharp in vacuum.
            Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                earthAngle - Mathf.Lerp(0.10f, 0.22f, Atmosphere), earthAngle + Mathf.Lerp(0.06f, 0.18f, Atmosphere), sunFromNadir));
            // Light reflected from Earth: how much of its disc is lit from here, and how much sky it fills.
            earthshineNow = (0.5f + 0.5f * Vector3.Dot(up, sunDirection)) * earthSine * earthSine;

            cloudTime += Time.deltaTime * config.cloudDriftSpeed;

            Shader.SetGlobalVector(UpDirId, up);
            Shader.SetGlobalVector(SunDirId, sunDirection);
            Shader.SetGlobalVector(MoonDirId, moonDirection);
            // The cockpit sits on top of the stack, so on the pad the eye is already well above the ground.
            Shader.SetGlobalFloat(AltitudeId, altitudeKm + (away ? 0f : config.stackHeight / 1000f));
            Shader.SetGlobalFloat(BodyRadiusId, (float)(body.radiusMeters / 1000.0));
            Shader.SetGlobalFloat(BodyId, body.name == "JUPITER" ? 1f : 0f);
            Shader.SetGlobalFloat(FeatureLonId, (float)body.featureLongitudeDegrees * Mathf.Deg2Rad);
            Shader.SetGlobalFloat(AtmosphereId, Atmosphere);
            Shader.SetGlobalFloat(StarVisibilityId, stars);
            Shader.SetGlobalFloat(CloudTimeId, cloudTime);
            Shader.SetGlobalMatrix(WorldToInertialId, Matrix4x4.Rotate(Quaternion.Inverse(inertialToWorld)));
            // Body-fixed axes. Away from Earth the flight plane is the body's equator: its pole lies to the
            // left of the track, and the body turns under the spacecraft by the simulation's own spin angle.
            Quaternion inertialToBody = away
                ? Quaternion.Euler(0f, (float)craft.BodySpinDegrees, 0f) * Quaternion.Euler(0f, 0f, -90f)
                : levelToEarthAtLaunch;
            Shader.SetGlobalMatrix(WorldToEarthId, Matrix4x4.Rotate(inertialToBody * levelToInertial * shownAttitude));

            Color haze = config.horizonColor * (0.05f + 0.95f * Daylight);
            Shader.SetGlobalColor(HazeColorId, haze);

            UpdateMoons(craft, away && body.name == "JUPITER", inertialToWorld);
            UpdateSun(sunDirection, away);
            UpdateAmbient(up, sunDirection, away);
            UpdateLaunchSite(flight, away);
            UpdateAirflow(flight);
        }

        /// <summary>
        /// Jupiter's four large moons on circular orbits in the flight plane, turning the same way the
        /// spacecraft flies. They are scenery: the flight model has one centre of gravity.
        /// </summary>
        private void UpdateMoons(Spacecraft craft, bool shown, Quaternion inertialToWorld)
        {
            for (int i = 0; i < moons.Length; i++)
            {
                if (!shown) { moons[i] = Vector4.zero; continue; }
                // In the frame the flight starts in: +Y is up at the start, +Z the way the vehicle travels.
                double here = craft.Flight.DownrangeAngle, there = MoonStart[i] + 2.0 * System.Math.PI * craft.SimClockSeconds / MoonPeriod[i];
                double y = MoonOrbit[i] * System.Math.Cos(there) - craft.Flight.Radius * System.Math.Cos(here);
                double z = MoonOrbit[i] * System.Math.Sin(there) - craft.Flight.Radius * System.Math.Sin(here);
                double distance = System.Math.Sqrt(y * y + z * z);
                Vector3 direction = inertialToWorld * new Vector3(0f, (float)(y / distance), (float)(z / distance));
                // Never smaller than a pixel or so, so a distant moon still shows as a point.
                float radius = Mathf.Max(0.0016f, (float)System.Math.Asin(System.Math.Min(1.0, MoonRadius[i] / distance)));
                moons[i] = new Vector4(direction.x, direction.y, direction.z, radius);
            }
            Shader.SetGlobalVectorArray(MoonsId, moons);
        }

        /// <summary>The Sun as the mission places it: in the flight plane, lifted a little out of it.</summary>
        private static Vector3 ScenarioSun(ScenarioSpec scenario)
        {
            float angle = (float)scenario.sunAngleDegrees * Mathf.Deg2Rad;
            float lift = (float)scenario.sunElevationDegrees * Mathf.Deg2Rad;
            // In the frame the flight starts in: +Y is up at the start, +Z is the way the vehicle travels.
            return new Vector3(Mathf.Sin(lift), Mathf.Cos(lift) * Mathf.Cos(angle), Mathf.Cos(lift) * Mathf.Sin(angle));
        }

        private void UpdateSun(Vector3 sunDirection, bool away)
        {
            if (sun == null) return;
            sun.transform.rotation = Quaternion.LookRotation(-sunDirection);
            sun.color = config.sunColor;
            // The Sun is harsher in vacuum, and switches off once the Earth is in the way.
            float intensity = Mathf.Lerp(config.sunIntensitySpace, config.sunIntensityGround, Atmosphere);
            sun.intensity = intensity * Daylight * (away ? config.deepSpaceSunScale : 1f);

            if (cabinBounce != null)
            {
                // The cockpit never turns in the scene, so the forward windows always face this way.
                float facing = Mathf.Clamp01(Vector3.Dot(sunDirection, new Vector3(0f, 0.3f, 0.954f)));
                cabinBounce.color = config.sunColor;
                cabinBounce.intensity = config.cabinBounceIntensity * facing * sun.intensity / Mathf.Max(0.01f, config.sunIntensitySpace);
            }

            // The reflection only needs redoing when the light has moved enough to show.
            // (An unset direction means it has never been done.)
            if (cabinProbe != null && (probeSun == Vector3.zero || Vector3.Angle(sunDirection, probeSun) > 5f))
            {
                probeSun = sunDirection;
                cabinProbe.RenderProbe();
            }
        }

        private void UpdateAmbient(Vector3 up, Vector3 sunDirection, bool away)
        {
            Color sky = config.groundAmbient * (Atmosphere * Daylight);
            Color space = config.spaceAmbient + (away ? config.jupiterShine : config.earthshine) * ((1f - Atmosphere) * earthshineNow);
            RenderSettings.ambientLight = sky + space;
        }

        private void UpdateLaunchSite(FlightModel flight, bool away)
        {
            float altitude = (float)flight.AltitudeMeters;
            float fade = away ? 0f : 1f - Mathf.InverseLerp(config.siteFadeStartAltitude, config.siteFadeEndAltitude, altitude);
            Shader.SetGlobalFloat(SiteFadeId, fade);

            // In the level frame the ground drops straight down and slides behind as the vehicle heads downrange.
            // Only while there is something there to see: beyond that the numbers are too large to place anything by.
            if (fade > 0f || altitude < 200000f)
                groundFrame.localPosition = new Vector3(0f, -altitude, -(float)flight.DownrangeMeters);

            if (launchSite == null) return;
            bool visible = fade > 0f;
            if (launchSite.gameObject.activeSelf != visible) launchSite.gameObject.SetActive(visible);
        }

        private void UpdateAirflow(FlightModel flight)
        {
            if (flight.Speed < 1.0)
            {
                AirflowDirection = Vector3.zero;
                return;
            }
            Vector3 velocityLevel = new Vector3(0f, (float)flight.VerticalSpeed, (float)flight.TangentialVelocity).normalized;
            AirflowDirection = worldRoot.rotation * velocityLevel;
        }
    }
}
