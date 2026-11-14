using System.Collections.Generic;
using Phainon.Simulation;
using TMPro;
using UnityEngine;

namespace Phainon.Displays
{
    /// <summary>
    /// Display 2: spacecraft systems. Propulsion, electrical, communications and cabin environment,
    /// the pre-launch GO / NO-GO board and the live caution and warning list.
    /// </summary>
    public class SystemsDisplay : CockpitDisplay
    {
        [Header("Propulsion")]
        [SerializeField] internal StatusRow engineStatus;
        [SerializeField] internal TMP_Text stageName;
        [SerializeField] internal BarGauge thrust;
        [SerializeField] internal ValueReadout throttleCommand;
        [SerializeField] internal ValueReadout thrustLimit;
        [SerializeField] internal ValueReadout chamberTemp;
        [SerializeField] internal BarGauge boosterFuel;
        [SerializeField] internal BarGauge upperFuel;
        [SerializeField] internal BarGauge feedPressure;

        [Header("Electrical")]
        [SerializeField] internal ValueReadout busVoltage;
        [SerializeField] internal ValueReadout busLoad;
        [SerializeField] internal StatusRow apuStatus;
        [SerializeField] internal BarGauge batteryA;
        [SerializeField] internal BarGauge batteryB;

        [Header("Communications")]
        [SerializeField] internal StatusRow commStatus;
        [SerializeField] internal BarGauge signal;

        [Header("Cabin")]
        [SerializeField] internal BarGauge oxygen;
        [SerializeField] internal ValueReadout cabinPressure;
        [SerializeField] internal ValueReadout cabinTemp;
        [SerializeField] internal ValueReadout cabinSetpoint;
        [SerializeField] internal StatusRow cabinStatus;

        [Header("GO / NO-GO board")]
        [SerializeField] internal TMP_Text boardTitle;
        [SerializeField] internal StatusRow[] checklistRows;

        [Header("Caution and warning")]
        [SerializeField] internal TMP_Text[] warningRows;
        [SerializeField] internal TMP_Text warningSummary;

        protected override void Refresh(Spacecraft craft, float dt)
        {
            RefreshPropulsion(craft);
            RefreshElectrical(craft);
            RefreshCabin(craft);
            RefreshChecklist(craft);
            RefreshWarnings(craft);
        }

        private void RefreshPropulsion(Spacecraft craft)
        {
            CockpitTheme t = Theme;
            EngineSystem engine = craft.Engine;
            SystemsSpec limits = craft.SystemsSpec;

            switch (engine.State)
            {
                case EngineState.Igniting: engineStatus.Set("IGNITION", t.cautionAmber); break;
                case EngineState.Running: engineStatus.Set("RUNNING", t.nominalGreen); break;
                case EngineState.Shutdown: engineStatus.Set("CUTOFF", t.cautionAmber); break;
                case EngineState.Depleted: engineStatus.Set("DEPLETED", t.warningRed); break;
                default: engineStatus.Set(engine.IgnitionInterlockOpen || engine.ActiveStage > 0 ? "ARMED" : "SAFE", t.textDim); break;
            }
            stageName.text = "STAGE " + (engine.ActiveStage + 1) + "  " + engine.ActiveStageSpec.name;

            float output = (float)engine.OutputFraction;
            thrust.Set(output, engine.IsBurning ? t.nominalGreen : t.textDim, (output * 100f).ToString("0") + "%");
            throttleCommand.Set((engine.ThrottleCommand * 100.0).ToString("0"));
            thrustLimit.Set((engine.ThrustLimit * 100.0).ToString("0"),
                engine.ThrustLimit < engine.ThrottleCommand - 0.005 ? t.cautionAmber : t.textPrimary);
            chamberTemp.Set(engine.ChamberTemperatureK.ToString("0"));

            // Two gauges for three stages: they show the active stage and its neighbour.
            int first = Mathf.Clamp(engine.ActiveStage, 0, Mathf.Max(0, craft.VehicleSpec.stages.Length - 2));
            SetFuel(boosterFuel, craft, first, limits);
            SetFuel(upperFuel, craft, first + 1, limits);

            float feed = (float)craft.Fuel.FeedPressure;
            feedPressure.Set(feed, craft.Fuel.FeedReady ? t.nominalGreen : t.cautionAmber, (feed * 100f).ToString("0") + "%");
        }

        private void SetFuel(BarGauge gauge, Spacecraft craft, int stage, SystemsSpec limits)
        {
            CockpitTheme t = Theme;
            if (stage >= craft.VehicleSpec.stages.Length) return;
            gauge.SetLabel("FUEL  " + craft.VehicleSpec.stages[stage].name);
            if (stage < craft.Engine.ActiveStage)
            {
                gauge.Set(0f, t.textDim, "JETTISONED");
                return;
            }
            float fraction = (float)craft.Fuel.GetFraction(stage);
            Color color = fraction < limits.lowFuelFraction ? t.warningRed
                : (fraction < limits.lowFuelFraction * 2.5 ? t.cautionAmber : t.nominalGreen);
            gauge.Set(fraction, color, (fraction * 100f).ToString("0.0") + "%");
        }

        private void RefreshElectrical(Spacecraft craft)
        {
            CockpitTheme t = Theme;
            PowerSystem power = craft.Power;
            SystemsSpec limits = craft.SystemsSpec;

            busVoltage.Set(power.BusVoltage.ToString("0.0"), power.BusVoltage < limits.lowBusVoltage ? t.cautionAmber : t.nominalGreen);
            busLoad.Set((power.LoadWatts / 1000.0).ToString("0.00"));

            if (power.ApuOnline) apuStatus.Set("ONLINE", t.nominalGreen);
            else if (power.ApuEnabled) apuStatus.Set("STARTING " + (power.ApuSpoolFraction * 100.0).ToString("0") + "%", t.cautionAmber);
            else apuStatus.Set("OFF", t.textDim);

            SetBattery(batteryA, power.BatteryACharge, power.BatteryAEnabled, limits);
            SetBattery(batteryB, power.BatteryBCharge, power.BatteryBEnabled, limits);

            CommsSystem comms = craft.Comms;
            if (comms.LinkEstablished) commStatus.Set("LINKED", t.nominalGreen);
            else if (comms.Enabled && comms.AntennaEnabled) commStatus.Set("ACQUIRING", t.cautionAmber);
            else if (comms.Enabled) commStatus.Set("XMIT OFF", t.cautionAmber);
            else commStatus.Set("OFF", t.textDim);
            float strength = (float)comms.SignalStrength;
            signal.Set(strength, strength > 0.5f ? t.nominalGreen : t.cautionAmber, (strength * 100f).ToString("0") + "%");
        }

        private void SetBattery(BarGauge gauge, double charge, bool enabled, SystemsSpec limits)
        {
            CockpitTheme t = Theme;
            Color color = !enabled ? t.textDim : (charge < limits.lowBatteryFraction ? t.warningRed : t.nominalGreen);
            gauge.Set((float)charge, color, enabled ? (charge * 100.0).ToString("0") + "%" : "OFF  " + (charge * 100.0).ToString("0") + "%");
        }

        private void RefreshCabin(Spacecraft craft)
        {
            CockpitTheme t = Theme;
            LifeSupportSystem cabin = craft.LifeSupport;
            SystemsSpec limits = craft.SystemsSpec;

            float o2 = (float)cabin.OxygenFraction;
            oxygen.Set(o2, o2 < limits.lowOxygenFraction ? t.warningRed : t.nominalGreen, (o2 * 100f).ToString("0.0") + "%");
            cabinPressure.Set(cabin.CabinPressureKpa.ToString("0.0"),
                cabin.CabinPressureKpa < limits.lowCabinPressureKpa ? t.cautionAmber : t.nominalGreen);
            cabinTemp.Set(cabin.CabinTemperatureC.ToString("0.0"),
                cabin.CabinTemperatureC > limits.highCabinTempC ? t.cautionAmber : t.textPrimary);
            cabinSetpoint.Set(cabin.TemperatureSetpointC.ToString("0.0"));

            if (cabin.Nominal) cabinStatus.Set("NOMINAL", t.nominalGreen);
            else if (cabin.Enabled) cabinStatus.Set("PRESSURISING", t.cautionAmber);
            else cabinStatus.Set("OFF", t.textDim);
        }

        private void RefreshChecklist(Spacecraft craft)
        {
            CockpitTheme t = Theme;
            MissionController mission = craft.Mission;
            IReadOnlyList<ChecklistItem> items = mission.Checklist;

            if (mission.Phase == MissionPhase.PreLaunch)
            {
                boardTitle.text = mission.AllGo ? "LAUNCH STATUS: GO" : "LAUNCH STATUS: NO-GO  " + mission.GoCount + "/" + items.Count;
                boardTitle.color = t.GoColor(mission.AllGo);
            }
            else
            {
                boardTitle.text = "SYSTEMS STATUS";
                boardTitle.color = t.textDim;
            }

            for (int i = 0; i < checklistRows.Length; i++)
            {
                if (i >= items.Count)
                {
                    checklistRows[i].gameObject.SetActive(false);
                    continue;
                }
                checklistRows[i].SetLabel(items[i].Label);
                checklistRows[i].Set(items[i].Status, t.GoColor(items[i].IsGo));
            }
        }

        private void RefreshWarnings(Spacecraft craft)
        {
            CockpitTheme t = Theme;
            IReadOnlyList<WarningEntry> active = craft.Warnings.Active;
            bool blink = t.BlinkPhase(Time.unscaledTime);

            for (int i = 0; i < warningRows.Length; i++)
            {
                TMP_Text row = warningRows[i];
                if (i < active.Count)
                {
                    WarningEntry entry = active[i];
                    row.text = entry.Message;
                    Color color = t.ForSeverity(entry.Severity);
                    // Unacknowledged red warnings flash until the crew press MASTER ALARM.
                    if (entry.Severity == WarningSeverity.Warning && craft.Warnings.MasterAlarmActive && !blink) color.a = 0.35f;
                    row.color = color;
                }
                else
                {
                    row.text = string.Empty;
                }
            }

            if (active.Count == 0)
            {
                warningSummary.text = "NO ACTIVE ALERTS";
                warningSummary.color = t.nominalGreen;
            }
            else
            {
                int hidden = Mathf.Max(0, active.Count - warningRows.Length);
                warningSummary.text = active.Count + " ACTIVE" + (hidden > 0 ? "  (+" + hidden + " MORE)" : string.Empty);
                warningSummary.color = t.ForSeverity(active[0].Severity);
            }
        }
    }
}
