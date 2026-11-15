using System;
using System.Collections.Generic;
using System.Globalization;

namespace Phainon.Simulation
{
    [Serializable]
    public class StorySnapshot
    {
        public string missionId;
        public int objectiveIndex;
        public bool complete;
        public JournalEntry[] journal = new JournalEntry[0];
        /// <summary>Mission time at which each scheduled fault falls due; negative until its clock starts.</summary>
        public double[] faultDue = new double[0];
        public bool[] faultFired = new bool[0];
        public double deadBusSeconds;
    }

    /// <summary>
    /// Runs one story mission: works through its objectives against the real state of the
    /// spacecraft, keeps the log, breaks what the mission says will break and decides when the
    /// mission is lost. It holds no story text of its own; all of that is in the mission's data.
    /// </summary>
    public class StoryController
    {
        private readonly StoryMissionSpec spec;
        private readonly Spacecraft craft;
        private readonly double[] faultDue;
        private readonly bool[] faultFired;
        private double deadBusSeconds;
        private bool begun;

        public StoryController(StoryMissionSpec spec, Spacecraft craft)
        {
            this.spec = spec;
            this.craft = craft;
            Journal = new Journal();
            faultDue = new double[spec.faults.Length];
            faultFired = new bool[spec.faults.Length];
            for (int i = 0; i < faultDue.Length; i++) faultDue[i] = -1.0;

            Journal.EntryRead += entry => craft.Flags.Add("read:" + entry.id);
            craft.Science.ObservationCompleted += OnObservationCompleted;
            craft.Science.RecordTransmitted += OnRecordTransmitted;
            craft.GroundControl.MessageReceived += OnMessageReceived;
        }

        public StoryMissionSpec Spec { get { return spec; } }
        public Journal Journal { get; private set; }
        public int ObjectiveIndex { get; private set; }
        public bool IsComplete { get; private set; }
        public ObjectiveSpec Current { get { return ObjectiveIndex < spec.objectives.Length ? spec.objectives[ObjectiveIndex] : null; } }
        public double Progress { get { return spec.objectives.Length > 0 ? (double)ObjectiveIndex / spec.objectives.Length : 1.0; } }

        /// <summary>Heading for the guide and the displays: where in the mission the crew are.</summary>
        public string StageTitle
        {
            get
            {
                ObjectiveSpec current = Current;
                if (current != null && !string.IsNullOrEmpty(current.stage)) return current.stage;
                return IsComplete ? "MISSION COMPLETE" : spec.title;
            }
        }

        public event Action<ObjectiveSpec> ObjectiveCompleted;
        public event Action Completed;

        // ---------------------------------------------------------------- orbit

        /// <summary>True when the orbit is closed and inside this mission's limits.</summary>
        public bool OrbitStable
        {
            get
            {
                FlightModel flight = craft.Flight;
                return flight.IsBound && flight.PeriapsisMeters >= spec.orbitMinPeriapsisMeters
                       && flight.ApoapsisMeters <= spec.orbitMaxApoapsisMeters;
            }
        }

        /// <summary>True when the burn has done its job and the engine should be cut.</summary>
        public bool CutoffAdvised { get { return OrbitStable && craft.Engine.IsBurning; } }

        /// <summary>True on the way in, close enough to periapsis that the crew should be getting ready to burn.</summary>
        public bool BurnWindowOpen
        {
            get
            {
                FlightModel flight = craft.Flight;
                if (flight.IsBound || flight.RadialVelocity >= 0.0 || craft.Engine.IsBurning) return false;
                double toPeriapsis = flight.TimeToPeriapsisSeconds;
                return !double.IsNaN(toPeriapsis) && toPeriapsis <= spec.burnWindowWarningSeconds;
            }
        }

        /// <summary>Speed to lose at periapsis for an orbit comfortably inside the mission's limits, m/s.</summary>
        public double CaptureDeltaV
        {
            get
            {
                FlightModel flight = craft.Flight;
                double periapsis = flight.Body.radiusMeters + flight.PeriapsisMeters;
                double target = flight.Body.radiusMeters + spec.orbitMaxApoapsisMeters * 0.85;
                double dv = OrbitMath.CaptureDeltaV(flight.Body.mu, flight.SpecificEnergy, periapsis, target);
                return double.IsNaN(dv) ? 0.0 : Math.Max(0.0, dv);
            }
        }

        // ---------------------------------------------------------------- time step

        /// <summary>Opens the mission: the records that are there from the start, and the first story event.</summary>
        public void Begin()
        {
            if (begun) return;
            begun = true;
            UnlockJournal();
            craft.GroundControl.NotifyStoryEvent("wake");
        }

        public void Tick(double dt)
        {
            if (craft.Mission.IsFailed) return;
            if (!begun) Begin();

            UnlockJournal();

            // An objective that is already satisfied when its turn comes is passed straight over.
            int guard = spec.objectives.Length;
            while (!IsComplete && guard-- > 0)
            {
                ObjectiveSpec current = Current;
                if (current == null)
                {
                    Finish();
                    break;
                }
                if (!Satisfied(current)) break;
                Complete(current);
            }

            TickFaults();
            TickLosses(dt);
        }

        private void Complete(ObjectiveSpec objective)
        {
            ObjectiveIndex++;
            if (!string.IsNullOrEmpty(objective.setsFlag)) craft.Flags.Add(objective.setsFlag);
            craft.Flags.Add("done:" + objective.id);
            Journal.Add("objective:" + objective.id, JournalCategory.MissionEvent, JournalReliability.Confirmed,
                objective.title, "Completed at " + craft.Mission.FormatClock() + ".", craft.Mission.FormatClock());

            for (int i = 0; i < spec.faults.Length; i++)
                if (spec.faults[i].afterObjectiveId == objective.id && faultDue[i] < 0.0)
                    faultDue[i] = craft.Mission.MissionElapsedSeconds + spec.faults[i].delaySeconds;

            craft.GroundControl.NotifyStoryEvent("objective:" + objective.id);
            if (ObjectiveCompleted != null) ObjectiveCompleted(objective);
        }

        private void Finish()
        {
            IsComplete = true;
            craft.Flags.Add("mission_complete:" + spec.id);
            craft.GroundControl.NotifyStoryEvent("mission_complete");
            if (Completed != null) Completed();
        }

        private void TickFaults()
        {
            for (int i = 0; i < spec.faults.Length; i++)
            {
                if (faultFired[i] || faultDue[i] < 0.0) continue;
                if (craft.Mission.MissionElapsedSeconds < faultDue[i] || !craft.Faults.CanOccur(spec.faults[i].fault)) continue;
                faultFired[i] = true;
                craft.Faults.Inject(spec.faults[i].fault);
            }
        }

        private void TickLosses(double dt)
        {
            FlightModel flight = craft.Flight;
            if (!flight.IsBound && flight.RadialVelocity > 0.0 && flight.Radius > spec.escapeFailRadiusMeters)
            {
                craft.Mission.Fail(flight.Body.name + " fell behind. Without a capture burn the spacecraft could not stay, and there is not the propellant to come back.");
                return;
            }

            // With no battery and no APU there is nothing left to restart anything with.
            bool dead = !craft.Power.HasBatterySource && !craft.Power.ApuOnline;
            deadBusSeconds = dead ? deadBusSeconds + dt : 0.0;
            if (deadBusSeconds > 30.0)
                craft.Mission.Fail("The batteries ran flat before the APU was restarted. The spacecraft went dark.");
        }

        private void UnlockJournal()
        {
            for (int i = 0; i < spec.journal.Length; i++)
            {
                JournalEntrySpec entry = spec.journal[i];
                if (!string.IsNullOrEmpty(entry.unlockFlag) && !craft.Flags.Contains(entry.unlockFlag)) continue;
                if (Journal.Find(entry.id) != null) continue;
                Journal.Add(entry.id, entry.category, entry.reliability, entry.title, entry.body, craft.Mission.FormatClock());
            }
        }

        // ---------------------------------------------------------------- events from the spacecraft

        private void OnObservationCompleted(ObservationRecord record, ObservationSpec observation)
        {
            bool poor = record.quality < 45.0 && !string.IsNullOrEmpty(observation.poorFinding);
            string body = (poor ? observation.poorFinding : observation.finding) + "\n\nData quality " + record.quality.ToString("0", CultureInfo.InvariantCulture) + " of 100.";
            Journal.Add("observation:" + observation.id,
                observation.anomaly && !poor ? JournalCategory.Anomaly : JournalCategory.Discovery,
                poor ? JournalReliability.Unverified : JournalReliability.Confirmed,
                observation.title, body, craft.Mission.FormatClock());
            craft.Flags.Add("observed:" + observation.id);
            if (!poor && !string.IsNullOrEmpty(observation.setsFlag)) craft.Flags.Add(observation.setsFlag);
            craft.GroundControl.NotifyStoryEvent("observation:" + observation.id);
        }

        private void OnRecordTransmitted(ObservationRecord record)
        {
            craft.Flags.Add("sent:" + record.observationId);
            craft.GroundControl.NotifyStoryEvent("sent:" + record.observationId);
        }

        private void OnMessageReceived(CommsMessage message)
        {
            if (message.Source == null || !message.Source.logged) return;
            Journal.Add("radio:" + message.Source.id, JournalCategory.Communication,
                message.Source.garbled ? JournalReliability.Corrupted : JournalReliability.Confirmed,
                message.SpeakerRole + "  " + message.SpeakerName, message.Text, message.Clock);
        }

        /// <summary>The crew opened a log entry.</summary>
        public void MarkRead(string journalId) { Journal.MarkRead(journalId); }

        // ---------------------------------------------------------------- objectives

        private bool Satisfied(ObjectiveSpec objective)
        {
            switch (objective.condition)
            {
                case ObjectiveCondition.MasterAlarmSilenced: return !craft.Warnings.MasterAlarmActive;
                case ObjectiveCondition.ApuOnline: return craft.Power.ApuOnline;
                case ObjectiveCondition.CabinNominal: return craft.LifeSupport.Nominal;
                case ObjectiveCondition.NavAligned: return craft.Navigation.Aligned;
                case ObjectiveCondition.LinkEstablished: return craft.Comms.LinkEstablished;
                case ObjectiveCondition.RecordRead:
                    JournalEntry entry = Journal.Find(objective.parameter);
                    return entry != null && entry.read;
                case ObjectiveCondition.NavModeSelected: return (int)craft.Navigation.Mode == (int)objective.value;
                case ObjectiveCondition.FeedReady: return craft.Fuel.FeedReady;
                case ObjectiveCondition.AttitudeHeld: return AttitudeHeld;
                case ObjectiveCondition.OrbitStable: return OrbitStable && !craft.Engine.IsBurning && craft.Engine.ThrustNewtons <= 0.0;
                case ObjectiveCondition.InstrumentPowered: return craft.Science.Powered;
                case ObjectiveCondition.ObservationsCollected: return craft.Science.CollectedCount >= (int)objective.value;
                case ObjectiveCondition.DataTransmitted:
                    return craft.Science.CollectedCount > 0 && craft.Science.TransmittedCount == craft.Science.CollectedCount;
                case ObjectiveCondition.FlagSet: return craft.Flags.Contains(objective.parameter);
                default: return false;
            }
        }

        private bool AttitudeHeld
        {
            get
            {
                NavMode mode = craft.Navigation.Mode;
                if (mode != NavMode.Orbit && mode != NavMode.Retro) return false;
                if (!craft.Navigation.GuidanceActive || !craft.Attitude.HasControlAuthority) return false;
                return Math.Abs(SimMath.WrapDegrees(craft.Attitude.GuidancePitchDegrees - craft.Attitude.PitchDegrees)) < 2.0;
            }
        }

        /// <summary>What the crew should do next, and the control it refers to.</summary>
        public void GetGuide(out string text, out ControlId control)
        {
            control = ControlId.None;
            ObjectiveSpec objective = Current;
            if (objective == null)
            {
                text = string.IsNullOrEmpty(spec.completionText) ? "Mission objectives complete." : spec.completionText;
                ApplyFaultAdvice(ref text, ref control);
                return;
            }

            if (objective.repairFault >= 0)
            {
                ControlId repair;
                string procedure = craft.Faults.GetAdvice((FaultId)objective.repairFault, out repair);
                if (procedure != null)
                {
                    text = procedure;
                    control = repair;
                    return;
                }
            }

            text = objective.text;
            control = objective.control;
            switch (objective.condition)
            {
                case ObjectiveCondition.CabinNominal:
                    if (craft.LifeSupport.Enabled) { text = "Cabin loop running. Pressure is coming back up."; control = ControlId.None; }
                    break;
                case ObjectiveCondition.NavAligned:
                    if (craft.Navigation.Enabled) { text = "Nav platform aligning. It needs a few seconds without power interruptions."; control = ControlId.None; }
                    break;
                case ObjectiveCondition.FeedReady:
                    control = !craft.Fuel.PumpsEnabled ? ControlId.FuelPump : (!craft.Fuel.TankPressEnabled ? ControlId.TankPress : ControlId.None);
                    break;
                case ObjectiveCondition.AttitudeHeld:
                    if (!craft.Navigation.GuidanceEnabled) control = ControlId.Guidance;
                    else if (!craft.Attitude.RcsEnabled) control = ControlId.Rcs;
                    else if (craft.Navigation.Mode != NavMode.Orbit && craft.Navigation.Mode != NavMode.Retro) control = ControlId.NavMode;
                    else { text = "The spacecraft is turning. Wait for it to settle on the flight path."; control = ControlId.None; }
                    break;
                case ObjectiveCondition.OrbitStable:
                    CaptureGuide(objective, out text, out control);
                    break;
                case ObjectiveCondition.ObservationsCollected:
                    ScienceSystem science = craft.Science;
                    if (science.Active != null) control = ControlId.None;
                    else if (science.NextFor(science.Selected) == null) control = ControlId.InstrumentSelect;
                    else control = science.Blocker(science.NextFor(science.Selected)) == null ? ControlId.Observe : ControlId.None;
                    break;
                case ObjectiveCondition.DataTransmitted:
                    if (craft.Science.Transmitting) { text = "Downlink running. Hold the link until the last record has gone."; control = ControlId.None; }
                    break;
            }

            text = Fill(text, objective);
            if (objective.yieldsToFaults) ApplyFaultAdvice(ref text, ref control);
        }

        /// <summary>A repair step comes before an objective that has nothing for the crew to operate, and before any step it could block.</summary>
        private void ApplyFaultAdvice(ref string text, ref ControlId control)
        {
            ControlId repair;
            string advice = craft.Faults.GetAdvice(out repair);
            if (advice != null && (repair != ControlId.None || control == ControlId.None))
            {
                text = advice;
                control = repair;
            }
        }

        /// <summary>The steps of getting into orbit, chosen from where the spacecraft is on its path.</summary>
        private void CaptureGuide(ObjectiveSpec objective, out string text, out ControlId control)
        {
            FlightModel flight = craft.Flight;
            EngineSystem engine = craft.Engine;
            control = ControlId.None;

            if (engine.IsBurning)
            {
                if (OrbitStable) { text = "ORBIT GO. Press ENGINE CUTOFF, or burn on for a tighter orbit at the cost of propellant."; control = ControlId.EngineCutoff; }
                else if (flight.IsBound) text = "Captured. Hold the burn until ORBIT GO lights: apoapsis is still {apo} km.";
                else text = "Capture burn. Hold it. {capture} m/s still to lose.";
                return;
            }

            if (flight.IsBound)
            {
                // Closed, but outside the limits: say which way.
                if (flight.PeriapsisMeters < spec.orbitMinPeriapsisMeters)
                    text = "In orbit, but periapsis is only {peri} km. Turn NAV MODE to ORBIT and burn near apoapsis to raise it.";
                else
                    text = "In orbit, but apoapsis is {apo} km. With NAV MODE on RETRO, burn again near periapsis to bring it down. Periapsis in {tpe}.";
                return;
            }

            if (flight.RadialVelocity >= 0.0)
            {
                text = "Past periapsis and still on an escape path. Burn now, against the flight path, while there is speed left to lose cheaply.";
                control = engine.ThrottleCommand < 0.9 ? ControlId.Throttle : ControlId.EngineIgnition;
                return;
            }

            if (engine.ThrottleCommand < 0.9)
            {
                text = "Set the throttle to full for the capture burn.";
                control = ControlId.Throttle;
                return;
            }

            double toPeriapsis = flight.TimeToPeriapsisSeconds;
            double lead = LeadSeconds;
            if (!double.IsNaN(toPeriapsis) && toPeriapsis <= lead + 5.0)
            {
                text = "Lift the guard and press ENGINE IGNITION now. Hold the burn until ORBIT GO lights.";
                control = ControlId.EngineIgnition;
                return;
            }
            text = objective.text;
        }

        /// <summary>Seconds before periapsis to light the engine so the burn straddles it.</summary>
        private double LeadSeconds { get { return BurnSeconds * 0.5; } }

        private double BurnSeconds
        {
            get
            {
                StageSpec stage = craft.Engine.ActiveStageSpec;
                return OrbitMath.BurnSeconds(CaptureDeltaV, stage.specificImpulseSeconds, craft.Flight.MassKg, stage.thrustNewtons);
            }
        }

        private string Fill(string text, ObjectiveSpec objective)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            FlightModel flight = craft.Flight;
            CultureInfo c = CultureInfo.InvariantCulture;
            return text
                .Replace("{tpe}", ScienceSystem.Duration(flight.TimeToPeriapsisSeconds))
                .Replace("{lead}", LeadSeconds.ToString("0", c) + " S")
                .Replace("{burn}", BurnSeconds.ToString("0", c) + " S")
                .Replace("{capture}", CaptureDeltaV.ToString("0", c))
                .Replace("{dv}", craft.AvailableDeltaV.ToString("0", c))
                .Replace("{peri}", (flight.PeriapsisMeters / 1000.0).ToString("N0", c))
                .Replace("{apo}", double.IsInfinity(flight.ApoapsisMeters) ? "--" : (flight.ApoapsisMeters / 1000.0).ToString("N0", c))
                .Replace("{science}", craft.Science.StatusText)
                .Replace("{count}", craft.Science.CollectedCount.ToString(c))
                .Replace("{need}", ((int)objective.value).ToString(c));
        }

        /// <summary>The same text helper for guidance built in code.</summary>
        public string CurrentGuideText
        {
            get
            {
                string text;
                ControlId control;
                GetGuide(out text, out control);
                return text;
            }
        }

        // ---------------------------------------------------------------- save state

        public StorySnapshot Capture()
        {
            return new StorySnapshot
            {
                missionId = spec.id, objectiveIndex = ObjectiveIndex, complete = IsComplete, journal = Journal.Capture(),
                faultDue = (double[])faultDue.Clone(), faultFired = (bool[])faultFired.Clone(), deadBusSeconds = deadBusSeconds
            };
        }

        public void Restore(StorySnapshot s)
        {
            ObjectiveIndex = (int)SimMath.Clamp(s.objectiveIndex, 0, spec.objectives.Length);
            IsComplete = s.complete;
            Journal.Restore(s.journal);
            for (int i = 0; i < faultDue.Length && i < s.faultDue.Length; i++) faultDue[i] = s.faultDue[i];
            for (int i = 0; i < faultFired.Length && i < s.faultFired.Length; i++) faultFired[i] = s.faultFired[i];
            deadBusSeconds = s.deadBusSeconds;
            begun = true;
        }
    }
}
