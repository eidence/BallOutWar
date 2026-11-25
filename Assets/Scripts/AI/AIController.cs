using System;
using BOW.Simulation;

namespace BOW.AI
{
    /// <summary>
    /// Computer opponent (TDD 7). Each tick it looks at a delayed, noisy picture of the balls and produces
    /// the same <see cref="PlayerInput"/> a human would, so it plays by identical rules (US-4.2).
    /// Runs only where the simulation runs: offline, or on the multiplayer host (TDD 7.1).
    /// </summary>
    public sealed class AIController
    {
        private const int HistorySize = 61;               // up to 1 s of delay at 60 Hz
        private const float ErrorResampleSeconds = 0.5f;  // misjudge consistently, don't jitter (TDD 7.2)
        private const float DefensiveBlastWindow = 0.3f;  // seconds before a ball reaches the paddle
        private const float HardRandomTargetChance = 0.3f;
        /// <summary>Only attack when no tracked ball will reach this goal sooner than this, keeping the blast for defence.</summary>
        private const float SafeToAttackSeconds = 1.2f;

        private readonly BallState[,] _history = new BallState[HistorySize, MatchState.MaxBalls];
        private readonly int[] _order = new int[MatchState.MaxBalls];
        private readonly float[] _distance = new float[MatchState.MaxBalls];
        private SimRandom _random;
        private int _head = -1;
        private int _count;
        private float _error;
        private float _errorTimer;

        public int Seat { get; }
        public AIDifficultyProfile Profile { get; }

        public AIController(int seat, AIDifficultyProfile profile, uint seed)
        {
            if (seat < 0 || seat >= SeatFrame.SeatCount) throw new ArgumentOutOfRangeException(nameof(seat));
            Seat = seat;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _random = new SimRandom(seed);
        }

        public AIController(int seat, AIDifficulty difficulty, uint seed)
            : this(seat, AIDifficultyProfile.For(difficulty), seed) { }

        /// <summary>Call exactly once per simulation tick, before <see cref="MatchSimulation.Step"/>.</summary>
        public PlayerInput Think(MatchSimulation sim)
        {
            var state = sim.State;
            var cfg = sim.Config;
            Record(state);

            _errorTimer -= cfg.FixedDeltaTime;
            if (_errorTimer <= 0f)
            {
                _errorTimer = ErrorResampleSeconds;
                _error = Gaussian() * Profile.AimErrorSigma;
            }

            SeatState me = state.Seats[Seat];
            if (!me.IsActive) return default;

            float h = sim.Arena.HalfSize;
            Vec2 paddle = SeatFrame.ToWorld(Seat, h, me.PaddlePosition, cfg.PaddleOffset);

            // The AI sees the balls as they were ReactionDelay ago.
            int delay = Math.Min(Profile.DelayTicks(cfg), _count - 1);
            int view = (_head - delay + HistorySize) % HistorySize;

            int seen = SortVisibleBalls(view, paddle);
            int tracked = Profile.ThreatsTracked <= 0 ? seen : Math.Min(seen, Profile.ThreatsTracked);

            float bestTime = float.MaxValue;
            float bestAlong = 0f;
            int bestBall = -1;
            for (int k = 0; k < tracked; k++)
            {
                int b = _order[k];
                if (TryPredictImpact(sim, _history[view, b], out float time, out float along) && time < bestTime)
                {
                    bestTime = time;
                    bestAlong = along;
                    bestBall = b;
                }
            }

            float target = bestBall >= 0 ? bestAlong + _error : 0f;
            bool blast = false;

            if (me.BlastCooldown <= 0f && state.Phase == MatchPhase.Playing)
            {
                float reach = cfg.BlastRadius + cfg.BallRadius;
                if (bestBall >= 0 && bestTime < DefensiveBlastWindow
                    && Vec2.Distance(_history[view, bestBall].Position, paddle) <= reach)
                {
                    // Defensive blast; Easy sometimes gets the timing wrong (TDD 7.3).
                    blast = _random.NextFloat() >= Profile.MistimeChance;
                }
                else if (Profile.OffensiveBlasts && bestTime > SafeToAttackSeconds)
                {
                    for (int k = 0; k < seen && !blast; k++)
                    {
                        Vec2 pos = _history[view, _order[k]].Position;
                        if (Vec2.Distance(pos, paddle) > reach) continue;
                        Vec2 dir = (pos - paddle).Normalized;
                        if (dir.LengthSquared < 0.5f) continue;
                        if (TryFindGoalOnLine(sim, pos, dir, out int victim, out float hitAlong) && victim != Seat)
                            blast = WantsToBlastAt(sim, victim, hitAlong);
                    }
                }
            }

            return PlayerInput.ToTarget(target, blast);
        }

        private void Record(MatchState state)
        {
            _head = (_head + 1) % HistorySize;
            for (int b = 0; b < MatchState.MaxBalls; b++) _history[_head, b] = state.Balls[b];
            if (_count < HistorySize) _count++;
        }

        /// <summary>Fills <see cref="_order"/> with visible balls, nearest to the paddle first. Returns the count.</summary>
        private int SortVisibleBalls(int view, Vec2 paddle)
        {
            int n = 0;
            for (int b = 0; b < MatchState.MaxBalls; b++)
            {
                if (!_history[view, b].Active) continue;
                float d = Vec2.Distance(_history[view, b].Position, paddle);
                int i = n++;
                while (i > 0 && _distance[i - 1] > d)
                {
                    _distance[i] = _distance[i - 1];
                    _order[i] = _order[i - 1];
                    i--;
                }
                _distance[i] = d;
                _order[i] = b;
            }
            return n;
        }

        /// <summary>Where along the goal line, and how soon, a ball would reach this seat's paddle line.</summary>
        private bool TryPredictImpact(MatchSimulation sim, BallState ball, out float time, out float along)
        {
            time = 0f;
            along = 0f;
            var cfg = sim.Config;
            float h = sim.Arena.HalfSize;
            Vec2 goal = SeatFrame.GoalCenter(Seat, h);
            Vec2 inward = SeatFrame.Inward(Seat);
            Vec2 right = SeatFrame.Right(Seat);

            float approach = -Vec2.Dot(ball.Velocity, inward);
            if (approach <= 0.1f) return false; // not coming this way

            float distance = Vec2.Dot(ball.Position - goal, inward) - cfg.PaddleOffset;
            time = Math.Max(0f, distance) / approach;
            along = Vec2.Dot(ball.Position - goal, right) + Vec2.Dot(ball.Velocity, right) * time;

            if (Profile.PredictsBounces)
            {
                // Fold the path back off the side walls (one or more bounces).
                float limit = h - cfg.BallRadius;
                for (int i = 0; i < 3 && Math.Abs(along) > limit; i++)
                    along = Math.Sign(along) * (2f * limit - Math.Abs(along));
            }

            // A ball heading for the wall beside the goal is not a threat (yet).
            return Math.Abs(along) < sim.Arena.GoalHalfWidth + cfg.BallRadius;
        }

        /// <summary>
        /// Follows a straight line from <paramref name="from"/> to the first side of the arena it reaches,
        /// and reports whose goal it would enter, if that side's goal is open.
        /// </summary>
        internal static bool TryFindGoalOnLine(MatchSimulation sim, Vec2 from, Vec2 dir, out int seat, out float along)
        {
            seat = -1;
            along = 0f;
            float h = sim.Arena.HalfSize;
            float bestT = float.MaxValue;
            int bestSide = -1;
            float bestAlong = 0f;

            for (int s = 0; s < SeatFrame.SeatCount; s++)
            {
                Vec2 inward = SeatFrame.Inward(s);
                float towardSide = -Vec2.Dot(dir, inward);
                if (towardSide <= 1e-4f) continue;
                Vec2 goal = SeatFrame.GoalCenter(s, h);
                float t = Vec2.Dot(from - goal, inward) / towardSide;
                if (t < 0f || t >= bestT) continue;
                bestT = t;
                bestSide = s;
                bestAlong = Vec2.Dot(from + dir * t - goal, SeatFrame.Right(s));
            }

            if (bestSide < 0 || !sim.State.Seats[bestSide].GoalOpen) return false;
            if (Math.Abs(bestAlong) >= sim.Arena.GoalHalfWidth - sim.Config.BallRadius) return false;
            seat = bestSide;
            along = bestAlong;
            return true;
        }

        private bool WantsToBlastAt(MatchSimulation sim, int victim, float hitAlong)
        {
            var cfg = sim.Config;
            if (Profile.AimsForOpenSpace)
            {
                // Expert: only when the shot lands well clear of the defending paddle.
                float gap = Math.Abs(hitAlong - sim.State.Seats[victim].PaddlePosition);
                return gap > cfg.PaddleHalfLength + cfg.PaddleRadius + 0.8f;
            }
            // Hard: goes after whoever is closest to losing, with the odd opportunistic shot.
            return victim == WeakestOpponent(sim) || _random.NextFloat() < HardRandomTargetChance;
        }

        private int WeakestOpponent(MatchSimulation sim)
        {
            var seats = sim.State.Seats;
            bool elimination = sim.Settings.Mode == GameMode.Elimination;
            int weakest = -1;
            for (int s = 0; s < SeatFrame.SeatCount; s++)
            {
                if (s == Seat || !seats[s].IsActive) continue;
                if (weakest < 0) { weakest = s; continue; }
                bool worse = elimination
                    ? seats[s].Lives < seats[weakest].Lives
                    : seats[s].GoalsAgainst > seats[weakest].GoalsAgainst;
                if (worse) weakest = s;
            }
            return weakest;
        }

        private float Gaussian()
        {
            // Box-Muller transform on the AI's own random stream (never the match's).
            float u1 = Math.Max(1e-6f, _random.NextFloat());
            float u2 = _random.NextFloat();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
