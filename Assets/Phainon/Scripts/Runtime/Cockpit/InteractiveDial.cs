using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>
    /// A rotary knob. Continuous dials report a 0..1 value; detented dials click between a fixed set
    /// of positions and report the position index. Drag or scroll to turn.
    /// </summary>
    public class InteractiveDial : Interactable
    {
        [SerializeField] internal Transform knob;
        [SerializeField] internal float minAngle = -135f;
        [SerializeField] internal float maxAngle = 135f;
        [Tooltip("Number of click positions. Zero or one makes the dial continuous.")]
        [SerializeField] internal int detents;
        [SerializeField] internal string[] detentLabels;
        [Range(0f, 1f)]
        [SerializeField] internal float value = 0.5f;
        [Tooltip("How the value is written in the hover prompt, e.g. \"{0:0}%\" with the range below.")]
        [SerializeField] internal string valueFormat = "{0:0}%";
        [SerializeField] internal float displayMin;
        [SerializeField] internal float displayMax = 100f;
        [SerializeField] internal float turnSpeed = 14f;

        private float dragAccumulator;
        private float shownValue;
        private int lastTickStep;

        public bool IsDetented { get { return detents > 1; } }
        public float Value { get { return value; } }
        public int DetentIndex { get { return IsDetented ? Mathf.RoundToInt(value * (detents - 1)) : 0; } }

        public override bool SupportsDrag { get { return true; } }
        public override string Prompt { get { return "Drag or scroll to turn"; } }
        public override float InitialValue { get { return IsDetented ? DetentIndex : value; } }

        public override string StateText
        {
            get
            {
                if (IsDetented)
                {
                    int index = DetentIndex;
                    return detentLabels != null && index < detentLabels.Length ? detentLabels[index] : index.ToString();
                }
                return string.Format(valueFormat, Mathf.Lerp(displayMin, displayMax, value));
            }
        }

        private void Awake()
        {
            if (IsDetented) value = DetentIndex / (float)(detents - 1);
            shownValue = value;
            lastTickStep = Mathf.RoundToInt(value * 20f);
            ApplyPose();
        }

        public override void SetWithoutNotify(float next)
        {
            value = IsDetented ? Mathf.Clamp(Mathf.Round(next), 0f, detents - 1) / (detents - 1) : Mathf.Clamp01(next);
            shownValue = value;
            lastTickStep = Mathf.RoundToInt(value * 20f);
            ApplyPose();
        }

        public override void OnDrag(Vector2 pixelDelta)
        {
            float sensitivity = settings != null ? settings.dialDragSensitivity : 0.0035f;
            // Right or up turns the knob clockwise, matching how a real knob is rolled with the fingertips.
            Turn((pixelDelta.x + pixelDelta.y) * sensitivity);
        }

        public override void OnScroll(float notches)
        {
            if (IsDetented) StepDetent(notches > 0f ? 1 : -1);
            else Turn(notches * (settings != null ? settings.dialScrollStep : 0.05f));
        }

        public override void OnRelease() { dragAccumulator = 0f; }

        private void Turn(float delta)
        {
            if (IsDetented)
            {
                // Accumulate drag until it crosses half a detent, then click over.
                dragAccumulator += delta;
                float stepSize = 1f / (detents - 1);
                while (dragAccumulator > stepSize * 0.5f) { dragAccumulator -= stepSize; StepDetent(1); }
                while (dragAccumulator < -stepSize * 0.5f) { dragAccumulator += stepSize; StepDetent(-1); }
                return;
            }

            float next = Mathf.Clamp01(value + delta);
            if (Mathf.Approximately(next, value)) return;
            value = next;

            int tickStep = Mathf.RoundToInt(value * 20f);
            if (tickStep != lastTickStep)
            {
                lastTickStep = tickStep;
                Play(InteractionSound.DialTick);
            }
            Emit(value);
        }

        private void StepDetent(int direction)
        {
            int index = Mathf.Clamp(DetentIndex + direction, 0, detents - 1);
            if (index == DetentIndex) return;
            value = index / (float)(detents - 1);
            Play(InteractionSound.DialTick);
            Emit(index);
        }

        protected override void Update()
        {
            base.Update();
            if (Mathf.Approximately(shownValue, value)) return;
            shownValue = Mathf.MoveTowards(shownValue, value, turnSpeed * Time.unscaledDeltaTime);
            ApplyPose();
        }

        private void ApplyPose()
        {
            // Negative Z rotation reads as clockwise to a viewer in front of the panel.
            if (knob != null) knob.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Lerp(minAngle, maxAngle, shownValue));
        }
    }
}
