using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Environment
{
    /// <summary>
    /// The visible side of a launch: engine exhaust, the cloud on the pad, condensation around the
    /// vehicle, the trail it leaves through the air and the stage separation event. Each effect has
    /// its own inputs and its own update, and every one of them is driven by live simulation state
    /// (engine output, altitude, speed, air density), so nothing plays on a timer.
    /// </summary>
    public class AscentEffects : MonoBehaviour
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int VacuumId = Shader.PropertyToID("_Vacuum");

        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal EnvironmentDirector environment;
        [SerializeField] internal EnvironmentConfig config;
        [Tooltip("The vehicle exterior. Its -Z axis runs aft along the stack.")]
        [SerializeField] internal Transform vehicle;

        [Header("1. Engine exhaust")]
        [SerializeField] internal Renderer[] boosterPlumes;
        [SerializeField] internal Renderer[] upperPlumes;
        [SerializeField] internal Renderer[] servicePlumes;
        [SerializeField] internal Light plumeLight;
        [SerializeField] internal float plumeLightIntensity = 5f;

        [Header("2. Launch pad exhaust cloud")]
        [SerializeField] internal ParticleSystem padBillows;
        [SerializeField] internal ParticleSystem padJets;
        [SerializeField] internal float padBillowRate = 34f;
        [SerializeField] internal float padJetRate = 46f;
        [Tooltip("Altitude above which the engines no longer disturb the pad, in metres.")]
        [SerializeField] internal float padSmokeCeiling = 350f;

        [Header("3. Condensation around the vehicle")]
        [Tooltip("Wisps that stream past the cockpit window: cloud layers and vapour.")]
        [SerializeField] internal ParticleSystem cloudWisps;
        [SerializeField] internal float wispDistance = 90f;
        [SerializeField] internal float maxWispRate = 46f;
        [SerializeField] internal float maxWispSpeed = 170f;
        [Tooltip("Vapour rings on the hull. The first is on the capsule; later ones belong to the booster.")]
        [SerializeField] internal ParticleSystem[] vapourCollars;
        [SerializeField] internal float collarRate = 70f;

        [Header("4. Ascent trail")]
        [SerializeField] internal ParticleSystem trail;
        [Tooltip("Distance flown between trail puffs at low speed, in metres.")]
        [SerializeField] internal float trailSpacing = 14f;
        [SerializeField] internal float maxTrailRate = 24f;
        [SerializeField] internal float boosterNozzleZ = -62f;
        [SerializeField] internal float upperNozzleZ = -24.4f;

        [Header("5. Stage separation")]
        [SerializeField] internal Transform booster;
        [Tooltip("Everything that leaves when the upper stage is dropped in orbit.")]
        [SerializeField] internal Transform upperStage;
        [SerializeField] internal ParticleSystem separationFlash;
        [SerializeField] internal ParticleSystem separationGas;
        [SerializeField] internal float boosterDriftSeconds = 16f;
        [SerializeField] internal Vector3 boosterTumble = new Vector3(9f, 3f, 14f);

        private MaterialPropertyBlock block;
        private Vector3[] boosterRestScales;
        private Vector3[] upperRestScales;
        private Vector3[] serviceRestScales;
        private bool boosterReleased;
        /// <summary>The spent stage currently backing away from the vehicle.</summary>
        private Transform drifting;
        private float boosterTimer;
        private Vector3 boosterVelocity;

        /// <summary>Condensation factor this frame, 0..1. Exposed for tuning and debugging.</summary>
        public float Condensation { get; private set; }

        private void Start()
        {
            block = new MaterialPropertyBlock();
            boosterRestScales = RestScales(boosterPlumes);
            upperRestScales = RestScales(upperPlumes);
            if (servicePlumes == null) servicePlumes = new Renderer[0];
            serviceRestScales = RestScales(servicePlumes);
            spacecraft.Craft.Engine.StageSeparated += OnStageSeparated;
        }

        private void OnDestroy()
        {
            if (spacecraft != null && spacecraft.Craft != null) spacecraft.Craft.Engine.StageSeparated -= OnStageSeparated;
        }

        private static Vector3[] RestScales(Renderer[] plumes)
        {
            var scales = new Vector3[plumes.Length];
            for (int i = 0; i < plumes.Length; i++) scales[i] = plumes[i].transform.localScale;
            return scales;
        }

        private void Update()
        {
            Spacecraft craft = spacecraft.Craft;
            UpdateExhaust(craft);
            UpdatePadCloud(craft);
            UpdateCondensation(craft);
            UpdateTrail(craft);
            UpdateBooster();
        }

        // ---------------------------------------------------------------- 1. engine exhaust

        private void UpdateExhaust(Spacecraft craft)
        {
            float output = (float)craft.Engine.OutputFraction;
            int stage = craft.Engine.ActiveStage;
            bool boosterStage = stage == 0;
            // A plume balloons outward as ambient pressure falls away, and loses its sooty glow.
            float vacuum = 1f - Mathf.Sqrt(environment.Atmosphere);
            float expansion = Mathf.Lerp(1f, 3.2f, vacuum);
            float flicker = 0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 23f, 1.7f);

            for (int i = 0; i < boosterPlumes.Length; i++)
                SetPlume(boosterPlumes[i], boosterRestScales[i], boosterStage && !boosterReleased ? output : 0f, expansion, flicker, vacuum);
            for (int i = 0; i < upperPlumes.Length; i++)
                SetPlume(upperPlumes[i], upperRestScales[i], stage == 1 ? output : 0f, expansion * 0.9f, flicker, vacuum);
            for (int i = 0; i < servicePlumes.Length; i++)
                SetPlume(servicePlumes[i], serviceRestScales[i], stage >= 2 ? output : 0f, expansion * 0.9f, flicker, vacuum);

            if (plumeLight != null) plumeLight.intensity = plumeLightIntensity * output * flicker;
        }

        private void SetPlume(Renderer plume, Vector3 restScale, float output, float expansion, float flicker, float vacuum)
        {
            bool visible = output > 0.01f;
            if (plume.enabled != visible) plume.enabled = visible;
            if (!visible) return;

            // Length follows thrust, so spool-up, throttling and tail-off all show in the flame.
            plume.transform.localScale = new Vector3(restScale.x * expansion, restScale.y * expansion,
                restScale.z * Mathf.Lerp(0.45f, 1f, output) * flicker);
            plume.GetPropertyBlock(block);
            block.SetFloat(IntensityId, Mathf.Lerp(0.35f, 1.15f, output) * flicker);
            block.SetFloat(VacuumId, vacuum);
            plume.SetPropertyBlock(block);
        }

        // ---------------------------------------------------------------- 2. pad cloud

        private void UpdatePadCloud(Spacecraft craft)
        {
            // Exhaust only reaches the pad while the booster burns on or just above it.
            float altitude = (float)craft.Flight.AltitudeMeters;
            bool nearPad = craft.Engine.ActiveStage == 0 && altitude < padSmokeCeiling;
            float strength = nearPad ? (float)craft.Engine.OutputFraction * (1f - Mathf.Clamp01(altitude / padSmokeCeiling)) : 0f;
            SetRate(padBillows, padBillowRate * strength);
            SetRate(padJets, padJetRate * strength);
        }

        // ---------------------------------------------------------------- 3. condensation

        private void UpdateCondensation(Spacecraft craft)
        {
            FlightModel flight = craft.Flight;
            Condensation = (float)VapourModel.CondensationFactor(flight.MachNumber, flight.AltitudeMeters,
                config.humidityScaleHeight, config.vapourPeakMach, config.vapourMachWidth);

            // Hull collars: the capsule's stays with the vehicle, the others leave with the booster.
            for (int i = 0; i < vapourCollars.Length; i++)
            {
                bool attached = i == 0 || (craft.Engine.ActiveStage == 0 && !boosterReleased);
                SetRate(vapourCollars[i], attached ? collarRate * Condensation : 0f);
            }

            if (cloudWisps == null) return;

            // Window wisps: cloud layers the vehicle punches through, plus the same vapour.
            float altitude = (float)flight.AltitudeMeters;
            float density = 0f;
            for (int i = 0; i < config.cloudLayers.Length; i++)
            {
                float offset = (altitude - config.cloudLayers[i].x) / Mathf.Max(1f, config.cloudLayers[i].y);
                density += Mathf.Exp(-offset * offset);
            }
            density = Mathf.Clamp01(density + Condensation * 0.7f);

            float speed = (float)flight.Speed;
            SetRate(cloudWisps, speed > 8f ? density * maxWispRate : 0f);

            Vector3 flow = environment.AirflowDirection;
            if (flow.sqrMagnitude > 0.5f)
            {
                // The emitter sits ahead along the flight path and throws wisps back past the cockpit.
                Transform emitter = cloudWisps.transform;
                emitter.position = flow * wispDistance;
                emitter.rotation = Quaternion.LookRotation(-flow);
                ParticleSystem.MainModule main = cloudWisps.main;
                main.startSpeed = Mathf.Clamp(speed * 0.8f, 25f, maxWispSpeed);
            }
        }

        // ---------------------------------------------------------------- 4. ascent trail

        private void UpdateTrail(Spacecraft craft)
        {
            if (trail == null) return;

            // The emitter rides on the active stage's nozzle. Puffs are simulated in the ground frame,
            // so each one stays where it was laid and the trail traces the real trajectory.
            float nozzle = craft.Engine.ActiveStage == 0 ? boosterNozzleZ : upperNozzleZ;
            trail.transform.position = vehicle.TransformPoint(0f, 0f, nozzle);

            // Exhaust only condenses into a visible trail where there is air: full strength low down,
            // thinning through the stratosphere, nothing in vacuum.
            float air = Mathf.Clamp01(environment.Atmosphere * 12f);
            float speed = (float)craft.Flight.Speed;
            float rate = Mathf.Min(speed / trailSpacing, maxTrailRate) * (float)craft.Engine.OutputFraction * air;
            SetRate(trail, craft.Flight.Released ? rate : 0f);

            // High, cold air keeps a trail for longer than the lower atmosphere does.
            ParticleSystem.MainModule main = trail.main;
            float lifetime = Mathf.Lerp(16f, 32f, Mathf.Clamp01((float)craft.Flight.AltitudeMeters / 15000f));
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime);
        }

        // ---------------------------------------------------------------- 5. stage separation

        private void OnStageSeparated(int droppedStage)
        {
            if (separationFlash != null) separationFlash.Play();
            if (separationGas != null) separationGas.Play();
            // Only one spent stage is ever in view: the previous one is long gone by the next separation.
            if (drifting != null) drifting.gameObject.SetActive(false);
            drifting = droppedStage == 0 ? booster : upperStage;
            if (droppedStage == 0) boosterReleased = true;
            if (drifting == null) return;
            boosterTimer = 0f;
            boosterVelocity = new Vector3(0.35f, -0.2f, -3.5f);
        }

        private void UpdateBooster()
        {
            Transform booster = drifting;
            if (booster == null || !booster.gameObject.activeSelf) return;

            // The spent stage backs away, slowly tumbling, until it is too small to matter.
            boosterTimer += Time.deltaTime;
            boosterVelocity += new Vector3(0f, 0f, -2.2f) * Time.deltaTime;
            booster.localPosition += boosterVelocity * Time.deltaTime;
            booster.Rotate(boosterTumble * Time.deltaTime, Space.Self);

            if (boosterTimer >= boosterDriftSeconds) booster.gameObject.SetActive(false);
        }

        /// <summary>Sets an emission rate, scaled by the project's effects density setting.</summary>
        private void SetRate(ParticleSystem system, float perSecond)
        {
            if (system == null) return;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = perSecond * config.effectsDensity;
        }
    }
}
