using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>
    /// The crew member's head. Holding the look button turns the head within seat limits, number
    /// keys snap to useful views, and the camera shakes with engine thrust and aerodynamic load.
    /// </summary>
    public class CockpitCameraController : MonoBehaviour
    {
        [System.Serializable]
        public struct ViewPreset
        {
            public string name;
            public float yaw;
            public float pitch;
        }

        [SerializeField] internal Camera viewCamera;
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal GameManager game;
        [SerializeField] internal InteractionSettings settings;
        [Tooltip("Views selected with the number keys. Pitch is positive looking down.")]
        [SerializeField] internal ViewPreset[] presets;
        [SerializeField] internal float startYaw;
        [SerializeField] internal float startPitch = 8f;

        private float yaw;
        private float pitch;
        private float targetYaw;
        private float targetPitch;
        private bool snapping;
        private Vector3 restPosition;
        private float impulse;
        private float smoothedVibration;
        private float smoothedG = 1f;

        /// <summary>Current vibration level, 0..1. The audio layer uses it for cabin rumble.</summary>
        public float Vibration { get { return smoothedVibration; } }
        public bool IsLooking { get; private set; }

        private void Awake()
        {
            restPosition = transform.localPosition;
            yaw = targetYaw = startYaw;
            pitch = targetPitch = startPitch;
        }

        private void Start()
        {
            Spacecraft craft = spacecraft.Craft;
            craft.Engine.Ignited += stage => AddImpulse(0.45f);
            craft.Engine.StageSeparated += stage => AddImpulse(1.0f);
            craft.Engine.ShutDown += (stage, reason) => AddImpulse(0.35f);
            craft.Mission.Failed += reason => AddImpulse(1.0f);
        }

        /// <summary>Adds a short jolt, used for ignition, staging and cutoff.</summary>
        public void AddImpulse(float amount) { impulse = Mathf.Max(impulse, amount); }

        private void LateUpdate()
        {
            bool flying = game.State == GameState.Flying;
            PhainonInput input = spacecraft.Input;

            IsLooking = flying && input.LookHold.IsPressed();
            UpdateCursor(flying);

            if (flying)
            {
                if (IsLooking)
                {
                    Vector2 delta = input.LookDelta.ReadValue<Vector2>() * settings.lookSensitivity;
                    yaw = Mathf.Clamp(yaw + delta.x, -settings.yawLimit, settings.yawLimit);
                    pitch = Mathf.Clamp(pitch - delta.y, -settings.pitchUpLimit, settings.pitchDownLimit);
                    snapping = false;
                }

                for (int i = 0; i < input.ViewPresets.Length && presets != null && i < presets.Length; i++)
                {
                    if (!input.ViewPresets[i].WasPressedThisFrame()) continue;
                    targetYaw = presets[i].yaw;
                    targetPitch = presets[i].pitch;
                    snapping = true;
                }

                if (snapping)
                {
                    float t = 1f - Mathf.Exp(-settings.viewSnapSpeed * Time.unscaledDeltaTime);
                    yaw = Mathf.Lerp(yaw, targetYaw, t);
                    pitch = Mathf.Lerp(pitch, targetPitch, t);
                    if (Mathf.Abs(yaw - targetYaw) < 0.05f && Mathf.Abs(pitch - targetPitch) < 0.05f) snapping = false;
                }

                bool zoom = input.Zoom.IsPressed();
                float fov = zoom ? settings.zoomFov : settings.defaultFov;
                viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, fov, 1f - Mathf.Exp(-settings.zoomSpeed * Time.unscaledDeltaTime));
            }

            ApplyPose();
        }

        private void ApplyPose()
        {
            Spacecraft craft = spacecraft.Craft;
            float dt = Time.deltaTime;

            // Vibration comes from engine thrust (strongest in thick air), aerodynamic buffet and one-off jolts.
            float thrust = (float)craft.Engine.OutputFraction;
            float air = (float)craft.Flight.AirDensityFraction;
            float buffet = Mathf.Clamp01((float)(craft.Flight.DynamicPressurePa / craft.SystemsSpec.maxDynamicPressurePa));
            float stageFactor = craft.Engine.ActiveStage == 0 ? 1f : 0.35f;
            float target = Mathf.Clamp01(thrust * stageFactor * (0.35f + 0.45f * air) + buffet * 0.5f + impulse);
            smoothedVibration = Mathf.Lerp(smoothedVibration, target, 1f - Mathf.Exp(-6f * dt));
            impulse = Mathf.MoveTowards(impulse, 0f, dt * 1.6f);

            float time = Time.time * settings.shakeFrequency;
            float nx = Mathf.PerlinNoise(time, 0.3f) - 0.5f;
            float ny = Mathf.PerlinNoise(0.7f, time) - 0.5f;
            float nz = Mathf.PerlinNoise(time, time * 0.7f) - 0.5f;

            Vector3 shakeAngles = new Vector3(nx, ny, nz * 0.6f) * (2f * settings.maxShakeDegrees * smoothedVibration);
            Vector3 shakeOffset = new Vector3(ny, nx, 0f) * (2f * settings.maxShakeMetres * smoothedVibration);

            // Sustained acceleration presses the crew back into the seat.
            smoothedG = Mathf.Lerp(smoothedG, (float)craft.Flight.GLoad, 1f - Mathf.Exp(-2f * dt));
            Vector3 gOffset = new Vector3(0f, -0.004f, -0.010f) * Mathf.Clamp(smoothedG - 1f, 0f, 4f);

            transform.localPosition = restPosition + shakeOffset + gOffset;
            transform.localRotation = Quaternion.Euler(pitch, yaw, 0f) * Quaternion.Euler(shakeAngles);
        }

        private void UpdateCursor(bool flying)
        {
            CursorLockMode wanted = IsLooking ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState != wanted) Cursor.lockState = wanted;
            bool visible = !IsLooking;
            if (Cursor.visible != visible) Cursor.visible = visible;
        }
    }
}
