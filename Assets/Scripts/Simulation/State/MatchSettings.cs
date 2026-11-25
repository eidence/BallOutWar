using System;

namespace BOW.Simulation
{
    public enum GameMode : byte
    {
        Timed,
        Elimination,
        Practice,
    }

    public enum TieRule : byte
    {
        SuddenDeath,
        AllowDraw,
    }

    /// <summary>
    /// The rules of one match (TDD 5.2). Whether a seat is human, AI or remote is a game-layer concern:
    /// the simulation only needs to know which seats take part.
    /// </summary>
    [Serializable]
    public sealed class MatchSettings
    {
        public const int MinBalls = 1;
        public const int MaxBalls = 4;
        public const int MinGoalsToEliminate = 3;
        public const int MaxGoalsToEliminate = 20;

        public GameMode Mode = GameMode.Timed;
        public float DurationSeconds = 180f;
        public int GoalsToEliminate = 10;
        public int BallCount = 2;
        public TieRule TieRule = TieRule.SuddenDeath;
        public uint Seed = 1;
        public bool[] SeatActive = { true, true, true, true };

        public int ActiveSeatCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SeatActive.Length; i++) if (SeatActive[i]) n++;
                return n;
            }
        }

        /// <summary>Returns null when valid, otherwise a description of the first problem found.</summary>
        public string Validate()
        {
            if (SeatActive == null || SeatActive.Length != SeatFrame.SeatCount)
                return "SeatActive must have exactly 4 entries.";

            int active = ActiveSeatCount;
            if (Mode == GameMode.Practice)
            {
                if (active != 1) return "Practice needs exactly 1 active seat.";
            }
            else if (active < 2)
            {
                return "A match needs 2 to 4 active seats.";
            }

            if (BallCount < MinBalls || BallCount > MaxBalls)
                return $"Ball count must be {MinBalls}-{MaxBalls}.";
            if (Mode == GameMode.Timed && !(DurationSeconds > 0f))
                return "Timed matches need a positive duration.";
            if (Mode == GameMode.Elimination && (GoalsToEliminate < MinGoalsToEliminate || GoalsToEliminate > MaxGoalsToEliminate))
                return $"Goals to eliminate must be {MinGoalsToEliminate}-{MaxGoalsToEliminate}.";
            return null;
        }

        public MatchSettings Clone()
        {
            var copy = (MatchSettings)MemberwiseClone();
            copy.SeatActive = (bool[])SeatActive.Clone();
            return copy;
        }
    }
}
