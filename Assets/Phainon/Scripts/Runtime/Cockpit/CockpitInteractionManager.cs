using System;
using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>
    /// Mouse interaction with the cockpit. Each frame it raycasts from the pointer into the scene,
    /// tracks which control is under it, and forwards press, drag, release and scroll to that control.
    /// While the look button is held the pointer is the centre of the screen, so controls can still
    /// be operated with a crosshair.
    /// </summary>
    public class CockpitInteractionManager : MonoBehaviour
    {
        [SerializeField] internal Camera viewCamera;
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal GameManager game;
        [SerializeField] internal InteractionSettings settings;

        /// <summary>The control under the pointer, or null.</summary>
        public Interactable Hovered { get; private set; }
        /// <summary>The control currently held with the mouse button, or null.</summary>
        public Interactable Active { get; private set; }

        /// <summary>Raised when the hovered control changes (null when leaving a control).</summary>
        public event Action<Interactable> HoverChanged;

        private void Update()
        {
            if (game.State != GameState.Flying)
            {
                ReleaseActive();
                SetHovered(null);
                return;
            }

            PhainonInput input = spacecraft.Input;

            // While a control is being dragged it keeps focus even if the pointer slides off it.
            if (Active == null) SetHovered(FindControlUnderPointer(input));

            if (input.Interact.WasPressedThisFrame() && Hovered != null)
            {
                Active = Hovered;
                Active.OnPress();
            }

            if (Active != null)
            {
                if (Active.SupportsDrag)
                {
                    Vector2 delta = input.LookDelta.ReadValue<Vector2>();
                    if (delta.sqrMagnitude > 0f) Active.OnDrag(delta);
                }
                if (!input.Interact.IsPressed()) ReleaseActive();
            }

            float scroll = input.Scroll.ReadValue<float>();
            if (Mathf.Abs(scroll) > 0.01f && Hovered != null) Hovered.OnScroll(Mathf.Sign(scroll));
        }

        private Interactable FindControlUnderPointer(PhainonInput input)
        {
            Vector2 pointer = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : input.Point.ReadValue<Vector2>();

            Ray ray = viewCamera.ScreenPointToRay(pointer);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, settings.reachDistance, settings.raycastMask, QueryTriggerInteraction.Collide))
                return null;
            return hit.collider.GetComponentInParent<Interactable>();
        }

        private void SetHovered(Interactable next)
        {
            if (next == Hovered) return;
            if (Hovered != null) Hovered.SetHovered(false);
            Hovered = next;
            if (Hovered != null) Hovered.SetHovered(true);
            if (HoverChanged != null) HoverChanged(Hovered);
        }

        private void ReleaseActive()
        {
            if (Active == null) return;
            Active.OnRelease();
            Active = null;
        }
    }
}
