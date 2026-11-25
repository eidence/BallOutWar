using System;

namespace BOW.Simulation
{
    public enum SimEventType : byte
    {
        PhaseChanged,
        BallSpawned,
        WallBounce,
        BallCollision,
        PaddleHit,
        Save,
        Blast,
        BlastDenied,
        Goal,
        Eliminated,
        SuddenDeathStarted,
        FinalCountdown,
        StallNudge,
        MatchEnded,
    }

    /// <summary>
    /// Something presentation should react to (TDD 4.7): sounds, VFX, haptics, HUD and stats.
    /// Fields not relevant to an event type are -1 (seat/ball) or zero.
    /// </summary>
    [Serializable]
    public struct SimEvent
    {
        public SimEventType Type;
        public int Tick;
        public int Seat;
        public int Ball;
        public Vec2 Position;

        public override string ToString() => $"{Type} tick={Tick} seat={Seat} ball={Ball} pos={Position}";
    }
}
