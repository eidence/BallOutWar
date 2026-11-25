namespace BOW.Simulation
{
    /// <summary>Launches balls from the arena centre toward a random open goal (TDD 4.6).</summary>
    internal static class LauncherSystem
    {
        /// <summary>Queues the opening launch on Go: one ball per slot, staggered.</summary>
        public static void QueueInitialLaunch(MatchSimulation sim)
        {
            var balls = sim.State.Balls;
            int count = sim.Settings.BallCount;
            for (int i = 0; i < MatchState.MaxBalls; i++)
            {
                balls[i] = default;
                balls[i].LastTouchSeat = -1;
                if (i < count)
                {
                    balls[i].PendingSpawn = true;
                    balls[i].SpawnTimer = i * sim.Config.LaunchStagger;
                }
            }
            sim.State.BallsLaunched = true;
            Update(sim, 0f);
        }

        public static void QueueRespawn(MatchSimulation sim, int ballIndex)
        {
            ref BallState ball = ref sim.State.Balls[ballIndex];
            ball.Active = false;
            ball.Velocity = Vec2.Zero;
            ball.PendingSpawn = ballIndex < sim.Settings.BallCount;
            ball.SpawnTimer = sim.Config.RespawnDelay;
        }

        public static void Update(MatchSimulation sim, float dt)
        {
            var balls = sim.State.Balls;
            for (int i = 0; i < MatchState.MaxBalls; i++)
            {
                ref BallState ball = ref balls[i];
                if (!ball.PendingSpawn) continue;
                ball.SpawnTimer -= dt;
                if (ball.SpawnTimer > 0f) continue;
                Spawn(sim, i);
            }
        }

        private static void Spawn(MatchSimulation sim, int index)
        {
            var state = sim.State;
            int target = PickOpenSeat(sim);
            if (target < 0) return; // nobody left to aim at; match is ending

            var cfg = sim.Config;
            ref SimRandom rng = ref state.Random;
            Vec2 toGoal = -SeatFrame.Inward(target);
            float jitter = rng.Range(-cfg.LaunchJitterDegrees, cfg.LaunchJitterDegrees) * SimMath.Deg2Rad;
            Vec2 offset = new Vec2(rng.Range(-0.5f, 0.5f), rng.Range(-0.5f, 0.5f));

            ref BallState ball = ref state.Balls[index];
            ball.Active = true;
            ball.PendingSpawn = false;
            ball.SpawnTimer = 0f;
            ball.Position = offset;
            ball.Velocity = toGoal.Rotate(jitter) * (cfg.LaunchSpeed * state.SpeedMultiplier);
            ball.LastTouchSeat = -1;
            ball.SlowTime = 0f;
            sim.Emit(SimEventType.BallSpawned, target, index, ball.Position);
        }

        /// <summary>Random seat whose goal is open, or -1.</summary>
        public static int PickOpenSeat(MatchSimulation sim)
        {
            var seats = sim.State.Seats;
            int open = 0;
            for (int i = 0; i < MatchState.MaxSeats; i++) if (seats[i].GoalOpen) open++;
            if (open == 0) return -1;
            int pick = sim.State.Random.Range(0, open);
            for (int i = 0; i < MatchState.MaxSeats; i++)
            {
                if (!seats[i].GoalOpen) continue;
                if (pick-- == 0) return i;
            }
            return -1;
        }
    }
}
