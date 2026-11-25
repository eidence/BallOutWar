using BOW.AI;
using NUnit.Framework;
using static BOW.Simulation.Tests.SimTestUtil;

namespace BOW.Simulation.Tests
{
    public class AITests
    {
        /// <summary>Plays a 2-player timed match between two AIs and returns goals conceded by each.</summary>
        private static (int a, int b) Duel(AIDifficulty seatA, AIDifficulty seatB, uint seed, float seconds = 60f)
        {
            var sim = Create(GameMode.Timed, balls: 2, seats: new[] { true, false, true, false },
                configure: s => { s.Seed = seed; s.DurationSeconds = seconds; s.TieRule = TieRule.AllowDraw; });
            var a = new AIController(0, seatA, seed * 31 + 1);
            var b = new AIController(2, seatB, seed * 17 + 3);
            var inputs = new PlayerInput[4];
            while (!sim.IsEnded)
            {
                inputs[0] = a.Think(sim);
                inputs[2] = b.Think(sim);
                sim.Step(inputs);
            }
            return (sim.State.Seats[0].GoalsAgainst, sim.State.Seats[2].GoalsAgainst);
        }

        /// <summary>
        /// Four-player timed matches with one AI of each level, rotating seats every match so no level
        /// benefits from a seat. Returns total goals conceded per level.
        /// </summary>
        internal static int[] ConcededByLevel(int matches, float seconds = 60f)
        {
            var conceded = new int[4];
            var inputs = new PlayerInput[4];
            for (int m = 0; m < matches; m++)
            {
                uint seed = (uint)(m + 1);
                var sim = Create(GameMode.Timed, balls: 2,
                    configure: s => { s.Seed = seed; s.DurationSeconds = seconds; s.TieRule = TieRule.AllowDraw; });
                var ais = new AIController[4];
                for (int seat = 0; seat < 4; seat++)
                    ais[seat] = new AIController(seat, (AIDifficulty)((seat + m) % 4), seed * 97 + (uint)seat);

                while (!sim.IsEnded)
                {
                    for (int seat = 0; seat < 4; seat++) inputs[seat] = ais[seat].Think(sim);
                    sim.Step(inputs);
                }
                for (int seat = 0; seat < 4; seat++)
                    conceded[(seat + m) % 4] += sim.State.Seats[seat].GoalsAgainst;
            }
            return conceded;
        }

        [Test] // US-4.1, US-4.2: each level concedes fewer goals than the one below it
        public void HarderLevelsConcedeFewerGoals()
        {
            int[] conceded = ConcededByLevel(16);
            TestContext.WriteLine($"Conceded in 16 four-player minutes - Easy {conceded[0]}, Medium {conceded[1]}, Hard {conceded[2]}, Expert {conceded[3]}");
            Assert.Greater(conceded[0], conceded[1], "Easy vs Medium");
            Assert.Greater(conceded[1], conceded[2], "Medium vs Hard");
            Assert.Greater(conceded[2], conceded[3], "Hard vs Expert");
        }

        [Test] // US-4.2: no level is unbeatable
        public void ExpertStillConcedesGoals()
        {
            int conceded = 0;
            for (uint seed = 1; seed <= 4; seed++) conceded += Duel(AIDifficulty.Expert, AIDifficulty.Expert, seed).a;
            Assert.Greater(conceded, 0);
        }

        [Test] // AI is deterministic for a given seed (needed for host replays and tests)
        public void SameSeedGivesSameDecisions()
        {
            var first = Duel(AIDifficulty.Medium, AIDifficulty.Hard, 5, 30f);
            var second = Duel(AIDifficulty.Medium, AIDifficulty.Hard, 5, 30f);
            Assert.AreEqual(first, second);
        }

        [Test] // TDD 7.3: Hard blasts a ball at an opponent's goal; Medium never attacks
        public void OnlyHardAndExpertUseOffensiveBlasts()
        {
            Assert.IsTrue(BlastsAtOpenGoal(AIDifficulty.Hard));
            Assert.IsFalse(BlastsAtOpenGoal(AIDifficulty.Medium));
        }

        private static bool BlastsAtOpenGoal(AIDifficulty level)
        {
            var sim = Create(seats: new[] { true, false, true, false });
            StartPlayingWithoutBalls(sim);
            sim.OnGoal(2, 0, Vec2.Zero); // North is the weakest opponent
            var ai = new AIController(0, level, 9);
            var inputs = new PlayerInput[4];

            for (int t = 0; t < 30; t++)
            {
                // A slow ball sitting in front of South's paddle, drifting toward North.
                PlaceBall(sim, 0, new Vec2(0f, -7.6f), new Vec2(0f, 0.5f));
                inputs[0] = ai.Think(sim);
                if (inputs[0].BlastPressed) return true;
                sim.Step(inputs);
            }
            return false;
        }

        [Test]
        public void LineTraceFindsTheGoalItWouldEnter()
        {
            var sim = Create(seats: new[] { true, true, true, false });
            Assert.IsTrue(AIController.TryFindGoalOnLine(sim, Vec2.Zero, new Vec2(0f, 1f), out int seat, out float along));
            Assert.AreEqual(2, seat);
            Assert.AreEqual(0f, along, 1e-4f);

            // West is closed, so a shot that way hits a wall.
            Assert.IsFalse(AIController.TryFindGoalOnLine(sim, Vec2.Zero, new Vec2(-1f, 0f), out _, out _));
            // Steep shot into the corner misses the goal opening.
            Assert.IsFalse(AIController.TryFindGoalOnLine(sim, Vec2.Zero, new Vec2(1f, 1f).Normalized, out _, out _));
        }

        [Test] // US-4.2: the AI's paddle obeys the same limits as a human's
        public void AIInputIsAlwaysWithinHumanLimits()
        {
            var sim = Create(balls: 4, configure: s => s.Seed = 3);
            var ais = new[]
            {
                new AIController(0, AIDifficulty.Expert, 1), new AIController(1, AIDifficulty.Hard, 2),
                new AIController(2, AIDifficulty.Medium, 3), new AIController(3, AIDifficulty.Easy, 4),
            };
            var inputs = new PlayerInput[4];
            for (int t = 0; t < 3600 && !sim.IsEnded; t++)
            {
                for (int i = 0; i < 4; i++) inputs[i] = ais[i].Think(sim);
                sim.Step(inputs);
                for (int i = 0; i < 4; i++)
                    Assert.LessOrEqual(System.Math.Abs(sim.State.Seats[i].PaddleVelocity), sim.Config.PaddleMaxSpeed + 1e-4f);
            }
        }
    }
}
