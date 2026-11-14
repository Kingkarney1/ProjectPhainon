using System;
using Phainon.Simulation;

namespace Phainon.Tests
{
    /// <summary>
    /// Scripted crew member that flies the mission through the same control interface the cockpit uses.
    /// Used by the automated tests; it never touches subsystem state directly.
    /// </summary>
    public class MissionPilot
    {
        public const double Step = 0.02;

        public readonly Spacecraft Craft;
        /// <summary>Highest throttle the pilot will command.</summary>
        public double MaxThrottle = 1.0;
        /// <summary>When true the pilot eases the throttle to respect the max-Q and g limits.</summary>
        public bool RespectLimits = true;
        /// <summary>Seconds the pilot waits before acting on staging and upper stage ignition.</summary>
        public double ReactionDelay = 2.0;

        private double throttle;

        public MissionPilot(Spacecraft craft = null)
        {
            Craft = craft ?? new Spacecraft(new VehicleSpec(), new SystemsSpec(), new MissionSpec());
        }

        public CommandResult Press(ControlId id) { return Craft.HandleControl(id, 1.0); }
        public CommandResult Set(ControlId id, double value) { return Craft.HandleControl(id, value); }

        public void Advance(double seconds)
        {
            int steps = (int)Math.Ceiling(seconds / Step);
            for (int i = 0; i < steps; i++) Craft.Tick(Step);
        }

        /// <summary>Ticks until the condition holds. Returns false if the time limit is hit first.</summary>
        public bool RunUntil(Func<bool> condition, double maxSeconds, Action perTick = null)
        {
            int steps = (int)Math.Ceiling(maxSeconds / Step);
            for (int i = 0; i < steps; i++)
            {
                if (condition()) return true;
                if (perTick != null) perTick();
                Craft.Tick(Step);
            }
            return condition();
        }

        /// <summary>Runs the full pre-launch procedure and waits for every checklist item to report GO.</summary>
        public bool PowerUp()
        {
            Set(ControlId.BatteryA, 1);
            Set(ControlId.BatteryB, 1);
            Press(ControlId.MainPower);
            Set(ControlId.Apu, 1);
            Set(ControlId.FuelPump, 1);
            Set(ControlId.TankPress, 1);
            Press(ControlId.NavSystem);
            Set(ControlId.NavMode, (double)NavMode.Ascent);
            Set(ControlId.Guidance, 1);
            Set(ControlId.CommAntenna, 1);
            Press(ControlId.Comms);
            Press(ControlId.CabinSystems);
            return RunUntil(() => Craft.Mission.AllGo, 60.0);
        }

        /// <summary>Starts the count, ignites in the window and waits for liftoff.</summary>
        public bool Launch()
        {
            if (!Press(ControlId.LaunchSequence).Accepted) return false;
            SetThrottle(MaxThrottle);
            if (!RunUntil(() => Craft.Engine.IgnitionInterlockOpen, 60.0)) return false;
            if (!Press(ControlId.EngineIgnition).Accepted) return false;
            return RunUntil(() => Craft.Mission.HasLiftedOff, 60.0);
        }

        /// <summary>Flies from liftoff until the mission completes or fails.</summary>
        public bool FlyToOrbit(double maxSeconds = 900.0)
        {
            double actAt = -1.0;
            RunUntil(() => Craft.Mission.IsComplete || Craft.Mission.IsFailed, maxSeconds, () =>
            {
                MissionController mission = Craft.Mission;
                ManageThrottle();

                bool needsAction = mission.StageSeparationReady
                                   || (mission.Phase == MissionPhase.StageSeparation && Craft.Engine.ActiveStage > 0 && !Craft.Engine.IsBurning);
                if (needsAction)
                {
                    if (actAt < 0) actAt = mission.MissionElapsedSeconds + ReactionDelay;
                    if (mission.MissionElapsedSeconds >= actAt)
                    {
                        if (Craft.Engine.ActiveStage == 0) Press(ControlId.StageSeparation);
                        else Press(ControlId.EngineIgnition);
                        actAt = -1.0;
                    }
                }

                if (mission.OrbitGo && Craft.Engine.IsBurning) Press(ControlId.EngineCutoff);
            });
            return Craft.Mission.IsComplete;
        }

        public bool FlyFullMission()
        {
            return PowerUp() && Launch() && FlyToOrbit();
        }

        private void ManageThrottle()
        {
            double target = MaxThrottle;
            if (RespectLimits)
            {
                SystemsSpec limits = Craft.SystemsSpec;
                if (Craft.Flight.DynamicPressurePa > limits.maxDynamicPressurePa * 0.9) target = Math.Min(target, 0.62);
                if (Craft.Flight.GLoad > limits.maxGLoad * 0.94) target = Math.Min(target, throttle - 0.004);
            }
            // The lever moves at a human rate rather than snapping.
            SetThrottle(SimMath.MoveTowards(throttle, Math.Max(0.3, target), 0.6 * Step));
        }

        private void SetThrottle(double value)
        {
            throttle = SimMath.Clamp01(value);
            Set(ControlId.Throttle, throttle);
        }
    }
}
