using UnityEngine;

namespace Phainon
{
    /// <summary>
    /// Tunable look of the world outside the window: where the Sun and Moon sit, how quickly the sky
    /// darkens with altitude, when the launch site drops out of view and where the cloud layers are.
    /// </summary>
    [CreateAssetMenu(menuName = "Phainon/Environment Config", fileName = "EnvironmentConfig")]
    public class EnvironmentConfig : ScriptableObject
    {
        [Header("Launch site")]
        public float launchLatitude = 28.6f;
        public float launchLongitude = -80.6f;
        [Tooltip("Compass heading of the ascent, in degrees. 90 is due east.")]
        public float launchAzimuth = 90f;
        [Tooltip("Distance from the cockpit down to the pad surface, in metres.")]
        public float stackHeight = 62f;
        [Tooltip("How far the window axis points below the thrust axis, in degrees. The crew sit reclined relative to the stack, so in level flight the window looks down at the horizon.")]
        public float cabinTiltDegrees = 30f;
        public float siteFadeStartAltitude = 1500f;
        public float siteFadeEndAltitude = 5500f;

        [Header("Sun and Moon (at liftoff; azimuth is measured clockwise from the downrange direction)")]
        public float sunAzimuth = 115f;
        public float sunElevation = 48f;
        public float moonAzimuth = -13f;
        public float moonElevation = -29f;
        public Color sunColor = new Color(1.0f, 0.96f, 0.90f);
        public float sunIntensityGround = 1.25f;
        public float sunIntensitySpace = 1.7f;

        [Header("Atmosphere")]
        [Tooltip("Altitude over which the daylight sky fades by a factor of e, in kilometres.")]
        public float skyScaleHeightKm = 9f;
        public float starsStartAltitudeKm = 22f;
        public float starsFullAltitudeKm = 75f;
        public Color horizonColor = new Color(0.56f, 0.74f, 0.95f);
        public Color groundAmbient = new Color(0.36f, 0.42f, 0.50f);
        public Color spaceAmbient = new Color(0.015f, 0.018f, 0.025f);
        [Tooltip("Blue light reflected into the cabin from the Earth below when in space.")]
        public Color earthshine = new Color(0.035f, 0.055f, 0.085f);

        [Header("Away from Earth")]
        [Tooltip("Sunlight at Jupiter as a share of sunlight in Earth orbit. Far dimmer in reality; this keeps the cabin readable.")]
        public float deepSpaceSunScale = 0.55f;
        [Tooltip("Strength of the soft light that stands in for sunlight bounced around the cabin, with the Sun full in the forward windows.")]
        public float cabinBounceIntensity = 0.9f;
        [Tooltip("Light reflected into the cabin from Jupiter's clouds.")]
        public Color jupiterShine = new Color(0.085f, 0.070f, 0.050f);

        [Header("Cloud layers (altitude and thickness in metres)")]
        public Vector2[] cloudLayers =
        {
            new Vector2(2200f, 900f),
            new Vector2(6500f, 1600f),
            new Vector2(10500f, 700f)
        };
        public float cloudDriftSpeed = 1f;

        [Header("Launch effects")]
        [Tooltip("Scales every particle emission rate. Lower it on slow machines.")]
        [Range(0.2f, 1.5f)] public float effectsDensity = 1f;
        [Tooltip("Altitude over which humidity falls by a factor of e, in metres. Higher keeps vapour forming for longer.")]
        public float humidityScaleHeight = 6500f;
        [Tooltip("Mach number at which condensation is strongest.")]
        public float vapourPeakMach = 1.0f;
        public float vapourMachWidth = 0.22f;
        [Tooltip("Chase camera position relative to the cockpit, in the level frame: X right of track, Y up, Z downrange.")]
        public Vector3 chaseCameraOffset = new Vector3(260f, -25f, -150f);
        public float chaseCameraFov = 34f;
    }
}
