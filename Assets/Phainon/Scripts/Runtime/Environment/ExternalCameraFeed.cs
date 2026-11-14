using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Environment
{
    /// <summary>
    /// The external camera behind the mission display. It has two views: the hull camera looking aft
    /// down the stack, and a chase camera riding alongside. It renders into a small render texture,
    /// and only every few frames, so the feed costs a fraction of the main view. The EXT CAM switch
    /// and bus power turn it off entirely.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class ExternalCameraFeed : MonoBehaviour
    {
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal GameManager game;
        [SerializeField] internal EnvironmentConfig config;
        [SerializeField] internal Camera feedCamera;
        [Tooltip("The world root, whose axes are the level frame: the chase camera keeps the horizon level.")]
        [SerializeField] internal Transform levelFrame;
        [SerializeField] internal Transform vehicle;
        [Tooltip("Point on the stack the chase camera aims at, as a distance aft of the cockpit in metres.")]
        [SerializeField] internal float chaseTargetAft = 30f;
        [Tooltip("Render one frame of the feed every N frames of the main view.")]
        [SerializeField] internal int frameInterval = 2;
        [SerializeField] internal float shakeDegrees = 0.5f;

        private Vector3 hullPosition;
        private Quaternion hullRotation;
        private float hullFov;

        private void Awake()
        {
            hullPosition = feedCamera.transform.localPosition;
            hullRotation = feedCamera.transform.localRotation;
            hullFov = feedCamera.fieldOfView;
            feedCamera.enabled = false;
        }

        // LateUpdate, after EnvironmentDirector has rotated the world for this frame.
        private void LateUpdate()
        {
            Spacecraft craft = spacecraft.Craft;
            bool live = craft.Power.IsBusPowered && craft.Settings.ExternalCameraOn && game.State != GameState.Paused;
            bool renderThisFrame = live && Time.frameCount % Mathf.Max(1, frameInterval) == 0;
            if (feedCamera.enabled != renderThisFrame) feedCamera.enabled = renderThisFrame;
            if (!renderThisFrame) return;

            Transform view = feedCamera.transform;
            float shake = shakeDegrees;
            if (craft.Settings.ExternalCameraView == ExternalView.Chase)
            {
                view.position = levelFrame.TransformPoint(config.chaseCameraOffset);
                Vector3 target = vehicle.TransformPoint(0f, 0f, -chaseTargetAft);
                view.rotation = Quaternion.LookRotation(target - view.position, levelFrame.up);
                feedCamera.fieldOfView = config.chaseCameraFov;
                shake *= 0.3f;      // a long lens on a steady platform
            }
            else
            {
                view.localPosition = hullPosition;
                view.localRotation = hullRotation;
                feedCamera.fieldOfView = hullFov;
            }

            // The camera rattles with engine output.
            float output = (float)craft.Engine.OutputFraction;
            float time = Time.time * 31f;
            Vector3 jitter = new Vector3(Mathf.PerlinNoise(time, 0.1f) - 0.5f, Mathf.PerlinNoise(0.4f, time) - 0.5f, 0f);
            view.rotation *= Quaternion.Euler(jitter * (2f * shake * output));
        }
    }
}
