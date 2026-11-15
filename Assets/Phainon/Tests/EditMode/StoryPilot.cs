using System;
using Phainon.Simulation;

namespace Phainon.Tests
{
    /// <summary>
    /// A scripted astronaut for story missions. It does what the objective guide points at, after a
    /// human pause, speeds time up when there is nothing to do, and points the window by hand when
    /// an instrument needs it. It knows nothing about any particular mission.
    /// </summary>
    public class StoryPilot
    {
        public const double Step = 0.02;

        public readonly Spacecraft Craft;
        /// <summary>Which answer to give when Ground Control asks something.</summary>
        public int ReplyChoice;
        /// <summary>False makes the pilot sit on its hands: nothing is operated.</summary>
        public bool Active = true;
        /// <summary>False leaves the engine alone, to see what a missed burn does.</summary>
        public bool BurnsEngine = true;
        public Action OnTick;

        private double nextAction;
        private bool wantsFastTime;

        public StoryPilot(Spacecraft craft = null)
        {
            Craft = craft ?? new Spacecraft(new VehicleSpec(), new SystemsSpec(), new MissionSpec(), null, 0, Mission01Jupiter.Create());
        }

        public CommandResult Press(ControlId id) { return Craft.HandleControl(id, 1.0); }
        public CommandResult Set(ControlId id, double value) { return Craft.HandleControl(id, value); }

        /// <summary>Flies until the condition holds or the simulated time runs out. Returns whether it held.</summary>
        public bool RunUntil(Func<bool> condition, double maxSimSeconds)
        {
            double start = Craft.SimClockSeconds;
            while (Craft.SimClockSeconds - start < maxSimSeconds)
            {
                if (condition()) return true;
                if (Active) Act();
                if (OnTick != null) OnTick();
                // Time is only sped up when the spacecraft allows it, in the same one-second slices the game uses.
                bool fast = wantsFastTime && Craft.TimeWarpInhibit == null;
                Craft.Tick(fast ? 1.0 : Step);
            }
            return condition();
        }

        public bool FlyMission(double maxSimSeconds = 40.0 * 86400.0)
        {
            return RunUntil(() => Craft.Story.IsComplete || Craft.Mission.IsFailed, maxSimSeconds) && Craft.Story.IsComplete;
        }

        private void Act()
        {
            Spacecraft c = Craft;
            StoryController story = c.Story;
            wantsFastTime = false;
            c.SetAttitudeInput(0, 0, 0);

            if (c.GroundControl.AwaitingReply != null && c.Comms.LinkEstablished) c.GroundControl.Reply(ReplyChoice);

            ControlId control = c.Mission.SuggestedControl;
            if (control == ControlId.None)
            {
                Wait(story.Current);
                return;
            }
            // A careful crew settle the window on the planet before starting an instrument that has to be aimed.
            if (control == ControlId.Observe && c.Science.Selected.needsPointing && c.Attitude.WindowOffNadirDegrees > 2.0)
            {
                PointAtBody();
                return;
            }
            if (c.SimClockSeconds < nextAction) return;
            nextAction = c.SimClockSeconds + 1.5;

            switch (control)
            {
                case ControlId.MasterAlarm: Press(control); break;
                case ControlId.Apu: Set(control, c.Power.ApuEnabled ? 0 : 1); break;
                case ControlId.CabinSystems: Press(control); break;
                case ControlId.NavSystem: Press(control); break;
                case ControlId.Comms: Press(control); break;
                case ControlId.CommAntenna: Set(control, c.Comms.AntennaEnabled ? 0 : 1); break;
                case ControlId.FuelPump: Set(control, c.Fuel.PumpsEnabled ? 0 : 1); break;
                case ControlId.TankPress: Set(control, 1); break;
                case ControlId.NavMode: Set(control, (double)NavMode.Retro); break;
                case ControlId.Guidance: Set(control, 1); break;
                case ControlId.Rcs: Set(control, 1); break;
                case ControlId.Throttle: Set(control, 1.0); break;
                case ControlId.EngineIgnition: if (BurnsEngine) Press(control); else wantsFastTime = true; break;
                case ControlId.EngineCutoff: Press(control); break;
                case ControlId.SciencePower: Set(control, 1); break;
                case ControlId.InstrumentSelect: Set(control, (c.Science.SelectedIndex + 1) % c.Science.Instruments.Count); break;
                case ControlId.Observe: Press(control); break;
                case ControlId.TransmitData: Press(control); break;
                case ControlId.BatteryA: Set(control, 0); break;
            }
        }

        /// <summary>Nothing to operate: read, point, change instrument or let time pass, depending on what is being waited for.</summary>
        private void Wait(ObjectiveSpec objective)
        {
            Spacecraft c = Craft;
            if (objective == null) { wantsFastTime = true; return; }

            switch (objective.condition)
            {
                case ObjectiveCondition.RecordRead:
                    c.Story.MarkRead(objective.parameter);
                    break;

                case ObjectiveCondition.OrbitStable:
                    // Coasting in to periapsis. Come back to real time in good time for the burn.
                    double toPeriapsis = c.Flight.TimeToPeriapsisSeconds;
                    wantsFastTime = c.Engine.IsBurning ? false : (double.IsNaN(toPeriapsis) || toPeriapsis > 240.0);
                    break;

                case ObjectiveCondition.ObservationsCollected:
                    WaitForScience();
                    break;

                default:
                    // Something is spooling up, aligning or downlinking.
                    wantsFastTime = objective.condition == ObjectiveCondition.DataTransmitted;
                    break;
            }
        }

        private void WaitForScience()
        {
            Spacecraft c = Craft;
            ScienceSystem science = c.Science;
            if (science.Active != null)
            {
                // Recording. Keep the window on the planet as it shrinks behind.
                if (!science.Recording) PointAtBody();
                return;
            }

            // Is there an instrument that could work from here if only it were pointed?
            int pointable = -1;
            for (int i = 0; i < science.Instruments.Count && pointable < 0; i++)
            {
                ObservationSpec next = science.NextFor(science.Instruments[i]);
                if (next == null) continue;
                string blocker = science.Blocker(next);
                if (blocker == null || blocker.StartsWith("NOT POINTED", StringComparison.Ordinal)) pointable = i;
            }

            if (pointable < 0)
            {
                wantsFastTime = true;       // out of range, in shadow or the wrong face: wait for the orbit to bring it round
                return;
            }
            if (science.SelectedIndex != pointable)
            {
                if (c.SimClockSeconds >= nextAction) { Set(ControlId.InstrumentSelect, pointable); nextAction = c.SimClockSeconds + 1.0; }
                return;
            }

            PointAtBody();
        }

        /// <summary>Turns the window onto the planet by hand.</summary>
        private void PointAtBody()
        {
            Spacecraft c = Craft;
            if (c.Navigation.GuidanceEnabled) Set(ControlId.Guidance, 0);
            if (!c.Attitude.RcsEnabled) Set(ControlId.Rcs, 1);
            double wanted = -90.0 + c.VehicleSpec.windowTiltDegrees;
            double error = SimMath.WrapDegrees(wanted - c.Attitude.PitchDegrees);
            if (Math.Abs(error) > 1.0) c.SetAttitudeInput(Math.Sign(error), 0, 0);
        }
    }
}
