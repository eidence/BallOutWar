using System;

namespace BOW.Simulation
{
    public enum SeatStatus : byte
    {
        /// <summary>No player; the goal is a wall (US-1.1).</summary>
        Closed,
        /// <summary>Playing; goal open, paddle present.</summary>
        Active,
        /// <summary>Knocked out; goal sealed, paddle removed (US-3.3, US-3.2).</summary>
        Eliminated,
        /// <summary>Not part of sudden death; goal sealed, paddle removed (US-3.2).</summary>
        Spectating,
    }

    [Serializable]
    public struct SeatState
    {
        public SeatStatus Status;

        // Paddle (position along the goal line, + = player's right)
        public float PaddlePosition;
        public float PaddleVelocity;
        public float BlastCooldown;

        // Scoring
        public int GoalsAgainst;
        public int Lives;
        /// <summary>Order this seat was knocked out (1 = first), or 0 if never.</summary>
        public int EliminationOrder;
        /// <summary>Final place (1 = winner) once the match has ended, else 0.</summary>
        public int Placement;

        // Stats for the results screen (US-3.4)
        public int Blasts;
        public int Saves;
        public float TimeSinceConceded;
        public float LongestCleanTime;

        public bool IsActive => Status == SeatStatus.Active;
        public bool GoalOpen => Status == SeatStatus.Active;
    }
}
