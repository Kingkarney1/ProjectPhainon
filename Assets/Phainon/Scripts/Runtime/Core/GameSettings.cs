using UnityEngine;

namespace Phainon
{
    /// <summary>Player settings, kept in PlayerPrefs so they outlast the session.</summary>
    public static class GameSettings
    {
        public static float MasterVolume
        {
            get { return PlayerPrefs.GetFloat("Phainon.MasterVolume", 1f); }
            set { PlayerPrefs.SetFloat("Phainon.MasterVolume", Mathf.Clamp01(value)); }
        }

        /// <summary>True removes flicker, haze and camera shake, for players those effects trouble.</summary>
        public static bool ReducedEffects
        {
            get { return PlayerPrefs.GetInt("Phainon.ReducedEffects", 0) != 0; }
            set { PlayerPrefs.SetInt("Phainon.ReducedEffects", value ? 1 : 0); }
        }
    }
}
