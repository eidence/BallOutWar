using System;

namespace BOW.Simulation
{
    [Serializable]
    public struct BallState
    {
        public bool Active;
        public Vec2 Position;
        public Vec2 Velocity;

        /// <summary>Seat that last touched the ball (paddle or blast), or -1. Stats only; never affects scoring.</summary>
        public int LastTouchSeat;

        /// <summary>Seconds the ball has been under the stall speed.</summary>
        public float SlowTime;

        /// <summary>True while the slot is waiting to launch a ball.</summary>
        public bool PendingSpawn;

        /// <summary>Seconds until a pending ball launches.</summary>
        public float SpawnTimer;
    }
}
