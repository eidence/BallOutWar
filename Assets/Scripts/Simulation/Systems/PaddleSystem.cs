using System;

namespace BOW.Simulation
{
    /// <summary>Paddle movement (TDD 4.3) and blast (TDD 4.5).</summary>
    internal static class PaddleSystem
    {
        /// <summary>Furthest the paddle centre can move from the goal centre: its end cap touches the post.</summary>
        public static float Limit(GameplayConfig cfg, ArenaDefinition arena)
            => Math.Max(0f, arena.GoalHalfWidth - arena.PostRadius - (cfg.PaddleHalfLength + cfg.PaddleRadius));

        public static Vec2 PaddleCenter(MatchSimulation sim, int seat)
            => SeatFrame.ToWorld(seat, sim.Arena.HalfSize, sim.State.Seats[seat].PaddlePosition, sim.Config.PaddleOffset);

        public static void Update(MatchSimulation sim, PlayerInput[] inputs, float dt, bool blastAllowed)
        {
            var cfg = sim.Config;
            float limit = Limit(cfg, sim.Arena);
            var seats = sim.State.Seats;

            for (int i = 0; i < MatchState.MaxSeats; i++)
            {
                ref SeatState seat = ref seats[i];
                if (!seat.IsActive) continue;

                PlayerInput input = inputs != null && i < inputs.Length ? inputs[i] : default;
                Move(ref seat, input, cfg, limit, dt);

                if (input.BlastPressed)
                {
                    if (!blastAllowed || seat.BlastCooldown > 0f)
                        sim.Emit(SimEventType.BlastDenied, i, -1, PaddleCenter(sim, i));
                    else
                        Blast(sim, i);
                }
            }
        }

        /// <summary>
        /// Moves one paddle for one tick and runs down its blast cooldown. Shared with guest-side prediction
        /// (<see cref="PaddlePrediction"/>), so a predicted paddle moves exactly as the host's does.
        /// </summary>
        internal static void Move(ref SeatState seat, PlayerInput input, GameplayConfig cfg, float limit, float dt)
        {
            // Movement: analog stick or drag target, both capped at the same max speed.
            float desired;
            if (input.HasTarget)
            {
                float target = SimMath.Clamp(SimMath.Sanitize(input.Target), -limit, limit);
                desired = SimMath.Clamp((target - seat.PaddlePosition) * cfg.DragGain, -cfg.PaddleMaxSpeed, cfg.PaddleMaxSpeed);
            }
            else
            {
                desired = SimMath.Clamp(SimMath.Sanitize(input.Move), -1f, 1f) * cfg.PaddleMaxSpeed;
            }

            bool speedingUp = Math.Abs(desired) > Math.Abs(seat.PaddleVelocity)
                              && (seat.PaddleVelocity == 0f || Math.Sign(desired) == Math.Sign(seat.PaddleVelocity));
            float rate = speedingUp ? cfg.PaddleAcceleration : cfg.PaddleDeceleration;
            seat.PaddleVelocity = SimMath.MoveTowards(seat.PaddleVelocity, desired, rate * dt);
            seat.PaddlePosition += seat.PaddleVelocity * dt;

            if (seat.PaddlePosition > limit) { seat.PaddlePosition = limit; seat.PaddleVelocity = Math.Min(0f, seat.PaddleVelocity); }
            if (seat.PaddlePosition < -limit) { seat.PaddlePosition = -limit; seat.PaddleVelocity = Math.Max(0f, seat.PaddleVelocity); }

            // Blast cooldown runs in Countdown and Playing.
            if (seat.BlastCooldown > 0f) seat.BlastCooldown = Math.Max(0f, seat.BlastCooldown - dt);
        }

        private static void Blast(MatchSimulation sim, int seatIndex)
        {
            var cfg = sim.Config;
            ref SeatState seat = ref sim.State.Seats[seatIndex];
            seat.BlastCooldown = cfg.BlastCooldown;
            seat.Blasts++;

            Vec2 center = PaddleCenter(sim, seatIndex);
            sim.Emit(SimEventType.Blast, seatIndex, -1, center);

            float reach = cfg.BlastRadius + cfg.BallRadius;
            var balls = sim.State.Balls;
            for (int b = 0; b < MatchState.MaxBalls; b++)
            {
                ref BallState ball = ref balls[b];
                if (!ball.Active) continue;
                Vec2 offset = ball.Position - center;
                float dist = offset.Length;
                if (dist > reach) continue;

                Vec2 dir = dist > 1e-4f ? offset / dist : SeatFrame.Inward(seatIndex);
                float speed = Math.Max(cfg.BlastSpeed * sim.State.SpeedMultiplier, ball.Velocity.Length);
                ball.Velocity = dir * speed;
                ball.LastTouchSeat = seatIndex;
                ball.SlowTime = 0f;
            }
        }
    }
}
