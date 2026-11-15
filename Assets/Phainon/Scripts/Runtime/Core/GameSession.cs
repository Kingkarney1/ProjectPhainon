namespace Phainon
{
    public enum GameMode { Standard, Story }

    /// <summary>What the next scene load should start: the launch mission or the story, new or continued.</summary>
    public static class GameSession
    {
        public static GameMode Mode;
        /// <summary>True to carry on from the story save instead of starting the mission afresh.</summary>
        public static bool LoadSave;
        /// <summary>True to start a new story at the seat, without the introduction and wake-up. Used by the editor's smoke test.</summary>
        public static bool SkipSequences;
    }
}
