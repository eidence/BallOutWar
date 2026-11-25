using System;
using System.Collections.Generic;

namespace BOW.Simulation
{
    [Serializable]
    public struct CircleObstacle
    {
        public Vec2 Center;
        public float Radius;

        public CircleObstacle(Vec2 center, float radius)
        {
            Center = center;
            Radius = radius;
        }
    }

    /// <summary>
    /// Arena layout as data (TDD 4.2, 4.9): a square floor with a goal opening in the middle of each side,
    /// a post at each end of every opening, and optional round bumpers. New arenas need no code.
    /// </summary>
    [Serializable]
    public sealed class ArenaDefinition
    {
        public string Id = "default";
        public float HalfSize = 10f;
        public float GoalHalfWidth = 6f;
        public float PostRadius = 0.5f;
        public CircleObstacle[] Bumpers = new CircleObstacle[0];

        public static ArenaDefinition CreateDefault() => new ArenaDefinition();

        /// <summary>
        /// Checks the fairness rules from TDD 10.4: every seat has the same goal and the layout
        /// is identical under a 90-degree rotation. Returns an empty list when the arena is valid.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (HalfSize <= 0f) errors.Add("HalfSize must be positive.");
            if (GoalHalfWidth <= 0f) errors.Add("GoalHalfWidth must be positive.");
            if (GoalHalfWidth + PostRadius >= HalfSize) errors.Add("Goal opening plus posts must fit inside the side.");
            if (PostRadius < 0f) errors.Add("PostRadius cannot be negative.");

            const float tolerance = 0.01f;
            foreach (var bumper in Bumpers)
            {
                if (bumper.Radius <= 0f) errors.Add($"Bumper at {bumper.Center} has no radius.");
                Vec2 rotated = bumper.Center.Rotate(90f * SimMath.Deg2Rad);
                bool found = false;
                foreach (var other in Bumpers)
                {
                    if (Vec2.Distance(other.Center, rotated) < tolerance && Math.Abs(other.Radius - bumper.Radius) < tolerance)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) errors.Add($"Bumper at {bumper.Center} has no 90-degree rotated twin; arena is not symmetric.");
            }
            return errors;
        }
    }
}
