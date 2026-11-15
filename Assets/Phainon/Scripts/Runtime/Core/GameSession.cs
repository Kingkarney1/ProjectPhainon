namespace Phainon
{
    public enum GameMode { Standard, Story }

    /// <summary>What the next scene load should start: the launch mission or the story, new or continued.</summary>
    public static class GameSession
    {
        public static GameMode Mode;
        /// <summary>True to carry on from the story save instead of starting the mission afresh.</summary>
        public static bool LoadSave;
    }
}
