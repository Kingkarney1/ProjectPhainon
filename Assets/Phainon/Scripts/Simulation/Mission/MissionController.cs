using System;
using System.Collections.Generic;

namespace Phainon.Simulation
{
    /// <summary>
    /// Structured mission flow: the pre-launch GO / NO-GO board, the launch sequencer, phase
    /// transitions from the pad to orbit, the flight-event timeline and the failure cases.
    /// </summary>
    public class MissionController
    {
        private readonly MissionSpec spec;
        private readonly Spacecraft craft;
        private readonly List<ChecklistItem> checklist = new List<ChecklistItem>();
        private readonly List<MissionEvent> events = new List<MissionEvent>();
        private double peakDynamicPressure;
        private ChecklistItem holdItem;

        public MissionController(MissionSpec spec, Spacecraft craft)
        {
            this.spec = spec;
            this.craft = craft;
            CountdownRemaining = spec.countdownSeconds;
            BuildChecklist();
            BuildEvents();

            craft.Engine.Ignited += OnEngineIgnited;
            craft.Engine.ShutDown += OnEngineShutDown;
            craft.Engine.StageSeparated += OnStageSeparated;
        }

        public MissionSpec Spec { get { return spec; } }
        public MissionPhase Phase { get; private set; }
        public LaunchSequenceState LaunchState { get; private set; }
        public double CountdownRemaining { get; private set; }
        /// <summary>Seconds since liftoff. Zero until the clamps release.</summary>
        public double MissionElapsedSeconds { get; private set; }
        public bool HasLiftedOff { get { return craft.Flight.Released; } }
        public bool OrbitGo { get; private set; }
        public bool IsComplete { get { return Phase == MissionPhase.OuterSpace; } }
        public bool IsFailed { get { return Phase == MissionPhase.Failed; } }
        public string FailureReason { get; private set; }

        public IReadOnlyList<ChecklistItem> Checklist { get { return checklist; } }
        public IReadOnlyList<MissionEvent> Events { get { return events; } }

        /// <summary>What the crew should do next, in plain words.</summary>
        public string CurrentObjective { get; private set; }
        /// <summary>The control the current objective refers to, so the cockpit can point at it.</summary>
        public ControlId SuggestedControl { get; private set; }

        public bool AllGo
        {
            get
            {
                for (int i = 0; i < checklist.Count; i++) if (!checklist[i].IsGo) return false;
                return true;
            }
        }

        public int GoCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < checklist.Count; i++) if (checklist[i].IsGo) n++;
                return n;
            }
        }

        public bool StageSeparationReady
        {
            get
            {
                return Phase == MissionPhase.StageSeparation && craft.Engine.ActiveStage == 0 && !craft.Engine.IsBurning;
            }
        }

        public event Action<MissionPhase> PhaseChanged;
        public event Action<MissionEvent> EventReached;
        /// <summary>Short crew-facing notifications (call-outs from the flight computer).</summary>
        public event Action<string> Notification;
        public event Action Completed;
        public event Action<string> Failed;
        /// <summary>Raised once per whole second of the countdown, with the seconds remaining.</summary>
        public event Action<int> CountdownTick;

        // ---------------------------------------------------------------- crew commands

        public CommandResult ToggleLaunchSequence()
        {
            if (Phase != MissionPhase.PreLaunch) return CommandResult.Rejected("LAUNCH SEQUENCE COMPLETE");

            if (LaunchState != LaunchSequenceState.Idle)
            {
                if (craft.Engine.IsBurning) return CommandResult.Rejected("CANNOT RECYCLE - ENGINE RUNNING. USE CUTOFF");
                LaunchState = LaunchSequenceState.Idle;
                CountdownRemaining = spec.countdownSeconds;
                craft.Engine.IgnitionInterlockOpen = false;
                Notify("LAUNCH SEQUENCE RECYCLED");
                return CommandResult.Ok("LAUNCH SEQUENCE RECYCLED");
            }

            RefreshChecklist();
            if (!AllGo)
            {
                ChecklistItem first = FirstNoGo();
                return CommandResult.Rejected("LAUNCH INHIBITED - " + first.Label + " NO-GO");
            }

            LaunchState = LaunchSequenceState.Counting;
            CountdownRemaining = spec.countdownSeconds;
            Notify("LAUNCH SEQUENCE STARTED");
            return CommandResult.Ok("LAUNCH SEQUENCE STARTED");
        }

        public CommandResult RequestStageSeparation()
        {
            if (craft.Engine.IsFinalStage) return CommandResult.Rejected("NO STAGE TO SEPARATE");
            if (!HasLiftedOff) return CommandResult.Rejected("SEPARATION INHIBITED - ON PAD");
            if (craft.Flight.AltitudeMeters < spec.stageSeparationMinAltitudeMeters)
                return CommandResult.Rejected("SEPARATION INHIBITED - BELOW " + (int)(spec.stageSeparationMinAltitudeMeters / 1000) + " KM");
            return craft.Engine.SeparateStage();
        }

        public CommandResult Abort()
        {
            if (Phase == MissionPhase.Failed) return CommandResult.Rejected("ABORT ALREADY INITIATED");
            if (Phase == MissionPhase.OuterSpace) return CommandResult.Rejected("ABORT NOT AVAILABLE IN ORBIT");
            if (Phase == MissionPhase.PreLaunch && LaunchState == LaunchSequenceState.Idle && !craft.Engine.IsBurning)
                return CommandResult.Rejected("ABORT NOT ARMED - NO LAUNCH SEQUENCE");

            craft.Engine.Cutoff(EngineShutdownReason.Abort);
            Fail(HasLiftedOff
                ? "Abort initiated. The crew escape system pulled the capsule clear of the launch vehicle."
                : "Launch aborted on the pad. The vehicle has been safed.");
            return CommandResult.Ok("ABORT INITIATED");
        }

        // ---------------------------------------------------------------- simulation

        /// <summary>Runs before the engine tick so the ignition interlock is current.</summary>
        public void PreTick(double dt)
        {
            RefreshChecklist();
            if (Phase == MissionPhase.PreLaunch) TickLaunchSequence(dt);
        }

        /// <summary>Runs after flight integration to evaluate phase transitions.</summary>
        public void PostTick(double dt)
        {
            if (HasLiftedOff && Phase != MissionPhase.Failed) MissionElapsedSeconds += dt;

            switch (Phase)
            {
                case MissionPhase.PreLaunch: TickLiftoffCheck(); break;
                case MissionPhase.Ascent: TickAscent(); break;
                case MissionPhase.StageSeparation: TickStageSeparation(); break;
                case MissionPhase.OrbitalInsertion: TickOrbitalInsertion(); break;
            }

            UpdateObjective();
        }

        private void TickLaunchSequence(double dt)
        {
            if (LaunchState == LaunchSequenceState.Idle || LaunchState == LaunchSequenceState.Complete) return;

            bool go = AllGo;
            if (LaunchState == LaunchSequenceState.Counting && !go)
            {
                LaunchState = LaunchSequenceState.Hold;
                holdItem = FirstNoGo();
                Notify("HOLD - " + holdItem.Label + " NO-GO");
            }
            else if (LaunchState == LaunchSequenceState.Hold && go)
            {
                LaunchState = LaunchSequenceState.Counting;
                Notify("COUNT RESUMED");
            }

            if (LaunchState == LaunchSequenceState.Counting)
            {
                int before = (int)Math.Ceiling(CountdownRemaining);
                CountdownRemaining = Math.Max(0.0, CountdownRemaining - dt);
                int after = (int)Math.Ceiling(CountdownRemaining);
                if (after != before && CountdownTick != null) CountdownTick(after);
                if (CountdownRemaining <= 0.0) LaunchState = LaunchSequenceState.AwaitingIgnition;
            }

            bool inWindow = LaunchState != LaunchSequenceState.Hold && CountdownRemaining <= spec.ignitionWindowSeconds;
            if (inWindow && !craft.Engine.IgnitionInterlockOpen) Notify("IGNITION INTERLOCK OPEN");
            craft.Engine.IgnitionInterlockOpen = inWindow;

            if (CountdownRemaining <= 0.0 && LaunchState != LaunchSequenceState.Hold)
                LaunchState = craft.Engine.IsBurning ? LaunchSequenceState.AwaitingThrust : LaunchSequenceState.AwaitingIgnition;
        }

        private void TickLiftoffCheck()
        {
            if (CountdownRemaining > 0.0 || LaunchState == LaunchSequenceState.Idle) return;

            double weight = craft.Flight.MassKg * EngineSystem.StandardGravity;
            if (weight > 0 && craft.Engine.ThrustNewtons / weight >= spec.liftoffThrustToWeight)
            {
                craft.Flight.ReleaseClamps();
                LaunchState = LaunchSequenceState.Complete;
                MissionElapsedSeconds = 0.0;
                MarkEvent(MissionEventId.Liftoff);
                SetActive(MissionEventId.MaxQ);
                SetPhase(MissionPhase.Ascent);
                Notify("LIFTOFF");
            }
        }

        private void TickAscent()
        {
            FlightModel flight = craft.Flight;

            // Max-Q is called once dynamic pressure has clearly passed its peak.
            if (flight.DynamicPressurePa > peakDynamicPressure) peakDynamicPressure = flight.DynamicPressurePa;
            if (GetEvent(MissionEventId.MaxQ).Status != MissionEventStatus.Done
                && peakDynamicPressure > spec.maxQDetectMinPa
                && flight.DynamicPressurePa < peakDynamicPressure * 0.9)
            {
                MarkEvent(MissionEventId.MaxQ);
                SetActive(MissionEventId.BoosterCutoff);
                Notify("VEHICLE IS THROUGH MAX-Q");
            }

            if (flight.Impacted) { Fail("The vehicle fell back and was lost on impact."); return; }

            bool boosterSpent = craft.Engine.ActiveStage == 0 && !craft.Engine.IsBurning
                                && !craft.Fuel.HasPropellant(0);
            bool cutHighEnough = craft.Engine.ActiveStage == 0 && !craft.Engine.IsBurning
                                 && flight.AltitudeMeters >= spec.stageSeparationMinAltitudeMeters;
            if (boosterSpent || cutHighEnough)
            {
                MarkEvent(MissionEventId.MaxQ);
                MarkEvent(MissionEventId.BoosterCutoff);
                SetActive(MissionEventId.StageSeparation);
                SetPhase(MissionPhase.StageSeparation);
                Notify("BOOSTER CUTOFF - STAGE SEPARATION REQUIRED");
            }
        }

        private void TickStageSeparation()
        {
            if (CheckTrajectoryLoss()) return;

            // The crew may restart the booster if they cut it early and it still has propellant.
            if (craft.Engine.ActiveStage == 0 && craft.Engine.IsBurning)
            {
                SetPhase(MissionPhase.Ascent);
                return;
            }

            if (craft.Engine.ActiveStage > 0 && craft.Engine.IsBurning)
            {
                MarkEvent(MissionEventId.UpperStageIgnition);
                SetActive(MissionEventId.OrbitGo);
                SetPhase(MissionPhase.OrbitalInsertion);
            }
        }

        private void TickOrbitalInsertion()
        {
            if (CheckTrajectoryLoss()) return;

            FlightModel flight = craft.Flight;
            bool go = flight.PeriapsisMeters >= spec.orbitMinPeriapsisMeters;
            if (go && !OrbitGo)
            {
                OrbitGo = true;
                MarkEvent(MissionEventId.OrbitGo);
                SetActive(MissionEventId.OrbitAchieved);
                Notify("ORBIT GO - CUT OFF THE ENGINE");
            }
            else if (!go)
            {
                OrbitGo = false;
            }

            if (OrbitGo && !craft.Engine.IsBurning)
            {
                MarkEvent(MissionEventId.OrbitAchieved);
                SetPhase(MissionPhase.OuterSpace);
                Notify("ORBIT ACHIEVED");
                if (Completed != null) Completed();
                return;
            }

            if (!OrbitGo && !craft.Engine.IsBurning && !craft.Fuel.HasPropellant(craft.Engine.ActiveStage))
                Fail("Propellant was exhausted before reaching orbital velocity.");
        }

        private bool CheckTrajectoryLoss()
        {
            FlightModel flight = craft.Flight;
            if (flight.Impacted || (flight.AltitudeMeters < spec.reentryFailAltitudeMeters && flight.VerticalSpeed < 0))
            {
                Fail("The trajectory decayed and the vehicle fell back into the atmosphere.");
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- engine events

        private void OnEngineIgnited(int stage)
        {
            Notify(stage == 0 ? "MAIN ENGINE IGNITION" : "UPPER STAGE IGNITION");
        }

        private void OnEngineShutDown(int stage, EngineShutdownReason reason)
        {
            switch (reason)
            {
                case EngineShutdownReason.PropellantDepleted:
                    Notify(stage == 0 ? "BOOSTER PROPELLANT DEPLETED" : "UPPER STAGE PROPELLANT DEPLETED");
                    break;
                case EngineShutdownReason.FeedPressureLost:
                    Notify("ENGINE FLAMEOUT - FEED PRESSURE LOST");
                    break;
                case EngineShutdownReason.Commanded:
                    Notify("ENGINE CUTOFF");
                    break;
            }
        }

        private void OnStageSeparated(int droppedStage)
        {
            MarkEvent(MissionEventId.BoosterCutoff);
            MarkEvent(MissionEventId.StageSeparation);
            SetActive(MissionEventId.UpperStageIgnition);
            if (Phase == MissionPhase.Ascent) SetPhase(MissionPhase.StageSeparation);
            Notify("STAGE SEPARATION CONFIRMED");
        }

        // ---------------------------------------------------------------- checklist and objective

        private void BuildChecklist()
        {
            Add("BATTERIES", "Set the BATT A and BATT B switches ON. They are on the overhead panel (key 6).", ControlId.BatteryA);
            Add("MAIN BUS", "Press MAIN POWER to energise the main bus.", ControlId.MainPower);
            Add("AUX POWER", "Set the APU switch ON (overhead panel, key 6) and wait for it to come online.", ControlId.Apu);
            Add("PROPELLANT", "Propellant load is below launch minimum.", ControlId.None);
            Add("FUEL FEED", "Set FUEL PUMP and TANK PRESS ON and wait for feed pressure.", ControlId.FuelPump);
            Add("NAV PLATFORM", "Press NAV SYSTEM and wait for the platform to align.", ControlId.NavSystem);
            Add("NAV MODE", "Turn the NAV MODE dial to ASCENT.", ControlId.NavMode);
            Add("GUIDANCE", "Set the GUIDANCE switch ON.", ControlId.Guidance);
            Add("COMMS LINK", "Set S-BAND XMIT ON and press COMMS to acquire the link.", ControlId.CommAntenna);
            Add("CABIN", "Press CABIN SYS to start the cabin environment loop.", ControlId.CabinSystems);
        }

        private void Add(string label, string hint, ControlId control)
        {
            checklist.Add(new ChecklistItem { Label = label, Hint = hint, Control = control, Status = "NO-GO" });
        }

        private void RefreshChecklist()
        {
            PowerSystem power = craft.Power;
            Set(0, power.BatteryAEnabled && power.BatteryBEnabled, power.BatteryAEnabled || power.BatteryBEnabled ? "ONE OFF" : "OFF");
            Set(1, power.IsBusPowered, "OFF");
            Set(2, power.ApuOnline, power.ApuEnabled && power.IsBusPowered ? "STARTING" : "OFF");
            // Before liftoff this is the launch-commit load check; in flight it only fails when the active stage is dry.
            bool propellantGo = HasLiftedOff
                ? craft.Fuel.HasPropellant(craft.Engine.ActiveStage)
                : craft.Fuel.GetFraction(0) > 0.97 && craft.Fuel.GetFraction(1) > 0.97;
            Set(3, propellantGo, HasLiftedOff ? "DEPLETED" : "LOW");
            Set(4, craft.Fuel.FeedReady, craft.Fuel.FeedPressure > 0.01 ? "PRESSURISING" : "OFF");
            Set(5, craft.Navigation.Aligned, craft.Navigation.Enabled ? "ALIGNING" : "OFF");
            Set(6, craft.Navigation.Mode == NavMode.Ascent, craft.Navigation.Mode == NavMode.Standby ? "STBY" : "ORBIT");
            Set(7, craft.Navigation.GuidanceEnabled, "OFF");
            Set(8, craft.Comms.LinkEstablished, craft.Comms.Enabled && craft.Comms.AntennaEnabled ? "ACQUIRING" : "NO LINK");
            Set(9, craft.LifeSupport.Nominal, craft.LifeSupport.Enabled ? "PRESSURISING" : "OFF");

            // The control hint for a two-switch item should point at whichever switch is still off.
            checklist[0].Control = !power.BatteryAEnabled ? ControlId.BatteryA : ControlId.BatteryB;
            checklist[4].Control = !craft.Fuel.PumpsEnabled ? ControlId.FuelPump : ControlId.TankPress;
            checklist[8].Control = !craft.Comms.AntennaEnabled ? ControlId.CommAntenna : ControlId.Comms;
        }

        private void Set(int index, bool go, string noGoStatus)
        {
            checklist[index].IsGo = go;
            checklist[index].Status = go ? "GO" : noGoStatus;
        }

        private ChecklistItem FirstNoGo()
        {
            for (int i = 0; i < checklist.Count; i++) if (!checklist[i].IsGo) return checklist[i];
            return null;
        }

        private void UpdateObjective()
        {
            string text;
            ControlId control = ControlId.None;
            SystemsSpec limits = craft.SystemsSpec;

            switch (Phase)
            {
                case MissionPhase.PreLaunch:
                    switch (LaunchState)
                    {
                        case LaunchSequenceState.Idle:
                            ChecklistItem item = FirstNoGo();
                            if (item != null) { text = item.Hint; control = item.Control; }
                            else { text = "All systems GO. Press LAUNCH SEQ to start the countdown."; control = ControlId.LaunchSequence; }
                            break;
                        case LaunchSequenceState.Hold:
                            ChecklistItem held = FirstNoGo() ?? holdItem;
                            text = "HOLD. " + (held != null ? held.Hint : "Restore all systems to GO.");
                            control = held != null ? held.Control : ControlId.None;
                            break;
                        case LaunchSequenceState.Counting:
                            if (craft.Engine.IsBurning)
                            {
                                text = "Engine running. Hold-down clamps release at T-0.";
                            }
                            else if (craft.Engine.IgnitionInterlockOpen)
                            {
                                text = "Lift the guard and press ENGINE IGNITION.";
                                control = ControlId.EngineIgnition;
                            }
                            else
                            {
                                text = "Countdown running. Set the throttle to full and stand by for ignition.";
                                control = craft.Engine.ThrottleCommand < 0.9 ? ControlId.Throttle : ControlId.None;
                            }
                            break;
                        case LaunchSequenceState.AwaitingIgnition:
                            text = "T-0 hold. Lift the guard and press ENGINE IGNITION.";
                            control = ControlId.EngineIgnition;
                            break;
                        default:
                            text = "Engine running. Advance the throttle to lift off.";
                            control = ControlId.Throttle;
                            break;
                    }
                    break;

                case MissionPhase.Ascent:
                    if (craft.Flight.DynamicPressurePa > limits.maxDynamicPressurePa * 0.85)
                    {
                        text = "Max-Q. Ease the throttle back to keep dynamic pressure under the limit.";
                        control = ControlId.Throttle;
                    }
                    else if (craft.Flight.GLoad > limits.maxGLoad * 0.92)
                    {
                        text = "G load is building. Reduce throttle to stay under " + limits.maxGLoad.ToString("0.0") + " g.";
                        control = ControlId.Throttle;
                    }
                    else if (!craft.Engine.IsBurning)
                    {
                        text = "Engine is out. Press ENGINE IGNITION to relight.";
                        control = ControlId.EngineIgnition;
                    }
                    else
                    {
                        text = "Ascent nominal. Monitor speed, altitude and fuel until booster burnout.";
                    }
                    break;

                case MissionPhase.StageSeparation:
                    if (craft.Engine.ActiveStage == 0)
                    {
                        text = "Booster is spent. Lift the guard and press STAGE SEP.";
                        control = ControlId.StageSeparation;
                    }
                    else
                    {
                        text = "Stage is clear. Press ENGINE IGNITION to start the upper stage.";
                        control = ControlId.EngineIgnition;
                    }
                    break;

                case MissionPhase.OrbitalInsertion:
                    if (OrbitGo)
                    {
                        text = "ORBIT GO. Press ENGINE CUTOFF.";
                        control = ControlId.EngineCutoff;
                    }
                    else if (!craft.Engine.IsBurning)
                    {
                        text = "Engine is out short of orbit. Press ENGINE IGNITION to relight.";
                        control = ControlId.EngineIgnition;
                    }
                    else if (craft.Flight.GLoad > limits.maxGLoad * 0.92)
                    {
                        text = "G load is building as the stage lightens. Reduce throttle.";
                        control = ControlId.Throttle;
                    }
                    else
                    {
                        text = "Hold the burn to orbital velocity. Cut the engine when ORBIT GO illuminates.";
                    }
                    break;

                case MissionPhase.OuterSpace:
                    text = "Orbit achieved. Set GUIDANCE OFF and RCS ON to fly attitude by hand.";
                    break;

                default:
                    text = FailureReason ?? "Mission failed.";
                    break;
            }

            CurrentObjective = text;
            SuggestedControl = control;
        }

        // ---------------------------------------------------------------- events and helpers

        private void BuildEvents()
        {
            AddEvent(MissionEventId.Liftoff, "LIFTOFF");
            AddEvent(MissionEventId.MaxQ, "MAX-Q");
            AddEvent(MissionEventId.BoosterCutoff, "BOOSTER CUTOFF");
            AddEvent(MissionEventId.StageSeparation, "STAGE SEPARATION");
            AddEvent(MissionEventId.UpperStageIgnition, "UPPER STAGE IGNITION");
            AddEvent(MissionEventId.OrbitGo, "ORBIT GO");
            AddEvent(MissionEventId.OrbitAchieved, "ORBIT ACHIEVED");
            events[0].Status = MissionEventStatus.Active;
        }

        private void AddEvent(MissionEventId id, string label)
        {
            events.Add(new MissionEvent { Id = id, Label = label, Status = MissionEventStatus.Pending });
        }

        public MissionEvent GetEvent(MissionEventId id)
        {
            for (int i = 0; i < events.Count; i++) if (events[i].Id == id) return events[i];
            return null;
        }

        private void SetActive(MissionEventId id)
        {
            MissionEvent e = GetEvent(id);
            if (e != null && e.Status == MissionEventStatus.Pending) e.Status = MissionEventStatus.Active;
        }

        private void MarkEvent(MissionEventId id)
        {
            MissionEvent e = GetEvent(id);
            if (e == null || e.Status == MissionEventStatus.Done) return;
            e.Status = MissionEventStatus.Done;
            e.MetSeconds = MissionElapsedSeconds;
            if (EventReached != null) EventReached(e);
        }

        /// <summary>Overall mission progress, 0..1, for the mission display.</summary>
        public double Progress
        {
            get
            {
                FlightModel flight = craft.Flight;
                switch (Phase)
                {
                    case MissionPhase.PreLaunch:
                        double board = checklist.Count > 0 ? (double)GoCount / checklist.Count : 0.0;
                        double count = 1.0 - SimMath.Clamp01(CountdownRemaining / Math.Max(1.0, spec.countdownSeconds));
                        return 0.10 * board + (LaunchState == LaunchSequenceState.Idle ? 0.0 : 0.05 * count);
                    case MissionPhase.Ascent:
                        return 0.15 + 0.30 * (1.0 - craft.Fuel.GetFraction(0));
                    case MissionPhase.StageSeparation:
                        return craft.Engine.ActiveStage == 0 ? 0.46 : 0.50;
                    case MissionPhase.OrbitalInsertion:
                        return 0.52 + 0.45 * SimMath.Clamp01(flight.Speed / flight.CircularOrbitSpeed);
                    case MissionPhase.OuterSpace:
                        return 1.0;
                    default:
                        return 0.0;
                }
            }
        }

        private void SetPhase(MissionPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            if (PhaseChanged != null) PhaseChanged(phase);
        }

        private void Fail(string reason)
        {
            if (Phase == MissionPhase.Failed) return;
            FailureReason = reason;
            SetPhase(MissionPhase.Failed);
            Notify("MISSION FAILED");
            if (Failed != null) Failed(reason);
        }

        private void Notify(string message)
        {
            if (Notification != null) Notification(message);
        }

        /// <summary>Formats the mission clock as T-00:15 before liftoff and T+00:02:31 after.</summary>
        public string FormatClock()
        {
            if (!HasLiftedOff)
            {
                int remaining = (int)Math.Ceiling(CountdownRemaining);
                return "T-00:" + (remaining / 60).ToString("00") + ":" + (remaining % 60).ToString("00");
            }
            return "T+" + FormatMet(MissionElapsedSeconds);
        }

        public static string FormatMet(double seconds)
        {
            int total = (int)Math.Max(0.0, seconds);
            return (total / 3600).ToString("00") + ":" + (total / 60 % 60).ToString("00") + ":" + (total % 60).ToString("00");
        }
    }
}
