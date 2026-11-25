using System;
using System.IO;
using BOW.Net;
using BOW.Simulation;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Offline resume (TDD 9.4, US-6.6). An offline match writes resume.json every 10 s and whenever the app
    /// loses focus, so a match survives Android killing the app. On the next launch the menu offers to resume;
    /// the file is deleted at match end, on quit, or when the player declines.
    /// The state is stored with the snapshot codec, which covers the whole <see cref="MatchState"/>.
    /// </summary>
    public static class ResumeStore
    {
        public const int CurrentSchema = 1;
        /// <summary>Older saves aren't offered: nobody wants to resume yesterday's match.</summary>
        private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

        [Serializable]
        public sealed class SavedMatch
        {
            public int SchemaVersion = CurrentSchema;
            public ushort Protocol = NetProtocol.Version;
            public long SavedAtUtcTicks;
            public uint Seed;
            public MatchLaunchConfig Config;
            public string State;
        }

        /// <summary>Set by the menu when the player chooses Resume; the match scene picks it up.</summary>
        public static SavedMatch Pending;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "resume.json");

        public static void Save(MatchLaunchConfig config, uint seed, MatchState state)
        {
            try
            {
                var writer = new PacketWriter(512);
                Codecs.WriteSnapshot(writer, state, null, 0);
                var data = new SavedMatch
                {
                    SavedAtUtcTicks = DateTime.UtcNow.Ticks,
                    Seed = seed,
                    Config = config,
                    State = Convert.ToBase64String(writer.Buffer, 0, writer.Length),
                };
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not save the match for resuming: {e.Message}");
            }
        }

        /// <summary>A saved match worth offering, or null (missing, too old, or from another version).</summary>
        public static SavedMatch Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var data = JsonUtility.FromJson<SavedMatch>(File.ReadAllText(FilePath));
                if (data == null || data.SchemaVersion != CurrentSchema || data.Protocol != NetProtocol.Version || data.Config == null || string.IsNullOrEmpty(data.State)) { Delete(); return null; }
                if (DateTime.UtcNow - new DateTime(data.SavedAtUtcTicks, DateTimeKind.Utc) > MaxAge) { Delete(); return null; }
                data.Config.Sanitise();
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Ignoring an unreadable resume file: {e.Message}");
                Delete();
                return null;
            }
        }

        /// <summary>Decodes the saved state into <paramref name="into"/>. Returns false if it is damaged.</summary>
        public static bool TryRestore(SavedMatch saved, MatchState into)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(saved.State);
                var reader = new PacketReader(bytes, bytes.Length);
                if (!reader.Header(out _, out var type) || type != MessageType.Snapshot) return false;
                Codecs.ReadSnapshot(reader, into, null);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception) { }
        }
    }
}
