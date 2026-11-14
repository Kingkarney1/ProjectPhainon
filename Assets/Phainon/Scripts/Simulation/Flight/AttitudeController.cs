using System;

namespace Phainon.Simulation
{
    /// <summary>
    /// Vehicle attitude: pitch above the local horizon, yaw out of the flight plane and roll about the nose.
    /// With guidance active the flight computer steers pitch; otherwise the crew fly it by hand.
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

        public void Tick(double dt, FlightModel flight, EngineSystem engine, bool guidanceActive, bool busPowered, bool apuOnline)
        {
            bool gimbal = engine.IsBurning && apuOnline;
            bool rcs = RcsEnabled && busPowered;
            HasControlAuthority = gimbal || rcs;
            RcsFiring = false;

            GuidancePitchDegrees = ComputeGuidancePitch(flight, engine);

            if (!flight.Released)
            {
                PitchDegrees = 90.0;
                YawDegrees = 0.0;
                return;
            }
            if (!HasControlAuthority) return;

            if (guidanceActive && engine.IsBurning)
            {
                double target = GuidancePitchDegrees + PitchTrim * vehicle.pitchTrimRangeDegrees;
                PitchDegrees = SimMath.MoveTowards(PitchDegrees, target, vehicle.guidancePitchRateDegPerSec * dt);
                YawDegrees = SimMath.MoveTowards(YawDegrees, 0.0, vehicle.manualYawRateDegPerSec * dt);
            }
            else
            {
                PitchDegrees = SimMath.Clamp(PitchDegrees + PitchInput * vehicle.manualPitchRateDegPerSec * dt, -90.0, 90.0);
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
            double gravity = FlightModel.EarthMu / (r * r);
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
