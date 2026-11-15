using System;
using System.IO;
using Phainon.Simulation;
using UnityEngine;

namespace Phainon
{
    [Serializable]
    public class StorySaveData
    {
        public int version = StorySave.Version;
        public string missionId;
        public string savedAt;
        public SpacecraftSnapshot craft;
    }

    /// <summary>
    /// The story's save file. It lives in its own folder, so nothing else the game writes can
    /// touch it. shortcut: one slot, add a slot index to the file name when more are wanted.
    /// </summary>
    public static class StorySave
    {
        /// <summary>Raise when the saved shape changes, and teach <see cref="Load"/> to read the older one.</summary>
        public const int Version = 1;

        private static string Folder { get { return Path.Combine(Application.persistentDataPath, "Story"); } }
        private static string FilePath { get { return Path.Combine(Folder, "save.json"); } }
        private static string BackupPath { get { return FilePath + ".bak"; } }

        public static bool Exists { get { return File.Exists(FilePath) || File.Exists(BackupPath); } }

        /// <summary>While true nothing is written, so an automated run cannot replace the player's save.</summary>
        internal static bool Suspended;

        /// <summary>Writes the save. Returns null when it worked, or why it did not.</summary>
        public static string Save(Spacecraft craft, string missionId)
        {
            if (Suspended) return "saving is suspended";
            try
            {
                var data = new StorySaveData { missionId = missionId, savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), craft = craft.Capture() };
                Directory.CreateDirectory(Folder);
                // Written beside the real file first, so a crash half-way through cannot leave a broken save.
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data));
                if (File.Exists(FilePath)) File.Copy(FilePath, BackupPath, true);
                File.Copy(temp, FilePath, true);
                File.Delete(temp);
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Phainon] Story save failed: " + e.Message);
                return e.Message;
            }
        }

        /// <summary>Reads the save, falling back to the previous one if the newest cannot be read. Null if there is nothing usable.</summary>
        public static StorySaveData Load()
        {
            return Read(FilePath) ?? Read(BackupPath);
        }

        private static StorySaveData Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                StorySaveData data = JsonUtility.FromJson<StorySaveData>(File.ReadAllText(path));
                if (data == null || data.craft == null || string.IsNullOrEmpty(data.missionId)) return null;
                if (data.version > Version)
                {
                    Debug.LogWarning("[Phainon] The story save is from a newer version of the game and was not loaded.");
                    return null;
                }
                // Older versions are upgraded here, one step at a time, when there are any.
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Phainon] Story save could not be read (" + path + "): " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Phainon] Story save could not be deleted: " + e.Message);
            }
        }
    }
}
