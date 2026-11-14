using UnityEngine;

namespace Phainon
{
    /// <summary>Tunable feel of the cockpit interaction and the first-person camera.</summary>
    [CreateAssetMenu(menuName = "Phainon/Interaction Settings", fileName = "InteractionSettings")]
    public class InteractionSettings : ScriptableObject
    {
        [Header("Reach")]
        public float reachDistance = 2.2f;
        public LayerMask raycastMask = ~0;

        [Header("Control feel")]
        [Tooltip("Dial travel per pixel of mouse drag, as a fraction of full range.")]
        public float dialDragSensitivity = 0.0035f;
        [Tooltip("Dial travel per scroll notch, as a fraction of full range.")]
        public float dialScrollStep = 0.05f;
        public float leverDragSensitivity = 0.004f;
        public float leverScrollStep = 0.05f;
        [Tooltip("Throttle travel per second when using the keyboard.")]
        public float throttleKeyRate = 0.45f;
        public float guardAutoCloseSeconds = 6f;

        [Header("Highlight")]
        [ColorUsage(false, true)] public Color hoverEmission = new Color(0.10f, 0.32f, 0.55f);
        [ColorUsage(false, true)] public Color suggestEmission = new Color(0.55f, 0.36f, 0.04f);
        public float suggestPulseHz = 1.4f;

        [Header("Camera")]
        public float lookSensitivity = 0.11f;
        public float yawLimit = 120f;
        public float pitchUpLimit = 70f;
        public float pitchDownLimit = 72f;
        public float defaultFov = 66f;
        public float zoomFov = 30f;
        public float zoomSpeed = 9f;
        public float viewSnapSpeed = 5f;

        [Header("Camera vibration")]
        public float maxShakeDegrees = 0.9f;
        public float maxShakeMetres = 0.012f;
        public float shakeFrequency = 21f;
    }
}
