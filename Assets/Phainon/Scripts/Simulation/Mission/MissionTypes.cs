namespace Phainon.Simulation
{
    public enum MissionPhase { PreLaunch = 0, Ascent = 1, StageSeparation = 2, OrbitalInsertion = 3, OuterSpace = 4, Failed = 5 }

    public enum LaunchSequenceState { Idle, Counting, Hold, AwaitingIgnition, AwaitingThrust, Complete }

    public enum MissionEventStatus { Pending, Active, Done }

    public enum MissionEventId { Liftoff, MaxQ, BoosterCutoff, StageSeparation, UpperStageIgnition, OrbitGo, OrbitAchieved }

    /// <summary>One row of the pre-launch GO / NO-GO board.</summary>
    public class ChecklistItem
    {
        public string Label;
        public string Hint;
        public ControlId Control;
        public bool IsGo;
        public string Status;
    }

    /// <summary>One entry on the flight-event timeline.</summary>
    public class MissionEvent
    {
        public MissionEventId Id;
        public string Label;
        public MissionEventStatus Status;
        /// <summary>Mission elapsed time at which the event happened, valid when Done.</summary>
        public double MetSeconds;
    }
}
