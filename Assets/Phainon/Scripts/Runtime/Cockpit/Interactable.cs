using System;
using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>Sounds a control can ask for. The audio layer decides what each one plays.</summary>
    public enum InteractionSound { ButtonPress, SwitchOn, SwitchOff, DialTick, GuardOpen, GuardClose, LeverMove }

    /// <summary>
    /// Base class for every physical cockpit control. It provides the shared behaviour (identity,
    /// prompt text, hover and tutorial highlighting, events) so buttons, switches, dials and levers
    /// all feel the same. A control knows nothing about spacecraft systems: it only reports a value.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] internal ControlId controlId;
        [SerializeField] internal string label = "CONTROL";
        [Tooltip("Renderers that glow when the control is hovered or suggested by the guide.")]
        [SerializeField] internal Renderer[] highlightRenderers;
        [SerializeField] internal InteractionSettings settings;

        private MaterialPropertyBlock block;
        private bool hovered;
        private bool suggested;
        private Color appliedEmission = new Color(-1f, -1f, -1f);

        public ControlId ControlId { get { return controlId; } }
        public string Label { get { return label; } }

        /// <summary>Raised when the control's value changes. The float is what the spacecraft receives.</summary>
        public event Action<Interactable, float> ValueChanged;
        public event Action<Interactable, InteractionSound> SoundRequested;

        /// <summary>Short instruction shown to the player while hovering, e.g. "Click to press".</summary>
        public abstract string Prompt { get; }
        /// <summary>Text describing the current setting, shown beside the prompt.</summary>
        public virtual string StateText { get { return string.Empty; } }
        /// <summary>The value the spacecraft should start with. Negative means "nothing to send".</summary>
        public virtual float InitialValue { get { return -1f; } }
        /// <summary>True when holding the mouse and dragging adjusts the control.</summary>
        public virtual bool SupportsDrag { get { return false; } }

        public virtual void OnPress() { }
        public virtual void OnDrag(Vector2 pixelDelta) { }
        public virtual void OnRelease() { }
        public virtual void OnScroll(float notches) { }

        public void SetHovered(bool value)
        {
            hovered = value;
            RefreshHighlight();
        }

        /// <summary>Marks the control as the one the guide wants the player to use next.</summary>
        public void SetSuggested(bool value)
        {
            suggested = value;
            RefreshHighlight();
        }

        protected virtual void Update()
        {
            // Only the suggested control animates, so idle controls cost nothing per frame.
            if (suggested && !hovered) RefreshHighlight();
        }

        protected void Emit(float value)
        {
            if (ValueChanged != null) ValueChanged(this, value);
        }

        protected void Play(InteractionSound sound)
        {
            if (SoundRequested != null) SoundRequested(this, sound);
        }

        private void RefreshHighlight()
        {
            if (highlightRenderers == null || settings == null) return;

            Color emission = Color.black;
            if (hovered)
            {
                emission = settings.hoverEmission;
            }
            else if (suggested)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * settings.suggestPulseHz * Mathf.PI * 2f);
                emission = settings.suggestEmission * (0.25f + 0.75f * pulse);
            }

            if (emission == appliedEmission) return;
            appliedEmission = emission;

            if (block == null) block = new MaterialPropertyBlock();
            for (int i = 0; i < highlightRenderers.Length; i++)
            {
                Renderer target = highlightRenderers[i];
                if (target == null) continue;
                target.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, emission);
                target.SetPropertyBlock(block);
            }
        }
    }
}
