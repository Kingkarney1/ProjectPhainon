using System;
using System.Collections.Generic;
using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>
    /// Connects the physical controls to the spacecraft. Every control's value change becomes a
    /// <see cref="Spacecraft.HandleControl"/> call, and the spacecraft's answer is published so the
    /// HUD and audio can give feedback. This is the only place the two sides meet.
    /// </summary>
    public class CockpitControlRouter : MonoBehaviour
    {
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal GameManager game;
        [SerializeField] internal InteractionSettings settings;
        [SerializeField] internal Interactable[] controls;
        [SerializeField] internal ThrottleLever throttle;

        private readonly Dictionary<ControlId, Interactable> byId = new Dictionary<ControlId, Interactable>();
        private Interactable suggested;
        private bool guideEnabled = true;

        /// <summary>A crew command was refused; the string says why.</summary>
        public event Action<ControlId, string> CommandRejected;
        /// <summary>A crew command was carried out and has something worth announcing.</summary>
        public event Action<ControlId, string> CommandAccepted;
        public event Action<Interactable, InteractionSound> ControlSound;

        public bool GuideEnabled { get { return guideEnabled; } }

        private void Start()
        {
            Spacecraft craft = spacecraft.Craft;
            craft.CommandHandled += OnCommandHandled;

            // Controls added under the cockpit later (for example from the control prefabs) are picked
            // up automatically, so the serialized list never has to be maintained by hand.
            var all = new List<Interactable>(controls ?? new Interactable[0]);
            foreach (Interactable found in GetComponentsInChildren<Interactable>(true))
                if (!all.Contains(found)) all.Add(found);
            controls = all.ToArray();

            for (int i = 0; i < controls.Length; i++)
            {
                Interactable control = controls[i];
                if (control == null) continue;
                if (control.settings == null) control.settings = settings;
                byId[control.ControlId] = control;
                control.ValueChanged += OnControlValueChanged;
                control.SoundRequested += OnControlSound;

                // Bring the simulation into line with how the panel is physically set at start-up.
                float initial = control.InitialValue;
                if (initial >= 0f) craft.HandleControl(control.ControlId, initial);
            }
        }

        private void OnDestroy()
        {
            if (spacecraft != null && spacecraft.Craft != null) spacecraft.Craft.CommandHandled -= OnCommandHandled;
        }

        private void Update()
        {
            if (game.State != GameState.Flying)
            {
                SetSuggested(null);
                return;
            }

            PhainonInput input = spacecraft.Input;

            if (throttle != null)
            {
                float axis = input.ThrottleAxis.ReadValue<float>();
                if (Mathf.Abs(axis) > 0.01f) throttle.Nudge(axis * settings.throttleKeyRate * Time.deltaTime);
                if (input.ThrottleFull.WasPressedThisFrame()) throttle.SetValue(1f);
                if (input.ThrottleCut.WasPressedThisFrame()) throttle.SetValue(0f);
            }

            if (input.ToggleGuide.WasPressedThisFrame()) guideEnabled = !guideEnabled;

            // Point the player at the control the mission wants next.
            Interactable next = null;
            if (guideEnabled) byId.TryGetValue(spacecraft.Craft.Mission.SuggestedControl, out next);
            SetSuggested(next);
        }

        public Interactable Find(ControlId id)
        {
            Interactable control;
            return byId.TryGetValue(id, out control) ? control : null;
        }

        private void SetSuggested(Interactable next)
        {
            if (next == suggested) return;
            if (suggested != null) suggested.SetSuggested(false);
            suggested = next;
            if (suggested != null) suggested.SetSuggested(true);
        }

        private void OnControlValueChanged(Interactable control, float value)
        {
            spacecraft.Craft.HandleControl(control.ControlId, value);
        }

        private void OnControlSound(Interactable control, InteractionSound sound)
        {
            if (ControlSound != null) ControlSound(control, sound);
        }

        private void OnCommandHandled(ControlId id, CommandResult result)
        {
            if (!result.Accepted)
            {
                if (CommandRejected != null) CommandRejected(id, result.Message);
            }
            else if (!string.IsNullOrEmpty(result.Message))
            {
                if (CommandAccepted != null) CommandAccepted(id, result.Message);
            }
        }
    }
}
