using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>
    /// A push button. The cap travels when pressed and springs back. Critical buttons can carry a
    /// hinged guard: the first click lifts the guard, the second presses the button.
    /// Whether the button is "on" is shown by its lamp, which reflects real system state.
    /// </summary>
    public class InteractiveButton : Interactable
    {
        [SerializeField] internal Transform cap;
        [SerializeField] internal float pressDepth = 0.006f;
        [SerializeField] internal float pressSpeed = 14f;

        [Header("Guard (optional)")]
        [SerializeField] internal Transform guard;
        [SerializeField] internal float guardOpenAngle = 115f;
        [SerializeField] internal float guardSpeed = 9f;

        private Vector3 capRestPosition;
        private Quaternion guardRestRotation;
        private float press;          // 0 = released, 1 = fully depressed
        private float pressTarget;
        private float guardOpen;      // 0 = closed, 1 = open
        private bool guardRaised;
        private float guardTimer;
        private bool pointerReleased = true;

        public bool HasGuard { get { return guard != null; } }
        public bool GuardRaised { get { return guardRaised; } }

        public override string Prompt
        {
            get { return HasGuard && !guardRaised ? "Click to lift the guard" : "Click to press"; }
        }

        private void Awake()
        {
            if (cap != null) capRestPosition = cap.localPosition;
            if (guard != null) guardRestRotation = guard.localRotation;
        }

        public override void OnPress()
        {
            if (HasGuard && !guardRaised)
            {
                guardRaised = true;
                guardTimer = settings != null ? settings.guardAutoCloseSeconds : 6f;
                Play(InteractionSound.GuardOpen);
                return;
            }

            pressTarget = 1f;
            pointerReleased = false;
            Play(InteractionSound.ButtonPress);
            Emit(1f);

            // A guarded button re-covers itself shortly after use.
            if (HasGuard) guardTimer = 1.2f;
        }

        public override void OnRelease()
        {
            pointerReleased = true;
        }

        protected override void Update()
        {
            base.Update();

            // Cap travel. The cap completes its full stroke even on a quick click, then springs back
            // once the mouse button has been let go.
            if (pointerReleased && press >= 0.99f) pressTarget = 0f;
            press = Mathf.MoveTowards(press, pressTarget, pressSpeed * Time.unscaledDeltaTime);
            if (cap != null) cap.localPosition = capRestPosition + Vector3.forward * (press * pressDepth);

            if (!HasGuard) return;

            if (guardRaised)
            {
                guardTimer -= Time.deltaTime;
                if (guardTimer <= 0f)
                {
                    guardRaised = false;
                    Play(InteractionSound.GuardClose);
                }
            }

            guardOpen = Mathf.MoveTowards(guardOpen, guardRaised ? 1f : 0f, guardSpeed * Time.unscaledDeltaTime);
            // The guard hinges along its top edge; a positive X rotation swings its free edge out and up.
            guard.localRotation = guardRestRotation * Quaternion.Euler(guardOpenAngle * Mathf.SmoothStep(0f, 1f, guardOpen), 0f, 0f);
        }
    }
}
