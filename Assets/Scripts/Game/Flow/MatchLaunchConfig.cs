using System;
using BOW.AI;
using BOW.Simulation;

namespace BOW.Game
{
    /// <summary>
    /// Everything the solo setup screen chooses (US-1.1, US-1.4, US-1.5, US-4.1). The local player is always
    /// seat 0 (South); opponents fill the other seats depending on how many there are.
    /// Serialisable with JsonUtility so the last choices are remembered (TDD 9.3).
    /// </summary>
    [Serializable]
    public sealed class MatchLaunchConfig
    {
        public const int LocalSeat = 0;
        public static readonly float[] DurationOptions = { 60f, 120f, 180f, 300f };

        public GameMode Mode = GameMode.Timed;
        public int BallCount = 2;
        public float DurationSeconds = 180f;
        public int GoalsToEliminate = 10;
        public TieRule TieRule = TieRule.SuddenDeath;
        public int Opponents = 3;
        public int PlayerCharacter = 0;
        /// <summary>AI level per seat; the local seat's entry is unused.</summary>
        public AIDifficulty[] Difficulty = { AIDifficulty.Medium, AIDifficulty.Medium, AIDifficulty.Medium, AIDifficulty.Medium };

        /// <summary>Which seats take part. 1 opponent sits opposite you; 2 sit either side; 3 fill the arena.</summary>
        public bool[] ActiveSeats()
        {
            if (Mode == GameMode.Practice) return new[] { true, false, false, false };
            switch (Math.Max(1, Math.Min(3, Opponents)))
            {
                case 1: return new[] { true, false, true, false };
                case 2: return new[] { true, true, false, true };
                default: return new[] { true, true, true, true };
            }
        }

        /// <summary>You get your chosen character; opponents get the others in order.</summary>
        public int[] CharactersBySeat()
        {
            var result = new int[SeatFrame.SeatCount];
            int player = Math.Max(0, Math.Min(Characters.Count - 1, PlayerCharacter));
            result[LocalSeat] = player;
            int next = 0;
            for (int seat = 0; seat < result.Length; seat++)
            {
                if (seat == LocalSeat) continue;
                if (next == player) next++;
                result[seat] = next % Characters.Count;
                next++;
            }
            return result;
        }

        public AIDifficulty DifficultyFor(int seat)
            => Difficulty != null && seat < Difficulty.Length ? Difficulty[seat] : AIDifficulty.Medium;

        /// <summary>Repairs values loaded from an old or edited save file.</summary>
        public void Sanitise()
        {
            BallCount = Math.Max(MatchSettings.MinBalls, Math.Min(MatchSettings.MaxBalls, BallCount));
            GoalsToEliminate = Math.Max(MatchSettings.MinGoalsToEliminate, Math.Min(MatchSettings.MaxGoalsToEliminate, GoalsToEliminate));
            Opponents = Math.Max(1, Math.Min(3, Opponents));
            PlayerCharacter = Math.Max(0, Math.Min(Characters.Count - 1, PlayerCharacter));
            if (Array.IndexOf(DurationOptions, DurationSeconds) < 0) DurationSeconds = 180f;
            if (Mode == GameMode.Practice) Mode = GameMode.Timed;
            if (Difficulty == null || Difficulty.Length != SeatFrame.SeatCount)
                Difficulty = new[] { AIDifficulty.Medium, AIDifficulty.Medium, AIDifficulty.Medium, AIDifficulty.Medium };
        }

        public MatchLaunchConfig Clone()
        {
            var copy = (MatchLaunchConfig)MemberwiseClone();
            copy.Difficulty = (AIDifficulty[])Difficulty.Clone();
            return copy;
        }
    }

    /// <summary>Hands the chosen setup from the menu to the Match scene.</summary>
    public static class MatchLaunch
    {
        public static MatchLaunchConfig Pending;
    }
}
