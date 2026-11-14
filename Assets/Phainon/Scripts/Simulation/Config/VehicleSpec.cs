using System;

namespace Phainon.Simulation
{
    /// <summary>One propulsive stage of the launch vehicle.</summary>
    [Serializable]
    public class StageSpec
    {
        public string name = "Stage";
        public double dryMassKg = 10000;
        public double propellantMassKg = 100000;
        public double thrustNewtons = 2000000;
        public double specificImpulseSeconds = 450;
        /// <summary>Seconds for the engine to reach full output after ignition.</summary>
        public double spoolUpSeconds = 3.0;
        /// <summary>Seconds for thrust to decay after cutoff.</summary>
        public double spoolDownSeconds = 0.8;
    }

    /// <summary>A point of the open-loop ascent pitch program (pitch as a function of altitude).</summary>
    [Serializable]
    public class PitchPoint
    {
        public double altitudeMeters;
        public double pitchDegrees;

        public PitchPoint() { }
        public PitchPoint(double altitudeMeters, double pitchDegrees)
        {
            this.altitudeMeters = altitudeMeters;
            this.pitchDegrees = pitchDegrees;
        }
    }

    /// <summary>
    /// All tunable numbers describing the vehicle and its guidance. The values describe a
    /// fictional crew launcher (two stages to orbit, plus a service module for leaving it) and are
    /// tuned for a six to eight minute ascent.
    /// </summary>
    [Serializable]
    public class VehicleSpec
    {
        public string vehicleName = "PHAINON-1";
        public StageSpec[] stages = DefaultStages();
        /// <summary>Mass of the crew vehicle that remains after all stages are spent.</summary>
        public double payloadMassKg = 8000;
        /// <summary>Drag coefficient multiplied by reference area, in square metres.</summary>
        public double dragAreaCd = 38.0;

        // Guidance: first stage flies an open-loop pitch program keyed on altitude.
        public PitchPoint[] ascentPitchProgram = DefaultPitchProgram();

        // Guidance: upper stage steers closed-loop towards the target altitude.
        public double targetOrbitAltitudeMeters = 185000;
        /// <summary>Desired climb rate per metre of altitude error (1/s).</summary>
        public double verticalSpeedGain = 0.010;
        public double maxTargetClimbRate = 1200;
        public double maxTargetSinkRate = 150;
        /// <summary>Time constant used to close the vertical-speed error (s).</summary>
        public double verticalSpeedTimeConstant = 12;
        public double closedLoopMinPitchDegrees = -2;
        public double closedLoopMaxPitchDegrees = 53;

        // Attitude dynamics.
        public double guidancePitchRateDegPerSec = 3.0;
        public double manualPitchRateDegPerSec = 6.0;
        public double manualYawRateDegPerSec = 5.0;
        public double rollRateDegPerSec = 14.0;
        public double maxYawDegrees = 25.0;
        public double pitchTrimRangeDegrees = 8.0;

        /// <summary>Lowest setting of the thrust-limit dial, as a fraction of full thrust.</summary>
        public double minThrustLimit = 0.4;

        public static StageSpec[] DefaultStages()
        {
            return new[]
            {
                new StageSpec
                {
                    name = "CORE BOOSTER", dryMassKg = 70000, propellantMassKg = 560000,
                    thrustNewtons = 11400000, specificImpulseSeconds = 305,
                    spoolUpSeconds = 3.5, spoolDownSeconds = 1.0
                },
                new StageSpec
                {
                    name = "UPPER STAGE", dryMassKg = 11000, propellantMassKg = 130000,
                    thrustNewtons = 2150000, specificImpulseSeconds = 452,
                    spoolUpSeconds = 2.0, spoolDownSeconds = 0.6
                },
                // Crew vehicle plus this stage weigh what the payload alone used to, so the ascent is unchanged.
                new StageSpec
                {
                    name = "SERVICE MODULE", dryMassKg = 2000, propellantMassKg = 16000,
                    thrustNewtons = 250000, specificImpulseSeconds = 450,
                    spoolUpSeconds = 1.0, spoolDownSeconds = 0.4
                }
            };
        }

        public static PitchPoint[] DefaultPitchProgram()
        {
            return new[]
            {
                new PitchPoint(0, 90), new PitchPoint(300, 90), new PitchPoint(2000, 84),
                new PitchPoint(8000, 70), new PitchPoint(20000, 52), new PitchPoint(40000, 36),
                new PitchPoint(60000, 27), new PitchPoint(90000, 20)
            };
        }

        /// <summary>Evaluates the piecewise-linear pitch program at an altitude.</summary>
        public double EvaluatePitchProgram(double altitudeMeters)
        {
            PitchPoint[] p = ascentPitchProgram;
            if (p == null || p.Length == 0) return 90.0;
            if (altitudeMeters <= p[0].altitudeMeters) return p[0].pitchDegrees;
            for (int i = 0; i < p.Length - 1; i++)
            {
                if (altitudeMeters < p[i + 1].altitudeMeters)
                {
                    double t = SimMath.InverseLerp(p[i].altitudeMeters, p[i + 1].altitudeMeters, altitudeMeters);
                    return SimMath.Lerp(p[i].pitchDegrees, p[i + 1].pitchDegrees, t);
                }
            }
            return p[p.Length - 1].pitchDegrees;
        }
    }
}
