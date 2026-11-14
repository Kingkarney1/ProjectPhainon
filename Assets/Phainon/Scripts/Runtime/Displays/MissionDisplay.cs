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

            phaseTitle.text = mission.Spec.GetPhaseInfo(mission.Phase).title;
            phaseTitle.color = mission.IsFailed ? t.warningRed : (mission.IsComplete ? t.nominalGreen : t.accentBlue);
            objective.text = mission.CurrentObjective;
            clock.text = mission.FormatClock();

            float fraction = (float)mission.Progress;
            progress.Set(fraction, mission.IsFailed ? t.warningRed : (mission.IsComplete ? t.nominalGreen : t.accentBlue),
                (fraction * 100f).ToString("0") + "%");

            RefreshStages(craft);
            RefreshEvents(mission);
        }

        private void RefreshStages(Spacecraft craft)
        {
            CockpitTheme t = Theme;
            EngineSystem engine = craft.Engine;
            bool separated = engine.ActiveStage > 0;

            if (separated) boosterStatus.Set("JETTISONED", t.textDim);
            else if (engine.IsBurning) boosterStatus.Set("BURNING  " + (craft.Fuel.GetFraction(0) * 100.0).ToString("0") + "%", t.nominalGreen);
            else if (!craft.Fuel.HasPropellant(0)) boosterStatus.Set("SPENT", t.cautionAmber);
            else boosterStatus.Set("ATTACHED", t.textPrimary);

            if (!separated) upperStageStatus.Set("STANDBY", t.textDim);
            else if (engine.IsBurning) upperStageStatus.Set("BURNING  " + (craft.Fuel.GetFraction(1) * 100.0).ToString("0") + "%", t.nominalGreen);
            else if (!craft.Fuel.HasPropellant(1)) upperStageStatus.Set("SPENT", t.cautionAmber);
            else upperStageStatus.Set(craft.Mission.IsComplete ? "SHUTDOWN" : "READY", t.textPrimary);

            if (separated) separationStatus.Set("CONFIRMED", t.nominalGreen);
            else if (craft.Mission.StageSeparationReady) separationStatus.Set("READY - SEPARATE", t.BlinkPhase(Time.unscaledTime) ? t.cautionAmber : t.textDim);
            else separationStatus.Set("LOCKED", t.textDim);
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
