using System;

namespace BOW.Simulation
{
    public static class SimMath
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        public static float Clamp01(float v) => Clamp(v, 0f, 1f);

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float diff = target - current;
            if (Math.Abs(diff) <= maxDelta) return target;
            return current + Math.Sign(diff) * maxDelta;
        }

        /// <summary>Closest point to <paramref name="p"/> on segment AB.</summary>
        public static Vec2 ClosestPointOnSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float lenSq = ab.LengthSquared;
            if (lenSq < 1e-8f) return a;
            float t = Clamp01(Vec2.Dot(p - a, ab) / lenSq);
            return a + ab * t;
        }

        /// <summary>Replaces NaN/infinity with zero so bad input can never poison the simulation.</summary>
        public static float Sanitize(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
    }
}
