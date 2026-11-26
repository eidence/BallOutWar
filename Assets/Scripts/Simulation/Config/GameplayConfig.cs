using System;

namespace BOW.Simulation
{
    /// <summary>
    /// Every tunable gameplay number (TDD 4.1). Values marked "tunable" in the requirements live here.
    /// A ScriptableObject wrapper in the game layer can copy its fields into this class.
    /// Distances are in arena units (default arena is 20 x 20), times in seconds.
    /// </summary>
    [Serializable]
    public sealed class GameplayConfig
    {
        // Timing
        public int TickRate = 60;
        public float CountdownSeconds = 3f;
        public float FinalCountdownSeconds = 10f;

        // Paddle (US-2.2)
        public float PaddleHalfLength = 0.8f;
        public float PaddleRadius = 0.4f;
        public float PaddleOffset = 0.8f;      // distance in front of the goal line
        public float PaddleMaxSpeed = 14f;
        public float PaddleAcceleration = 80f;
        public float PaddleDeceleration = 110f;
        public float DragGain = 12f;           // drag-to-position spring strength

        // Ball (US-2.4)
        public float BallRadius = 0.35f;
        public float LaunchSpeed = 7f;
        public float MinSpeed = 2f;
        public float MaxSpeed = 12f;
        public float ExcessSpeedDecay = 1.5f;  // fraction of speed above MaxSpeed lost per second
        public float Friction = 0.05f;         // fraction of speed lost per second
        public float MaxDeflectionDegrees = 60f;
        public float PaddleVelocityTransfer = 0.3f;
        public float StallSpeed = 3f;
        public float StallSeconds = 5f;
        public float LaunchJitterDegrees = 25f;
        public float RespawnDelay = 1f;
        public float LaunchStagger = 0.5f;     // gap between initial ball launches

        // Blast (US-2.3)
        public float BlastRadius = 2.2f;
        public float BlastSpeed = 18f;
        public float BlastCooldown = 1.5f;

        public float FixedDeltaTime => 1f / TickRate;

        public static GameplayConfig CreateDefault() => new GameplayConfig();

        public GameplayConfig Clone() => (GameplayConfig)MemberwiseClone();
    }
}
