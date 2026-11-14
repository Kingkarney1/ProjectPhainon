using System;

namespace Phainon.Simulation
{
    public enum EngineState { Off, Igniting, Running, Shutdown, Depleted }

    public enum EngineShutdownReason { Commanded, PropellantDepleted, FeedPressureLost, Abort }

    /// <summary>
    /// Main propulsion. Owns the active stage, ignition interlocks, spool-up and thrust output.
    /// Thrust follows the throttle lever, capped by the thrust-limit dial.
    /// </summary>
    public class EngineSystem
    {
        public const double StandardGravity = 9.80665;

        private readonly VehicleSpec vehicle;
        private readonly FuelSystem fuel;
        private double spool;
        private double tailOff;
        private double cutoffOutput;

        public EngineSystem(VehicleSpec vehicle, FuelSystem fuel)
        {
            this.vehicle = vehicle;
            this.fuel = fuel;
            ThrustLimit = 1.0;
        }

        public EngineState State { get; private set; }
        public int ActiveStage { get; private set; }
        public StageSpec ActiveStageSpec { get { return vehicle.stages[ActiveStage]; } }
        public bool IsFinalStage { get { return ActiveStage >= vehicle.stages.Length - 1; } }

        /// <summary>Throttle lever position, 0..1.</summary>
        public double ThrottleCommand { get; set; }
        /// <summary>Thrust-limit dial, 0..1. The engine never exceeds this output.</summary>
        public double ThrustLimit { get; set; }
        public double EffectiveThrottle { get { return Math.Min(SimMath.Clamp01(ThrottleCommand), SimMath.Clamp01(ThrustLimit)); } }

        /// <summary>Set by the mission: ignition of the first stage is locked out until the launch sequence allows it.</summary>
        public bool IgnitionInterlockOpen { get; set; }

        public double ThrustNewtons { get; private set; }
        /// <summary>Current output as a fraction of the active stage's maximum thrust.</summary>
        public double OutputFraction { get; private set; }
        public double ChamberTemperatureK { get; private set; }
        public bool IsBurning { get { return State == EngineState.Igniting || State == EngineState.Running; } }

        public event Action<int> Ignited;
        public event Action<int, EngineShutdownReason> ShutDown;
        public event Action<int> StageSeparated;

        /// <summary>Attempts ignition of the active stage's engine.</summary>
        public CommandResult TryIgnite(bool busPowered, bool apuOnline)
        {
            if (IsBurning) return CommandResult.Rejected("ENGINE ALREADY RUNNING");
            if (!busPowered) return CommandResult.Rejected("IGNITION INHIBITED - MAIN BUS UNPOWERED");
            if (!apuOnline) return CommandResult.Rejected("IGNITION INHIBITED - APU OFFLINE");
            if (!fuel.FeedReady) return CommandResult.Rejected("IGNITION INHIBITED - FUEL FEED NOT READY");
            if (!fuel.HasPropellant(ActiveStage)) return CommandResult.Rejected("IGNITION INHIBITED - STAGE PROPELLANT DEPLETED");
            if (ActiveStage == 0 && !IgnitionInterlockOpen)
                return CommandResult.Rejected("IGNITION INHIBITED - LAUNCH SEQUENCE NOT AT IGNITION");

            State = EngineState.Igniting;
            if (Ignited != null) Ignited(ActiveStage);
            return CommandResult.Ok("IGNITION");
        }

        public CommandResult Cutoff(EngineShutdownReason reason)
        {
            if (!IsBurning) return CommandResult.Rejected("ENGINE NOT RUNNING");
            EndBurn(reason);
            return CommandResult.Ok("ENGINE CUTOFF");
        }

        /// <summary>Drops the spent stage. Only valid with the engine shut down.</summary>
        public CommandResult SeparateStage()
        {
            if (IsFinalStage) return CommandResult.Rejected("NO STAGE TO SEPARATE");
            if (IsBurning || ThrustNewtons > ActiveStageSpec.thrustNewtons * 0.02)
                return CommandResult.Rejected("SEPARATION INHIBITED - ENGINE STILL THRUSTING");

            int dropped = ActiveStage;
            ActiveStage++;
            State = EngineState.Off;
            spool = 0.0;
            tailOff = 0.0;
            cutoffOutput = 0.0;
            ThrustNewtons = 0.0;
            OutputFraction = 0.0;
            if (StageSeparated != null) StageSeparated(dropped);
            return CommandResult.Ok("STAGE SEPARATION");
        }

        public void Tick(double dt)
        {
            StageSpec stage = ActiveStageSpec;

            if (IsBurning)
            {
                if (!fuel.FeedReady)
                {
                    EndBurn(EngineShutdownReason.FeedPressureLost);
                }
                else
                {
                    spool = SimMath.MoveTowards(spool, 1.0, dt / Math.Max(0.1, stage.spoolUpSeconds));
                    if (spool >= 1.0) State = EngineState.Running;
                }
            }

            double output;
            if (IsBurning)
            {
                output = spool * EffectiveThrottle;
            }
            else
            {
                // Thrust tails off over the spool-down time after any shutdown.
                tailOff = SimMath.MoveTowards(tailOff, 0.0, dt / Math.Max(0.1, stage.spoolDownSeconds));
                output = tailOff * cutoffOutput;
            }

            double thrust = stage.thrustNewtons * output;
            if (IsBurning && thrust > 0)
            {
                double wanted = thrust / (stage.specificImpulseSeconds * StandardGravity) * dt;
                double drawn = fuel.Consume(ActiveStage, wanted, dt);
                if (drawn < wanted * 0.999)
                {
                    thrust *= wanted > 0 ? drawn / wanted : 0.0;
                    OutputFraction = output;
                    EndBurn(EngineShutdownReason.PropellantDepleted);
                }
            }

            ThrustNewtons = thrust;
            OutputFraction = output;

            double targetTemp = 290.0 + 3300.0 * (stage.thrustNewtons > 0 ? ThrustNewtons / stage.thrustNewtons : 0.0);
            ChamberTemperatureK = SimMath.Approach(ChamberTemperatureK <= 0 ? 290.0 : ChamberTemperatureK, targetTemp, 1.5, dt);
        }

        private void EndBurn(EngineShutdownReason reason)
        {
            State = reason == EngineShutdownReason.PropellantDepleted ? EngineState.Depleted : EngineState.Shutdown;
            cutoffOutput = OutputFraction;
            tailOff = 1.0;
            spool = 0.0;
            if (ShutDown != null) ShutDown(ActiveStage, reason);
        }
    }
}
