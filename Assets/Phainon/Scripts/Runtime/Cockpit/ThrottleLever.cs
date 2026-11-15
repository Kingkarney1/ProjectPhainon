using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>
    /// The main engine throttle lever. It travels between an idle and a full stop and reports its
    /// position as 0..1. It can be dragged, scrolled or driven from the keyboard.
    /// </summary>
    public class ThrottleLever : Interactable
    {
        [SerializeField] internal Transform pivot;
        [SerializeField] internal float idleAngle = -32f;
        [SerializeField] internal float fullAngle = 32f;
        [Range(0f, 1f)]
        [SerializeField] internal float value;
        [SerializeField] internal float followSpeed = 6f;

        private float shownValue;
        private int lastDetent;

        public float Value { get { return value; } }
        public override bool SupportsDrag { get { return true; } }
        public override string Prompt { get { return "Drag or scroll to move  (Shift / Ctrl)"; } }
        public override string StateText { get { return Mathf.RoundToInt(value * 100f) + "%"; } }
        public override float InitialValue { get { return value; } }

        private void Awake()
        {
            shownValue = value;
            lastDetent = Mathf.RoundToInt(value * 10f);
            ApplyPose();
        }

        public override void SetWithoutNotify(float next)
        {
            value = Mathf.Clamp01(next);
            shownValue = value;
            lastDetent = Mathf.RoundToInt(value * 10f);
            ApplyPose();
        }

        public override void OnDrag(Vector2 pixelDelta)
        {
            float sensitivity = settings != null ? settings.leverDragSensitivity : 0.004f;
            SetValue(value + pixelDelta.y * sensitivity);
        }

        public override void OnScroll(float notches)
        {
            SetValue(value + notches * (settings != null ? settings.leverScrollStep : 0.05f));
        }

        /// <summary>Moves the lever by a signed amount. Used for keyboard throttle control.</summary>
        public void Nudge(float delta) { SetValue(value + delta); }

        public void SetValue(float next)
        {
            next = Mathf.Clamp01(next);
            if (Mathf.Approximately(next, value)) return;
            value = next;

            // A soft click every ten percent gives the lever a mechanical feel without constant noise.
            int detent = Mathf.RoundToInt(value * 10f);
            if (detent != lastDetent)
            {
                lastDetent = detent;
                Play(InteractionSound.LeverMove);
            }
            Emit(value);
        }

        protected override void Update()
        {
            base.Update();
            if (Mathf.Approximately(shownValue, value)) return;
            shownValue = Mathf.MoveTowards(shownValue, value, followSpeed * Time.unscaledDeltaTime);
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (pivot != null) pivot.localRotation = Quaternion.Euler(Mathf.Lerp(idleAngle, fullAngle, shownValue), 0f, 0f);
        }
    }
}
