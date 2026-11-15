using System;
using System.Collections.Generic;

namespace Phainon.Simulation
{
    public enum WarningSeverity { Advisory = 0, Caution = 1, Warning = 2 }

    public enum WarningId
    {
        BusPowerLoss, BatteryLow, ApuOffline, FuelLow, DynamicPressureHigh, HighG,
        CabinPressureLow, OxygenLow, CabinTempHigh, CommLoss, NavNotAligned,
        EngineFlameout, StageSepRequired, TrajectoryLow,
        // Appended, never reordered: authored radio lines store these by number.
        FeedPressureDropping, BatteryFault, ThrustLow,
        CabinAirLow, CabinTempLow, OxygenVenting, BurnWindow
    }

    [Serializable]
    public class WarningSnapshot
    {
        public int[] ids = new int[0];
        public int[] severities = new int[0];
        public string[] messages = new string[0];
        public bool masterAlarm;
    }

    public class WarningEntry
    {
        public WarningId Id;
        public WarningSeverity Severity;
        public string Message;
        public bool Active;
    }

    /// <summary>
    /// Caution and warning annunciator. Conditions are evaluated elsewhere and reported through
    /// <see cref="Set"/>; this class tracks what is active and latches the master alarm.
    /// </summary>
    public class WarningSystem
    {
        private readonly Dictionary<WarningId, WarningEntry> entries = new Dictionary<WarningId, WarningEntry>();
        private readonly List<WarningEntry> active = new List<WarningEntry>();

        public WarningSystem()
        {
            foreach (WarningId id in Enum.GetValues(typeof(WarningId)))
                entries[id] = new WarningEntry { Id = id, Message = id.ToString() };
        }

        /// <summary>Active entries, most severe first.</summary>
        public IReadOnlyList<WarningEntry> Active { get { return active; } }

        /// <summary>Latched when a red warning appears; cleared by the crew or when no warning remains.</summary>
        public bool MasterAlarmActive { get; private set; }
        public bool AnyWarning { get; private set; }
        public bool AnyCaution { get; private set; }

        public event Action<WarningEntry> Raised;
        public event Action<WarningEntry> Cleared;

        public bool IsActive(WarningId id) { return entries[id].Active; }

        public void Set(WarningId id, bool isActive, WarningSeverity severity, string message)
        {
            WarningEntry entry = entries[id];
            if (isActive)
            {
                bool escalated = entry.Active && severity > entry.Severity;
                entry.Severity = severity;
                entry.Message = message;
                if (!entry.Active || escalated)
                {
                    if (!entry.Active) active.Add(entry);
                    entry.Active = true;
                    Resort();
                    if (severity == WarningSeverity.Warning) MasterAlarmActive = true;
                    if (Raised != null) Raised(entry);
                }
            }
            else if (entry.Active)
            {
                entry.Active = false;
                active.Remove(entry);
                Resort();
                if (Cleared != null) Cleared(entry);
            }
        }

        public WarningSnapshot Capture()
        {
            var s = new WarningSnapshot { masterAlarm = MasterAlarmActive };
            s.ids = new int[active.Count];
            s.severities = new int[active.Count];
            s.messages = new string[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                s.ids[i] = (int)active[i].Id;
                s.severities[i] = (int)active[i].Severity;
                s.messages[i] = active[i].Message;
            }
            return s;
        }

        /// <summary>Puts the annunciator back as it was, without announcing anything a second time.</summary>
        public void Restore(WarningSnapshot s)
        {
            foreach (WarningEntry entry in active) entry.Active = false;
            active.Clear();
            for (int i = 0; i < s.ids.Length; i++)
            {
                WarningEntry entry;
                if (!entries.TryGetValue((WarningId)s.ids[i], out entry)) continue;
                entry.Active = true;
                entry.Severity = (WarningSeverity)s.severities[i];
                entry.Message = s.messages[i];
                active.Add(entry);
            }
            Resort();
            MasterAlarmActive = s.masterAlarm && AnyWarning;
        }

        /// <summary>Crew acknowledgement: silences the master alarm without clearing the conditions.</summary>
        public CommandResult Acknowledge()
        {
            if (!MasterAlarmActive) return CommandResult.Rejected("NO ACTIVE ALARM");
            MasterAlarmActive = false;
            return CommandResult.Ok("MASTER ALARM ACKNOWLEDGED");
        }

        private void Resort()
        {
            active.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            AnyWarning = false;
            AnyCaution = false;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Severity == WarningSeverity.Warning) AnyWarning = true;
                else if (active[i].Severity == WarningSeverity.Caution) AnyCaution = true;
            }
            if (!AnyWarning) MasterAlarmActive = false;
        }
    }
}
