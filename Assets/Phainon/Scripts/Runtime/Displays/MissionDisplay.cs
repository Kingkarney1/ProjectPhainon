using System.Collections.Generic;
using Phainon.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Phainon.Displays
{
    /// <summary>
    /// Display 3: external view and mission. A live feed from the aft-looking hull camera, the
    /// current objective, overall mission progress, stage status and the flight-event timeline.
    /// </summary>
    public class MissionDisplay : CockpitDisplay
    {
        [Header("External camera")]
        [SerializeField] internal RawImage feed;
        [SerializeField] internal GameObject noSignal;
        [SerializeField] internal TMP_Text feedCaption;
        [SerializeField] internal Image recordLamp;

        [Header("Mission")]
        [SerializeField] internal TMP_Text missionName;
        [SerializeField] internal TMP_Text phaseTitle;
        [SerializeField] internal TMP_Text objective;
        [SerializeField] internal BarGauge progress;
        [SerializeField] internal TMP_Text clock;

        [Header("Stages")]
        [SerializeField] internal StatusRow boosterStatus;
        [SerializeField] internal StatusRow upperStageStatus;
        [SerializeField] internal StatusRow separationStatus;

        [Header("Flight events")]
        [SerializeField] internal StatusRow[] eventRows;
        [Tooltip("Section headings, renamed when the rows show a story mission's science and objectives.")]
        [SerializeField] internal TMP_Text stagesTitle;
        [SerializeField] internal TMP_Text eventsTitle;

        protected override void Start()
        {
            base.Start();
            MissionSpec spec = spacecraft.Craft.Mission.Spec;
            missionName.text = spec.missionName + "\n<size=60%>" + spec.missionSubtitle + "</size>";
        }

        protected override void Refresh(Spacecraft craft, float dt)
        {
            CockpitTheme t = Theme;
            MissionController mission = craft.Mission;

            bool cameraOn = craft.Settings.ExternalCameraOn;
            if (feed.enabled != cameraOn) feed.enabled = cameraOn;
            if (noSignal.activeSelf == cameraOn) noSignal.SetActive(!cameraOn);
            feedCaption.text = (craft.Settings.ExternalCameraView == ExternalView.Chase ? "CAM 2  CHASE" : "CAM 1  AFT HULL")
                               + "    ALT " + Kilometres(craft.Flight.AltitudeMeters) + " KM";
            recordLamp.color = cameraOn && t.BlinkPhase(Time.unscaledTime * 0.5f) ? t.warningRed : t.gaugeTrack;

            phaseTitle.text = mission.PhaseTitle;
            phaseTitle.color = mission.IsFailed ? t.warningRed : (mission.IsAccomplished ? t.nominalGreen : t.accentBlue);
            objective.text = mission.CurrentObjective;
            clock.text = mission.FormatClock();

            float fraction = (float)mission.Progress;
            progress.Set(fraction, mission.IsFailed ? t.warningRed : (mission.IsAccomplished ? t.nominalGreen : t.accentBlue),
                (fraction * 100f).ToString("0") + "%");

            if (craft.Story != null)
            {
                RefreshScience(craft.Science);
                RefreshObjectives(craft.Story);
                return;
            }
            RefreshStages(craft);
            RefreshEvents(mission);
        }

        /// <summary>On a story mission the three rows under the camera show the science payload.</summary>
        private void RefreshScience(ScienceSystem science)
        {
            CockpitTheme t = Theme;
            if (stagesTitle != null) stagesTitle.text = "SCIENCE PAYLOAD";
            InstrumentSpec instrument = science.Selected;
            boosterStatus.SetLabel(instrument != null ? instrument.name + "   " + (science.SelectedCondition * 100.0).ToString("0") + "%" : "NO INSTRUMENT");
            boosterStatus.Set(science.StatusText, science.Recording ? t.nominalGreen : (science.Powered ? t.cautionAmber : t.textDim));

            ObservationSpec shown = science.Active ?? science.NextFor(instrument);
            upperStageStatus.SetLabel(shown != null ? shown.title.ToUpperInvariant() : "OBSERVATION");
            if (science.Active != null) upperStageStatus.Set((science.ActiveProgress * 100.0).ToString("0") + "%", t.nominalGreen);
            else upperStageStatus.Set(shown != null ? "NEXT" : "--", t.textDim);

            separationStatus.SetLabel("DATA STORE");
            separationStatus.Set(science.Transmitting
                    ? "SENDING " + (science.TransmitProgress * 100.0).ToString("0") + "%"
                    : science.TransmittedCount + " SENT OF " + science.CollectedCount,
                science.CollectedCount > science.TransmittedCount ? t.cautionAmber : t.textPrimary);
        }

        /// <summary>On a story mission the event rows list the objectives around the current one.</summary>
        private void RefreshObjectives(StoryController story)
        {
            CockpitTheme t = Theme;
            if (eventsTitle != null) eventsTitle.text = "OBJECTIVES";
            ObjectiveSpec[] objectives = story.Spec.objectives;
            // Keep the current objective in view with a couple of finished ones above it.
            int first = Mathf.Clamp(story.ObjectiveIndex - 2, 0, Mathf.Max(0, objectives.Length - eventRows.Length));
            for (int i = 0; i < eventRows.Length; i++)
            {
                int index = first + i;
                if (index >= objectives.Length)
                {
                    eventRows[i].gameObject.SetActive(false);
                    continue;
                }
                eventRows[i].SetLabel(objectives[index].title.ToUpperInvariant());
                if (index < story.ObjectiveIndex) { eventRows[i].Set("DONE", t.nominalGreen); eventRows[i].SetLabelColor(t.textDim); }
                else if (index == story.ObjectiveIndex) { eventRows[i].Set("NOW", t.accentBlue); eventRows[i].SetLabelColor(t.textPrimary); }
                else { eventRows[i].Set("--", t.textDim); eventRows[i].SetLabelColor(t.textDim); }
            }
        }

        private void RefreshStages(Spacecraft craft)
        {
            CockpitTheme t = Theme;
            EngineSystem engine = craft.Engine;

            // Two rows for three stages: they show the active stage and its neighbour.
            int first = Mathf.Clamp(engine.ActiveStage, 0, Mathf.Max(0, craft.VehicleSpec.stages.Length - 2));
            SetStage(boosterStatus, craft, first);
            SetStage(upperStageStatus, craft, first + 1);

            if (craft.Mission.StageSeparationReady) separationStatus.Set("READY - SEPARATE", t.BlinkPhase(Time.unscaledTime) ? t.cautionAmber : t.textDim);
            else if (engine.ActiveStage == 1 && craft.Mission.IsComplete && !engine.IsBurning) separationStatus.Set("AVAILABLE", t.textPrimary);
            else if (engine.ActiveStage > 0) separationStatus.Set("CONFIRMED", t.nominalGreen);
            else separationStatus.Set("LOCKED", t.textDim);
        }

        private void SetStage(StatusRow row, Spacecraft craft, int stage)
        {
            CockpitTheme t = Theme;
            EngineSystem engine = craft.Engine;
            if (stage >= craft.VehicleSpec.stages.Length) return;
            row.SetLabel(craft.VehicleSpec.stages[stage].name);

            if (stage < engine.ActiveStage) row.Set("JETTISONED", t.textDim);
            else if (stage > engine.ActiveStage) row.Set("STANDBY", t.textDim);
            else if (engine.IsBurning) row.Set("BURNING  " + (craft.Fuel.GetFraction(stage) * 100.0).ToString("0") + "%", t.nominalGreen);
            else if (!craft.Fuel.HasPropellant(stage)) row.Set("SPENT", t.cautionAmber);
            else if (stage == 0) row.Set("ATTACHED", t.textPrimary);
            else row.Set(engine.State == EngineState.Shutdown ? "SHUTDOWN" : "READY", t.textPrimary);
        }

        private void RefreshEvents(MissionController mission)
        {
            CockpitTheme t = Theme;
            IReadOnlyList<MissionEvent> events = mission.Events;

            for (int i = 0; i < eventRows.Length; i++)
            {
                if (i >= events.Count)
                {
                    eventRows[i].gameObject.SetActive(false);
                    continue;
                }

                MissionEvent e = events[i];
                eventRows[i].SetLabel(e.Label);
                switch (e.Status)
                {
                    case MissionEventStatus.Done:
                        eventRows[i].Set("T+" + MissionController.FormatMet(e.MetSeconds), t.nominalGreen);
                        eventRows[i].SetLabelColor(t.textDim);
                        break;
                    case MissionEventStatus.Active:
                        eventRows[i].Set("NEXT", t.accentBlue);
                        eventRows[i].SetLabelColor(t.textPrimary);
                        break;
                    default:
                        eventRows[i].Set("--", t.textDim);
                        eventRows[i].SetLabelColor(t.textDim);
                        break;
                }
            }
        }
    }
}
