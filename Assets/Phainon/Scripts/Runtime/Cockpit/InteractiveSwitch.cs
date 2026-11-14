using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>A two-position toggle switch. The lever visibly sits up for on and down for off.</summary>
    public class InteractiveSwitch : Interactable
    {
        [SerializeField] internal Transform lever;
        [SerializeField] internal float onAngle = 28f;
        [SerializeField] internal float offAngle = -28f;
        [SerializeField] internal float throwSpeed = 16f;
        [SerializeField] internal bool isOn;

        private float position;   // 0 = off, 1 = on

        public bool IsOn { get { return isOn; } }
        public override string Prompt { get { return isOn ? "Click to switch off" : "Click to switch on"; } }
        public override string StateText { get { return isOn ? "ON" : "OFF"; } }
        public override float InitialValue { get { return isOn ? 1f : 0f; } }

        private void Awake()
        {
            position = isOn ? 1f : 0f;
            ApplyPose();
        }

        public override void OnPress()
        {
            isOn = !isOn;
            Play(isOn ? InteractionSound.SwitchOn : InteractionSound.SwitchOff);
            Emit(isOn ? 1f : 0f);
        }

        protected override void Update()
        {
            base.Update();
            float target = isOn ? 1f : 0f;
            if (Mathf.Approximately(position, target)) return;
            position = Mathf.MoveTowards(position, target, throwSpeed * Time.unscaledDeltaTime);
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (lever != null) lever.localRotation = Quaternion.Euler(Mathf.Lerp(offAngle, onAngle, position), 0f, 0f);
        }
    }
}
