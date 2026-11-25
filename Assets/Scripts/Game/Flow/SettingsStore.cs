using System;
using System.IO;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>Player settings (TDD 9.2, US-7.3, US-7.5). Saved to settings.json.</summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const int MaxVolume = 10;

        public int SchemaVersion = SettingsStore.CurrentSchema;
        /// <summary>0..10.</summary>
        public int MasterVolume = 8;
        public int EffectsVolume = 10;
        public int MusicVolume = 7;
        public bool Haptics = true;
        /// <summary>Index into Quality Settings (0 Low, 1 Medium, 2 High). -1 = the platform default.</summary>
        public int Quality = -1;
        public int FrameRate = 60;
        /// <summary>Puts the blast button on the left and your panel on the right (US-7.5).</summary>
        public bool LeftHanded;

        public void Sanitise()
        {
            MasterVolume = Mathf.Clamp(MasterVolume, 0, MaxVolume);
            EffectsVolume = Mathf.Clamp(EffectsVolume, 0, MaxVolume);
            MusicVolume = Mathf.Clamp(MusicVolume, 0, MaxVolume);
            if (Quality >= QualitySettings.names.Length) Quality = -1;
            if (FrameRate != 30 && FrameRate != 60) FrameRate = 60;
        }
    }

    /// <summary>
    /// Loads, applies and saves <see cref="GameSettings"/> (TDD 9.2, 9.3). Every change applies at once and raises
    /// <see cref="Changed"/>; saving is debounced by the caller (menus save when they close).
    /// </summary>
    public static class SettingsStore
    {
        public const int CurrentSchema = 1;
        private static GameSettings _current;
        private static int _defaultQuality = -1;

        public static event Action Changed;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static GameSettings Current
        {
            get
            {
                if (_current == null) Load();
                return _current;
            }
        }

        public static void Load()
        {
            if (_defaultQuality < 0) _defaultQuality = QualitySettings.GetQualityLevel();
            try
            {
                if (File.Exists(FilePath)) _current = JsonUtility.FromJson<GameSettings>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not read settings, using defaults: {e.Message}");
            }
            if (_current == null) _current = new GameSettings();
            _current.Sanitise();
            Apply();
        }

        /// <summary>Applies the current settings to the engine: volume, quality and frame rate.</summary>
        public static void Apply()
        {
            var s = Current;
            AudioListener.volume = s.MasterVolume / (float)GameSettings.MaxVolume;
            int quality = s.Quality >= 0 ? s.Quality : _defaultQuality;
            if (quality >= 0 && quality != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(quality, true);
            Application.targetFrameRate = s.FrameRate;
            Changed?.Invoke();
        }

        public static void Save()
        {
            if (_current == null) return;
            try
            {
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(_current, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not save settings: {e.Message}");
            }
        }

        public static string QualityName(int index)
        {
            if (index < 0) return "Auto";
            var names = QualitySettings.names;
            return index < names.Length ? names[index] : "Auto";
        }
    }
}
