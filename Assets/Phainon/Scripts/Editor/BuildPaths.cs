namespace Phainon.EditorTools
{
    /// <summary>Where generated assets live. Everything the builder writes goes under these folders.</summary>
    public static class BuildPaths
    {
        public const string Root = "Assets/Phainon";
        public const string Data = Root + "/Data";
        public const string Materials = Root + "/Art/Materials";
        public const string Meshes = Root + "/Art/Meshes";
        public const string Textures = Root + "/Art/Textures";
        public const string Audio = Root + "/Audio";
        public const string Prefabs = Root + "/Prefabs/Controls";
        public const string Scenes = Root + "/Scenes";
        public const string MissionScene = Scenes + "/Mission.unity";
        public const string EarthDayTexture = Textures + "/EarthDay.jpg";
        public const string VolumeProfile = "Assets/Settings/CockpitPostProcess.asset";

        public const int LayerCockpit = 6;
        public const int LayerInteractable = 7;
        public const int LayerExterior = 8;
        public const int LayerEnvironment = 9;
    }
}
