using System;
using NUnit.Framework;
using static BOW.Simulation.Tests.SimTestUtil;

namespace BOW.Simulation.Tests
{
    public class DeterminismTests
    {
        [Test] // AD-1, NFR-5: same seed and inputs give the same match
        public void SameSeedAndInputsGiveIdenticalState()
        {
            var a = Create(balls: 3, configure: s => { s.Seed = 1234; s.DurationSeconds = 120f; });
            var b = Create(balls: 3, configure: s => { s.Seed = 1234; s.DurationSeconds = 120f; });
            var rngA = new SimRandom(99);
            var rngB = new SimRandom(99);
            var bufA = new PlayerInput[4];
            var bufB = new PlayerInput[4];

            for (int t = 0; t < 6000; t++)
            {
                a.Step(RandomInputs(ref rngA, bufA));
                b.Step(RandomInputs(ref rngB, bufB));
                if (t % 500 == 0) Assert.IsTrue(a.State.ContentEquals(b.State), $"diverged at tick {t}");
            }
            Assert.IsTrue(a.State.ContentEquals(b.State));
        }

        [Test] // US-6.6 and network snapshots: a copied state carries on identically
        public void LoadedStateContinuesIdentically()
        {
            var a = Create(balls: 2, configure: s => s.Seed = 7);
            var rng = new SimRandom(5);
            var buf = new PlayerInput[4];
            for (int t = 0; t < 1500; t++) a.Step(RandomInputs(ref rng, buf));

            MatchState snapshot = a.State.Clone();
            var b = Create(balls: 2, configure: s => s.Seed = 7);
            b.LoadState(snapshot);
            Assert.IsTrue(a.State.ContentEquals(b.State));

            var rngA = rng;
            var rngB = rng;
            var bufB = new PlayerInput[4];
            for (int t = 0; t < 1500; t++)
            {
                a.Step(RandomInputs(ref rngA, buf));
                b.Step(RandomInputs(ref rngB, bufB));
            }
            Assert.IsTrue(a.State.ContentEquals(b.State));
        }

        [Test] // Soak: a long chaotic match stays valid and finishes
        public void LongRandomMatchStaysValidAndEnds([Values(GameMode.Timed, GameMode.Elimination)] GameMode mode)
        {
            var sim = Create(mode, balls: 4, configure: s =>
            {
                s.Seed = 42;
                s.DurationSeconds = 120f;
                s.GoalsToEliminate = 5;
            });
            var rng = new SimRandom(2024);
            var buf = new PlayerInput[4];
            float bound = sim.Arena.HalfSize + 1f;
            int goals = 0;

            for (int t = 0; t < 60 * 60 * 10 && !sim.IsEnded; t++)
            {
                sim.Step(RandomInputs(ref rng, buf));
                foreach (var e in sim.Events) if (e.Type == SimEventType.Goal) goals++;

                for (int i = 0; i < MatchState.MaxBalls; i++)
                {
                    var ball = sim.State.Balls[i];
                    if (!ball.Active) continue;
                    Assert.IsTrue(ball.Position.IsFinite && ball.Velocity.IsFinite, $"NaN at tick {t}");
                    Assert.LessOrEqual(Math.Abs(ball.Position.X), bound, $"ball {i} escaped at tick {t}");
                    Assert.LessOrEqual(Math.Abs(ball.Position.Y), bound, $"ball {i} escaped at tick {t}");
                }
            }

            Assert.IsTrue(sim.IsEnded, "match should finish within 10 minutes");
            Assert.Greater(goals, 0);
            int winners = 0;
            for (int i = 0; i < 4; i++) if (sim.State.Seats[i].Placement == 1) winners++;
            Assert.GreaterOrEqual(winners, 1);
        }
    }
}
