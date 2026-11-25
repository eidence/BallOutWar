using System;

namespace BOW.Simulation
{
    public enum MatchPhase : byte
    {
        /// <summary>3-2-1-Go. Paddles move, blasts are disabled, balls are frozen (US-2.6).</summary>
        Countdown,
        Playing,
        /// <summary>Nothing moves and the clock is frozen (US-6.1, US-6.2).</summary>
        Paused,
        Ended,
    }

    /// <summary>
    /// The complete, copyable state of a match (TDD 4.1). Used for network snapshots,
    /// offline resume and tests. Holds no references to config or rules.
    /// </summary>
    [Serializable]
    public sealed class MatchState
    {
        public const int MaxSeats = SeatFrame.SeatCount;
        public const int MaxBalls = MatchSettings.MaxBalls;

        public int Tick;
        public MatchPhase Phase = MatchPhase.Countdown;
        /// <summary>Seconds left in the current countdown.</summary>
        public float CountdownRemaining;
        /// <summary>Seconds left on the match clock (Timed mode).</summary>
        public float ClockRemaining;
        /// <summary>Seconds of Playing time so far.</summary>
        public float Elapsed;
        public bool BallsLaunched;
        public bool SuddenDeath;
        public bool FinalCountdownRaised;
        public int EliminationCount;
        /// <summary>Winning seat once ended, or -1 (also -1 for a draw).</summary>
        public int WinnerSeat = -1;
        public bool IsDraw;
        /// <summary>Practice-only multiplier on ball speeds (US-3.5).</summary>
        public float SpeedMultiplier = 1f;
        public SimRandom Random;

        public SeatState[] Seats = new SeatState[MaxSeats];
        public BallState[] Balls = new BallState[MaxBalls];

        public int ActiveSeatCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Seats.Length; i++) if (Seats[i].IsActive) n++;
                return n;
            }
        }

        public void CopyFrom(MatchState other)
        {
            Tick = other.Tick;
            Phase = other.Phase;
            CountdownRemaining = other.CountdownRemaining;
            ClockRemaining = other.ClockRemaining;
            Elapsed = other.Elapsed;
            BallsLaunched = other.BallsLaunched;
            SuddenDeath = other.SuddenDeath;
            FinalCountdownRaised = other.FinalCountdownRaised;
            EliminationCount = other.EliminationCount;
            WinnerSeat = other.WinnerSeat;
            IsDraw = other.IsDraw;
            SpeedMultiplier = other.SpeedMultiplier;
            Random = other.Random;
            Array.Copy(other.Seats, Seats, MaxSeats);
            Array.Copy(other.Balls, Balls, MaxBalls);
        }

        public MatchState Clone()
        {
            var copy = new MatchState();
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>Exact field-by-field comparison, used by determinism tests and snapshot checks.</summary>
        public bool ContentEquals(MatchState other)
        {
            if (Tick != other.Tick || Phase != other.Phase || CountdownRemaining != other.CountdownRemaining
                || ClockRemaining != other.ClockRemaining || Elapsed != other.Elapsed
                || BallsLaunched != other.BallsLaunched || SuddenDeath != other.SuddenDeath
                || FinalCountdownRaised != other.FinalCountdownRaised || EliminationCount != other.EliminationCount
                || WinnerSeat != other.WinnerSeat || IsDraw != other.IsDraw
                || SpeedMultiplier != other.SpeedMultiplier || Random.State != other.Random.State)
                return false;
            for (int i = 0; i < MaxSeats; i++)
                if (!Seats[i].Equals(other.Seats[i])) return false;
            for (int i = 0; i < MaxBalls; i++)
                if (!Balls[i].Equals(other.Balls[i])) return false;
            return true;
        }
    }
}
