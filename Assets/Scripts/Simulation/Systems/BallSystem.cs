using System;

namespace BOW.Simulation
{
    /// <summary>
    /// Ball movement, collisions and goal detection (TDD 4.4, 4.6).
    /// Balls move in sub-steps of at most half a radius, so they never tunnel through walls or paddles.
    /// </summary>
    internal static class BallSystem
    {
        private const float FrontFaceThreshold = 0.3f;

        public static void Update(MatchSimulation sim, float dt)
        {
            var state = sim.State;
            var cfg = sim.Config;
            float r = cfg.BallRadius;

            for (int b = 0; b < MatchState.MaxBalls; b++)
            {
                ref BallState ball = ref state.Balls[b];
                if (!ball.Active) continue;

                ApplySpeedRules(sim, ref ball, dt);

                float travel = ball.Velocity.Length * dt;
                int steps = Math.Max(1, (int)Math.Ceiling(travel / (r * 0.5f)));
                float subDt = dt / steps;

                for (int s = 0; s < steps && ball.Active; s++)
                {
                    ball.Position += ball.Velocity * subDt;
                    CollideWalls(sim, b, ref ball);
                    CollidePosts(sim, b, ref ball);
                    CollidePaddles(sim, b, ref ball);
                    CheckGoal(sim, b, ref ball);
                }

                if (ball.Active) CheckStall(sim, b, ref ball, dt);
                if (ball.Active) CheckOutOfBounds(sim, b, ref ball);
            }

            CollideBalls(sim);
        }

        private static void ApplySpeedRules(MatchSimulation sim, ref BallState ball, float dt)
        {
            var cfg = sim.Config;
            float mult = sim.State.SpeedMultiplier;
            float max = cfg.MaxSpeed * mult;
            float min = cfg.MinSpeed * mult;
            float hardCap = Math.Max(cfg.BlastSpeed, cfg.MaxSpeed) * mult * 1.25f;

            float speed = ball.Velocity.Length;
            if (speed < 1e-5f)
            {
                // A dead-still ball would never move again; give it a nudge along +Y.
                ball.Velocity = new Vec2(0f, min);
                return;
            }

            float newSpeed = speed * (1f - cfg.Friction * dt);
            if (newSpeed > max) newSpeed = max + (newSpeed - max) * Math.Max(0f, 1f - cfg.ExcessSpeedDecay * dt);
            newSpeed = SimMath.Clamp(newSpeed, min, hardCap);
            ball.Velocity = ball.Velocity * (newSpeed / speed);
        }

        private static void CollideWalls(MatchSimulation sim, int index, ref BallState ball)
        {
            var arena = sim.Arena;
            float h = arena.HalfSize;
            float g = arena.GoalHalfWidth;
            float r = sim.Config.BallRadius;

            for (int seat = 0; seat < MatchState.MaxSeats; seat++)
            {
                Vec2 inward = SeatFrame.Inward(seat);
                // The two wall stretches either side of the goal opening.
                CollideSegment(sim, index, ref ball, SeatFrame.ToWorld(seat, h, -h, 0f), SeatFrame.ToWorld(seat, h, -g, 0f), inward, r);
                CollideSegment(sim, index, ref ball, SeatFrame.ToWorld(seat, h, g, 0f), SeatFrame.ToWorld(seat, h, h, 0f), inward, r);
                // A closed, eliminated or spectating seat's goal is a wall (US-1.1, US-3.2, US-3.3).
                if (!sim.State.Seats[seat].GoalOpen)
                    CollideSegment(sim, index, ref ball, SeatFrame.ToWorld(seat, h, -g, 0f), SeatFrame.ToWorld(seat, h, g, 0f), inward, r);
            }

            foreach (var bumper in arena.Bumpers)
                CollideCircle(sim, index, ref ball, bumper.Center, bumper.Radius + r);
        }

        private static void CollidePosts(MatchSimulation sim, int index, ref BallState ball)
        {
            var arena = sim.Arena;
            if (arena.PostRadius <= 0f) return;
            float reach = arena.PostRadius + sim.Config.BallRadius;
            for (int seat = 0; seat < MatchState.MaxSeats; seat++)
            {
                CollideCircle(sim, index, ref ball, SeatFrame.ToWorld(seat, arena.HalfSize, -arena.GoalHalfWidth, 0f), reach);
                CollideCircle(sim, index, ref ball, SeatFrame.ToWorld(seat, arena.HalfSize, arena.GoalHalfWidth, 0f), reach);
            }
        }

        private static void CollideSegment(MatchSimulation sim, int index, ref BallState ball, Vec2 a, Vec2 b, Vec2 fallbackNormal, float radius)
        {
            Vec2 closest = SimMath.ClosestPointOnSegment(ball.Position, a, b);
            Vec2 d = ball.Position - closest;
            float distSq = d.LengthSquared;
            if (distSq >= radius * radius) return;

            float dist = (float)Math.Sqrt(distSq);
            Vec2 n = dist > 1e-5f ? d / dist : fallbackNormal;
            ball.Position = closest + n * radius;
            Reflect(sim, index, ref ball, n, SimEventType.WallBounce);
        }

        private static void CollideCircle(MatchSimulation sim, int index, ref BallState ball, Vec2 center, float reach)
        {
            Vec2 d = ball.Position - center;
            float distSq = d.LengthSquared;
            if (distSq >= reach * reach) return;

            float dist = (float)Math.Sqrt(distSq);
            Vec2 n = dist > 1e-5f ? d / dist : -center.Normalized;
            ball.Position = center + n * reach;
            Reflect(sim, index, ref ball, n, SimEventType.WallBounce);
        }

        private static void Reflect(MatchSimulation sim, int index, ref BallState ball, Vec2 normal, SimEventType evt)
        {
            float vn = Vec2.Dot(ball.Velocity, normal);
            if (vn >= 0f) return; // already moving away
            ball.Velocity -= normal * (2f * vn);
            sim.Emit(evt, -1, index, ball.Position);
        }

        private static void CollidePaddles(MatchSimulation sim, int index, ref BallState ball)
        {
            var cfg = sim.Config;
            float reach = cfg.BallRadius + cfg.PaddleRadius;

            for (int seat = 0; seat < MatchState.MaxSeats; seat++)
            {
                ref SeatState seatState = ref sim.State.Seats[seat];
                if (!seatState.IsActive) continue;

                Vec2 right = SeatFrame.Right(seat);
                Vec2 inward = SeatFrame.Inward(seat);
                Vec2 center = PaddleSystem.PaddleCenter(sim, seat);
                Vec2 a = center - right * cfg.PaddleHalfLength;
                Vec2 b = center + right * cfg.PaddleHalfLength;

                Vec2 closest = SimMath.ClosestPointOnSegment(ball.Position, a, b);
                Vec2 d = ball.Position - closest;
                float distSq = d.LengthSquared;
                if (distSq >= reach * reach) continue;

                float dist = (float)Math.Sqrt(distSq);
                Vec2 n = dist > 1e-5f ? d / dist : inward;
                ball.Position = closest + n * reach;

                Vec2 paddleVel = right * seatState.PaddleVelocity;
                if (Vec2.Dot(ball.Velocity - paddleVel, n) >= 0f) continue; // separating

                bool headingForGoal = Vec2.Dot(ball.Velocity, inward) < 0f;

                if (Vec2.Dot(n, inward) > FrontFaceThreshold)
                {
                    // Front-face hit: angle depends on where it hit (edge hits deflect more), plus some paddle spin.
                    float along = Vec2.Dot(ball.Position - center, right) / (cfg.PaddleHalfLength + cfg.PaddleRadius);
                    float angle = SimMath.Clamp(along, -1f, 1f) * cfg.MaxDeflectionDegrees * SimMath.Deg2Rad;
                    Vec2 dir = inward * (float)Math.Cos(angle) + right * (float)Math.Sin(angle);
                    float speed = Math.Max(ball.Velocity.Length, cfg.MinSpeed * sim.State.SpeedMultiplier);
                    ball.Velocity = dir * speed + right * (seatState.PaddleVelocity * cfg.PaddleVelocityTransfer);
                }
                else
                {
                    float vn = Vec2.Dot(ball.Velocity - paddleVel, n);
                    ball.Velocity -= n * (2f * vn);
                }

                ball.LastTouchSeat = seat;
                ball.SlowTime = 0f;
                sim.Emit(SimEventType.PaddleHit, seat, index, ball.Position);
                if (headingForGoal && Vec2.Dot(ball.Velocity, inward) > 0f)
                {
                    seatState.Saves++;
                    sim.Emit(SimEventType.Save, seat, index, ball.Position);
                }
            }
        }

        private static void CheckGoal(MatchSimulation sim, int index, ref BallState ball)
        {
            var arena = sim.Arena;
            float r = sim.Config.BallRadius;
            for (int seat = 0; seat < MatchState.MaxSeats; seat++)
            {
                if (!sim.State.Seats[seat].GoalOpen) continue;
                float depth = SeatFrame.Depth(seat, arena.HalfSize, ball.Position);
                if (depth <= r) continue; // must be fully across the line (US-2.5)
                float along = SeatFrame.Along(seat, arena.HalfSize, ball.Position);
                if (Math.Abs(along) >= arena.GoalHalfWidth) continue;

                Vec2 pos = ball.Position;
                LauncherSystem.QueueRespawn(sim, index);
                sim.OnGoal(seat, index, pos);
                return;
            }
        }

        private static void CheckStall(MatchSimulation sim, int index, ref BallState ball, float dt)
        {
            var cfg = sim.Config;
            if (ball.Velocity.Length >= cfg.StallSpeed * sim.State.SpeedMultiplier)
            {
                ball.SlowTime = 0f;
                return;
            }

            ball.SlowTime += dt;
            if (ball.SlowTime < cfg.StallSeconds) return;

            int target = LauncherSystem.PickOpenSeat(sim);
            if (target < 0) return;
            Vec2 goal = SeatFrame.GoalCenter(target, sim.Arena.HalfSize);
            Vec2 dir = (goal - ball.Position).Normalized;
            if (dir.LengthSquared < 0.5f) dir = -SeatFrame.Inward(target);
            ball.Velocity = dir * (cfg.LaunchSpeed * sim.State.SpeedMultiplier);
            ball.SlowTime = 0f;
            sim.Emit(SimEventType.StallNudge, target, index, ball.Position);
        }

        private static void CheckOutOfBounds(MatchSimulation sim, int index, ref BallState ball)
        {
            float limit = sim.Arena.HalfSize + 3f;
            if (ball.Position.IsFinite && ball.Velocity.IsFinite
                && Math.Abs(ball.Position.X) <= limit && Math.Abs(ball.Position.Y) <= limit)
                return;
            // Should never happen; recover without scoring a goal.
            LauncherSystem.QueueRespawn(sim, index);
        }

        private static void CollideBalls(MatchSimulation sim)
        {
            var balls = sim.State.Balls;
            float minDist = sim.Config.BallRadius * 2f;
            for (int i = 0; i < MatchState.MaxBalls; i++)
            {
                if (!balls[i].Active) continue;
                for (int j = i + 1; j < MatchState.MaxBalls; j++)
                {
                    if (!balls[j].Active) continue;
                    Vec2 d = balls[j].Position - balls[i].Position;
                    float distSq = d.LengthSquared;
                    if (distSq >= minDist * minDist) continue;

                    float dist = (float)Math.Sqrt(distSq);
                    Vec2 n = dist > 1e-5f ? d / dist : new Vec2(1f, 0f);
                    float push = (minDist - dist) * 0.5f;
                    balls[i].Position -= n * push;
                    balls[j].Position += n * push;

                    float approach = Vec2.Dot(balls[j].Velocity - balls[i].Velocity, n);
                    if (approach >= 0f) continue;
                    // Equal-mass elastic collision: swap the normal components.
                    Vec2 impulse = n * approach;
                    balls[i].Velocity += impulse;
                    balls[j].Velocity -= impulse;
                    sim.Emit(SimEventType.BallCollision, -1, i, balls[i].Position + n * (minDist * 0.5f));
                }
            }
        }
    }
}
