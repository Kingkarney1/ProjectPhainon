using System;

namespace Phainon.Simulation
{
    /// <summary>What the flight computer steers towards when guidance is active.</summary>
    public enum AttitudeHold
    {
        /// <summary>The ascent profile: only meaningful under thrust.</summary>
        Profile,
        /// <summary>Nose along the flight path.</summary>
        Prograde,
        /// <summary>Nose against the flight path, for slowing down.</summary>
        Retrograde
    }

    [Serializable]
    public class AttitudeSnapshot
    {
        public double pitch = 90.0;
        public double yaw;
        public double roll;
        public double rollManualOffset;
        public bool rcsEnabled;
        public double pitchTrim;
        public double rollDial;
    }

    /// <summary>
    /// Vehicle attitude: pitch above the local horizon, yaw out of the flight plane and roll about the nose.
    /// Pitch is a full circle: 0 is level and facing downrange, 90 straight up, 180 level and facing back.
    /// With guidance active the flight computer steers pitch: along the ascent profile while the
    /// engine burns, or along or against the flight path in the two hold modes. Otherwise the crew fly it by hand.
    /// </summary>
    public class AttitudeController
    {
        private readonly VehicleSpec vehicle;
        private double rollManualOffset;

        public AttitudeController(VehicleSpec vehicle)
        {
            this.vehicle = vehicle;
            PitchDegrees = 90.0;
        }

        public double PitchDegrees { get; private set; }
        public double YawDegrees { get; private set; }
        public double RollDegrees { get; private set; }
        /// <summary>Pitch the flight computer is currently asking for.</summary>
        public double GuidancePitchDegrees { get; private set; }

        public bool RcsEnabled { get; set; }
        /// <summary>Pitch-trim dial, -1..1, added to the guidance command.</summary>
        public double PitchTrim { get; set; }
        /// <summary>Roll dial, -1..1, mapped to a commanded roll angle of +/-180 degrees.</summary>
        public double RollDial { get; set; }

        // Hand-controller input, each -1..1.
        public double PitchInput { get; set; }
        public double YawInput { get; set; }
        public double RollInput { get; set; }

        /// <summary>True when the crew have a way to rotate the vehicle (engine gimbal or RCS jets).</summary>
        public bool HasControlAuthority { get; private set; }
        public bool RcsFiring { get; private set; }

        /// <summary>
        /// Angle between the forward window's line of sight and the centre of the body below, in
        /// degrees. The window looks below the thrust axis by the vehicle's window tilt.
        /// </summary>
        public double WindowOffNadirDegrees
        {
            get
            {
                double pitch = PitchDegrees * SimMath.Deg2Rad;
                double roll = RollDegrees * SimMath.Deg2Rad;
                double tilt = vehicle.windowTiltDegrees * SimMath.Deg2Rad;
                // Upward part of the sight line. The tilt is fixed in the cabin, so rolling swings the
                // window off to one side; yaw turns the vehicle about the vertical and changes nothing here.
                double up = Math.Cos(tilt) * Math.Sin(pitch) - Math.Sin(tilt) * Math.Cos(roll) * Math.Cos(pitch);
                return Math.Acos(SimMath.Clamp(-up, -1.0, 1.0)) * SimMath.Rad2Deg;
            }
        }

        /// <summary>
        /// Points the vehicle without turning it there. Used when a flight starts away from the pad.
        /// The roll is where the vehicle has drifted to, not a command: once the crew have control
        /// again it comes back to what the roll dial asks for.
        /// </summary>
        public void SetAttitude(double pitchDegrees, double yawDegrees, double rollDegrees)
        {
            PitchDegrees = SimMath.WrapDegrees(pitchDegrees);
            YawDegrees = yawDegrees;
            RollDegrees = SimMath.WrapDegrees(rollDegrees);
            rollManualOffset = 0.0;
        }

        public AttitudeSnapshot Capture()
        {
            return new AttitudeSnapshot
            {
                pitch = PitchDegrees, yaw = YawDegrees, roll = RollDegrees, rollManualOffset = rollManualOffset,
                rcsEnabled = RcsEnabled, pitchTrim = PitchTrim, rollDial = RollDial
            };
        }

        public void Restore(AttitudeSnapshot s)
        {
            PitchDegrees = s.pitch;
            YawDegrees = s.yaw;
            RollDegrees = s.roll;
            rollManualOffset = s.rollManualOffset;
            RcsEnabled = s.rcsEnabled;
            PitchTrim = s.pitchTrim;
            RollDial = s.rollDial;
        }

        public void Tick(double dt, FlightModel flight, EngineSystem engine, bool guidanceActive, AttitudeHold hold, bool busPowered, bool apuOnline)
        {
            bool gimbal = engine.IsBurning && apuOnline;
            bool rcs = RcsEnabled && busPowered;
            HasControlAuthority = gimbal || rcs;
            RcsFiring = false;

            switch (hold)
            {
                case AttitudeHold.Prograde: GuidancePitchDegrees = flight.FlightPathAngleDegrees; break;
                case AttitudeHold.Retrograde: GuidancePitchDegrees = SimMath.WrapDegrees(flight.FlightPathAngleDegrees + 180.0); break;
                default: GuidancePitchDegrees = ComputeGuidancePitch(flight, engine); break;
            }

            if (!flight.Released)
            {
                PitchDegrees = 90.0;
                YawDegrees = 0.0;
                return;
            }
            if (!HasControlAuthority) return;

            // The ascent profile only means something under thrust. The holds also work while coasting.
            if (guidanceActive && (engine.IsBurning || hold != AttitudeHold.Profile))
            {
                double target = GuidancePitchDegrees + PitchTrim * vehicle.pitchTrimRangeDegrees;
                // The short way round: from 170 to -170 is twenty degrees, not three hundred and forty.
                double error = SimMath.WrapDegrees(target - PitchDegrees);
                if (!gimbal && (Math.Abs(error) > 0.05 || Math.Abs(YawDegrees) > 0.05)) RcsFiring = true;
                double step = vehicle.guidancePitchRateDegPerSec * dt;
                PitchDegrees = SimMath.WrapDegrees(PitchDegrees + SimMath.Clamp(error, -step, step));
                YawDegrees = SimMath.MoveTowards(YawDegrees, 0.0, vehicle.manualYawRateDegPerSec * dt);
            }
            else
            {
                PitchDegrees = SimMath.WrapDegrees(PitchDegrees + PitchInput * vehicle.manualPitchRateDegPerSec * dt);
                YawDegrees = SimMath.Clamp(YawDegrees + YawInput * vehicle.manualYawRateDegPerSec * dt,
                    -vehicle.maxYawDegrees, vehicle.maxYawDegrees);
                if (rcs && (Math.Abs(PitchInput) > 0.05 || Math.Abs(YawInput) > 0.05)) RcsFiring = true;
            }

            // Roll is always under crew command: the dial sets an angle and the hand controller nudges it.
            rollManualOffset = SimMath.WrapDegrees(rollManualOffset + RollInput * vehicle.rollRateDegPerSec * dt);
            double rollTarget = SimMath.WrapDegrees(RollDial * 180.0 + rollManualOffset);
            double rollError = SimMath.WrapDegrees(rollTarget - RollDegrees);
            double rollStep = SimMath.Clamp(rollError, -vehicle.rollRateDegPerSec * dt, vehicle.rollRateDegPerSec * dt);
            if (rcs && Math.Abs(rollError) > 0.5) RcsFiring = true;
            RollDegrees = SimMath.WrapDegrees(RollDegrees + rollStep);
        }

        private double ComputeGuidancePitch(FlightModel flight, EngineSystem engine)
        {
            if (engine.ActiveStage == 0)
                return vehicle.EvaluatePitchProgram(flight.AltitudeMeters);

            // Closed loop: pick the pitch whose vertical thrust component closes the climb-rate error
            // while the rest of the thrust builds horizontal (orbital) velocity.
            double climbTarget = SimMath.Clamp(
                (vehicle.targetOrbitAltitudeMeters - flight.AltitudeMeters) * vehicle.verticalSpeedGain,
                -vehicle.maxTargetSinkRate, vehicle.maxTargetClimbRate);

            double r = flight.Radius;
            double gravity = flight.Body.mu / (r * r);
            double centrifugal = flight.TangentialVelocity * flight.TangentialVelocity / r;
            double neededRadialAccel = (climbTarget - flight.VerticalSpeed) / vehicle.verticalSpeedTimeConstant
                                       + gravity - centrifugal;

            double thrustAccel = flight.MassKg > 0 ? engine.ActiveStageSpec.thrustNewtons * Math.Max(0.3, engine.EffectiveThrottle) / flight.MassKg : 1.0;
            double minSin = Math.Sin(vehicle.closedLoopMinPitchDegrees * SimMath.Deg2Rad);
            double maxSin = Math.Sin(vehicle.closedLoopMaxPitchDegrees * SimMath.Deg2Rad);
            double sin = SimMath.Clamp(neededRadialAccel / thrustAccel, minSin, maxSin);
            return Math.Asin(sin) * SimMath.Rad2Deg;
        }
    }
}
