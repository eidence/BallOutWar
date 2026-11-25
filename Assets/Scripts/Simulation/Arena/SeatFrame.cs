namespace BOW.Simulation
{
    /// <summary>
    /// Per-seat coordinate frames (TDD 4.2). Seats 0-3 sit South, East, North, West.
    /// Each seat's frame is the South frame rotated by seat x 90 degrees counter-clockwise, so paddle
    /// logic is written once: <see cref="Right"/> runs along the goal line to the player's right,
    /// and <see cref="Inward"/> points from the goal into the arena.
    /// </summary>
    public static class SeatFrame
    {
        public const int SeatCount = 4;

        private static readonly Vec2[] InwardDirs =
        {
            new Vec2(0f, 1f),   // South
            new Vec2(-1f, 0f),  // East
            new Vec2(0f, -1f),  // North
            new Vec2(1f, 0f),   // West
        };

        private static readonly Vec2[] RightDirs =
        {
            new Vec2(1f, 0f),
            new Vec2(0f, 1f),
            new Vec2(-1f, 0f),
            new Vec2(0f, -1f),
        };

        public static Vec2 Inward(int seat) => InwardDirs[seat];
        public static Vec2 Right(int seat) => RightDirs[seat];

        /// <summary>Centre of the seat's goal line.</summary>
        public static Vec2 GoalCenter(int seat, float halfSize) => InwardDirs[seat] * -halfSize;

        /// <summary>World position of a point given as (along the goal line, distance inward).</summary>
        public static Vec2 ToWorld(int seat, float halfSize, float along, float inward)
            => GoalCenter(seat, halfSize) + RightDirs[seat] * along + InwardDirs[seat] * inward;

        /// <summary>Position along the goal line (+ = player's right).</summary>
        public static float Along(int seat, float halfSize, Vec2 world)
            => Vec2.Dot(world - GoalCenter(seat, halfSize), RightDirs[seat]);

        /// <summary>Distance past the goal line, outward (positive = behind the goal line).</summary>
        public static float Depth(int seat, float halfSize, Vec2 world)
            => -Vec2.Dot(world - GoalCenter(seat, halfSize), InwardDirs[seat]);
    }
}
