using System;
using System.IO;
using UnityEngine;

namespace BOW.Game
{
    [Serializable]
    public sealed class PlayerProfile
    {
        public int SchemaVersion = ProfileStore.CurrentSchema;
        public string PlayerName = "Player";
        public bool TutorialSeen;
        /// <summary>Last solo setup, so the menu opens with the same choices (US-1.4, US-4.1).</summary>
        public MatchLaunchConfig LastMatch = new MatchLaunchConfig();
    }

    /// <summary>
    /// Reads and writes profile.json in the app's private storage (TDD 9.3). Writes go to a temporary
    /// file first and then replace the original, so a crash never leaves a half-written profile.
    /// </summary>
    public static class ProfileStore
    {
        public const int CurrentSchema = 1;
        private static PlayerProfile _cached;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "profile.json");

        public static PlayerProfile Load()
        {
            if (_cached != null) return _cached;
            try
            {
                if (File.Exists(FilePath))
                    _cached = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not read profile, starting fresh: {e.Message}");
            }

            if (_cached == null) _cached = new PlayerProfile();
            if (_cached.LastMatch == null) _cached.LastMatch = new MatchLaunchConfig();
            _cached.LastMatch.Sanitise();
            _cached.SchemaVersion = CurrentSchema;
            return _cached;
        }

        public static void Save()
        {
            if (_cached == null) return;
            try
            {
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(_cached, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not save profile: {e.Message}");
            }
        }
    }
}
