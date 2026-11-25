using System;
using System.IO;
using System.Linq;
using BOW.Meta;
using BOW.Simulation;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Keeps <see cref="Entitlements"/> in entitlements.json (TDD 10.1) so unlocked modes work offline (US-8.1, US-8.5),
    /// and saves on every change.
    /// </summary>
    public static class EntitlementStore
    {
        [Serializable]
        private sealed class SaveData
        {
            public string[] Owned = Array.Empty<string>();
            public int[] TrialsUsed = Array.Empty<int>();
        }

        private static Entitlements _current;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "entitlements.json");

        public static Entitlements Current
        {
            get
            {
                if (_current != null) return _current;
                _current = new Entitlements();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
                        if (data != null)
                            _current.Load(data.Owned, (data.TrialsUsed ?? Array.Empty<int>()).Where(m => m >= 0 && m <= (int)GameMode.Practice).Select(m => (GameMode)m));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not read entitlements; purchases will be restored from the store: {e.Message}");
                }
                _current.Changed += Save;
                return _current;
            }
        }

        private static void Save()
        {
            try
            {
                var data = new SaveData
                {
                    Owned = _current.Owned.ToArray(),
                    TrialsUsed = _current.TrialsUsed.Select(m => (int)m).ToArray(),
                };
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not save entitlements: {e.Message}");
            }
        }

        /// <summary>A line explaining a locked mode for solo setup, or null when the mode is available.</summary>
        public static string SoloNote(GameMode mode)
        {
            switch (Current.StatusFor(mode))
            {
                case ModeStatus.Trial: return "Elimination is part of the Modes unlock. Your first match is a free try.";
                case ModeStatus.Locked: return "Elimination needs the Modes unlock. START opens the store.";
                default: return null;
            }
        }
    }
}
