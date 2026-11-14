using System;

namespace Phainon.Simulation
{
    /// <summary>Text shown to the player for one mission phase.</summary>
    [Serializable]
    public class PhaseInfo
    {
        public MissionPhase phase;
        public string title;
        public string objective;

        public PhaseInfo() { }
        public PhaseInfo(MissionPhase phase, string title, string objective)
        {
            this.phase = phase;
            this.title = title;
            this.objective = objective;
        }
    }

    /// <summary>Tunable numbers and text that define the Earth-to-orbit mission.</summary>
    [Serializable]
    public class MissionSpec
    {
        public string missionName = "OPERATION PHAINON";
        public string missionSubtitle = "EARTH TO DEEP SPACE";

        public double countdownSeconds = 15;
        /// <summary>Ignition is permitted once the countdown is inside this many seconds.</summary>
        public double ignitionWindowSeconds = 6;
        /// <summary>Thrust-to-weight ratio needed before the hold-down clamps release.</summary>
        public double liftoffThrustToWeight = 1.03;

        public double stageSeparationMinAltitudeMeters = 40000;
        public double orbitMinPeriapsisMeters = 150000;
        /// <summary>After staging, sinking below this altitude fails the mission.</summary>
        public double reentryFailAltitudeMeters = 60000;
        public double spaceBoundaryAltitudeMeters = 100000;
        public double maxQDetectMinPa = 8000;
        /// <summary>Distances from Earth that are called out on the way out, in kilometres.</summary>
        public double[] distanceMarksKm = { 10000, 100000, 384400, 1000000 };

        public PhaseInfo[] phases = DefaultPhases();

        public static PhaseInfo[] DefaultPhases()
        {
            return new[]
            {
                new PhaseInfo(MissionPhase.PreLaunch, "PRE-LAUNCH",
                    "Power up the spacecraft and bring every system to GO, then start the launch sequence."),
                new PhaseInfo(MissionPhase.Ascent, "ATMOSPHERIC ASCENT",
                    "Manage throttle through max-Q and hold the ascent until booster burnout."),
                new PhaseInfo(MissionPhase.StageSeparation, "STAGE SEPARATION",
                    "Booster is spent. Separate the stage, then ignite the upper stage engine."),
                new PhaseInfo(MissionPhase.OrbitalInsertion, "ORBITAL INSERTION",
                    "Burn to orbital velocity. Cut the engine when ORBIT GO illuminates."),
                new PhaseInfo(MissionPhase.OuterSpace, "OUTER SPACE",
                    "Stable orbit achieved. Stay, or drop the upper stage and burn the service module to leave Earth."),
                new PhaseInfo(MissionPhase.Failed, "MISSION FAILED",
                    "The mission cannot continue. Restart from the pause menu.")
            };
        }

        public PhaseInfo GetPhaseInfo(MissionPhase phase)
        {
            if (phases != null)
            {
                for (int i = 0; i < phases.Length; i++)
                    if (phases[i] != null && phases[i].phase == phase) return phases[i];
            }
            return new PhaseInfo(phase, phase.ToString().ToUpperInvariant(), string.Empty);
        }
    }
}
