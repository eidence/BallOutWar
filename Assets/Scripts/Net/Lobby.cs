using System;
using BOW.AI;
using BOW.Simulation;

namespace BOW.Net
{
    public enum SeatType : byte
    {
        /// <summary>Waiting for a player to join; becomes Closed if the match starts with it still open.</summary>
        Open,
        Human,
        AI,
        Closed,
    }

    public struct LobbySeat
    {
        public SeatType Type;
        public string Name;
        public byte Character;
        public bool Ready;
        public bool IsHost;
        public AIDifficulty Difficulty;
    }

    /// <summary>The match options the host picks in the lobby (TDD 5.2, 8.5).</summary>
    public sealed class LobbySettings
    {
        public GameMode Mode = GameMode.Timed;
        public int BallCount = 2;
        public int DurationSeconds = 120;
        public int GoalsToEliminate = 5;
        public TieRule TieRule = TieRule.SuddenDeath;

        public LobbySettings Clone() => (LobbySettings)MemberwiseClone();

        public bool SameAs(LobbySettings o)
            => o != null && Mode == o.Mode && BallCount == o.BallCount && DurationSeconds == o.DurationSeconds
               && GoalsToEliminate == o.GoalsToEliminate && TieRule == o.TieRule;

        internal void Write(PacketWriter w)
        {
            w.U8((byte)Mode);
            w.U8((byte)BallCount);
            w.U16((ushort)DurationSeconds);
            w.U8((byte)GoalsToEliminate);
            w.U8((byte)TieRule);
        }

        internal static LobbySettings Read(PacketReader r)
        {
            var s = new LobbySettings
            {
                Mode = (GameMode)r.U8(),
                BallCount = r.U8(),
                DurationSeconds = r.U16(),
                GoalsToEliminate = r.U8(),
                TieRule = (TieRule)r.U8(),
            };
            if (s.Mode > GameMode.Elimination) s.Mode = GameMode.Timed; // Practice is offline only
            s.BallCount = Math.Max(MatchSettings.MinBalls, Math.Min(MatchSettings.MaxBalls, s.BallCount));
            s.DurationSeconds = Math.Max(30, Math.Min(600, s.DurationSeconds));
            s.GoalsToEliminate = Math.Max(MatchSettings.MinGoalsToEliminate, Math.Min(MatchSettings.MaxGoalsToEliminate, s.GoalsToEliminate));
            if (s.TieRule > TieRule.AllowDraw) s.TieRule = TieRule.SuddenDeath;
            return s;
        }
    }

    /// <summary>
    /// The host's authoritative lobby (TDD 8.5): settings and four seats. Broadcast in full on every change.
    /// Seat 0 (South) is always the host.
    /// </summary>
    public sealed class LobbyState
    {
        public const int CharacterCount = 4;

        public string MatchName = "";
        public bool InGame;
        public LobbySettings Settings = new LobbySettings();
        public readonly LobbySeat[] Seats = new LobbySeat[SeatFrame.SeatCount];

        public int HumanCount
        {
            get
            {
                int n = 0;
                foreach (var s in Seats) if (s.Type == SeatType.Human) n++;
                return n;
            }
        }

        /// <summary>Seats that will play: humans and AIs.</summary>
        public int PlayingCount
        {
            get
            {
                int n = 0;
                foreach (var s in Seats) if (s.Type == SeatType.Human || s.Type == SeatType.AI) n++;
                return n;
            }
        }

        public int OpenCount
        {
            get
            {
                int n = 0;
                foreach (var s in Seats) if (s.Type == SeatType.Open) n++;
                return n;
            }
        }

        /// <summary>Why the match can't start yet, or null when it can.</summary>
        public string StartBlocker()
        {
            if (PlayingCount < 2) return "Add an AI or wait for another player.";
            for (int i = 0; i < Seats.Length; i++)
                if (Seats[i].Type == SeatType.Human && !Seats[i].IsHost && !Seats[i].Ready)
                    return $"Waiting for {Seats[i].Name} to be ready.";
            return null;
        }

        public MatchSettings ToMatchSettings(uint seed)
        {
            var m = new MatchSettings
            {
                Mode = Settings.Mode,
                BallCount = Settings.BallCount,
                DurationSeconds = Settings.DurationSeconds,
                GoalsToEliminate = Settings.GoalsToEliminate,
                TieRule = Settings.TieRule,
                Seed = seed,
            };
            for (int i = 0; i < Seats.Length; i++) m.SeatActive[i] = Seats[i].Type == SeatType.Human || Seats[i].Type == SeatType.AI;
            return m;
        }

        public int[] Characters()
        {
            var c = new int[Seats.Length];
            for (int i = 0; i < c.Length; i++) c[i] = Seats[i].Character % CharacterCount;
            return c;
        }

        public LobbyState Clone()
        {
            var copy = new LobbyState { MatchName = MatchName, InGame = InGame, Settings = Settings.Clone() };
            Array.Copy(Seats, copy.Seats, Seats.Length);
            return copy;
        }

        internal void Write(PacketWriter w)
        {
            w.Str(MatchName, 32);
            w.Bool(InGame);
            Settings.Write(w);
            foreach (var s in Seats)
            {
                w.U8((byte)s.Type);
                w.Str(s.Name, 32);
                w.U8(s.Character);
                w.Bool(s.Ready);
                w.Bool(s.IsHost);
                w.U8((byte)s.Difficulty);
            }
        }

        internal static LobbyState Read(PacketReader r)
        {
            var l = new LobbyState { MatchName = r.Str(), InGame = r.Bool(), Settings = LobbySettings.Read(r) };
            for (int i = 0; i < l.Seats.Length; i++)
            {
                l.Seats[i] = new LobbySeat
                {
                    Type = (SeatType)Math.Min(r.U8(), (byte)SeatType.Closed),
                    Name = r.Str(),
                    Character = (byte)(r.U8() % CharacterCount),
                    Ready = r.Bool(),
                    IsHost = r.Bool(),
                    Difficulty = (AIDifficulty)Math.Min(r.U8(), (byte)AIDifficulty.Expert),
                };
            }
            return l;
        }
    }

    public static class PlayerNames
    {
        /// <summary>Trims, removes control characters and limits length (TDD 9.2). Falls back to "Player".</summary>
        public static string Normalise(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Player";
            var chars = new System.Text.StringBuilder(name.Length);
            foreach (char c in name.Trim()) if (!char.IsControl(c)) chars.Append(c);
            string s = chars.ToString().Trim();
            if (s.Length > NetProtocol.MaxNameLength) s = s.Substring(0, NetProtocol.MaxNameLength).Trim();
            return s.Length >= NetProtocol.MinNameLength ? s : "Player";
        }
    }
}
