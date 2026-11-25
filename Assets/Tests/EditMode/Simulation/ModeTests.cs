using System;
using NUnit.Framework;
using static BOW.Simulation.Tests.SimTestUtil;

namespace BOW.Simulation.Tests
{
    public class TimedModeTests
    {
        private static MatchSimulation TimedSim(float seconds, bool[] seats, TieRule tie = TieRule.SuddenDeath)
        {
            var sim = Create(GameMode.Timed, seats: seats, configure: s =>
            {
                s.DurationSeconds = seconds;
                s.TieRule = tie;
            });
            StartPlayingWithoutBalls(sim);
            return sim;
        }

        [Test] // US-3.1
        public void FewestGoalsAgainstWinsAtTimeUp()
        {
            var sim = TimedSim(5f, new[] { true, false, true, false });
            sim.OnGoal(2, 0, Vec2.Zero);
            sim.OnGoal(2, 0, Vec2.Zero);
            sim.OnGoal(0, 0, Vec2.Zero);

            var events = RunSeconds(sim, 5.1f);

            Assert.AreEqual(MatchPhase.Ended, sim.State.Phase);
            Assert.AreEqual(0, sim.State.WinnerSeat);
            Assert.AreEqual(1, sim.State.Seats[0].Placement);
            Assert.AreEqual(2, sim.State.Seats[2].Placement);
            Assert.AreEqual(0, sim.State.Seats[1].Placement, "closed seats get no placement");
            Assert.AreEqual(1, Count(events, SimEventType.MatchEnded));
        }

        [Test] // US-3.1: final 10 seconds warning, raised once
        public void FinalCountdownIsRaisedOnce()
        {
            var sim = TimedSim(12f, new[] { true, true, false, false });
            sim.OnGoal(1, 0, Vec2.Zero);
            var events = RunSeconds(sim, 12.5f);
            Assert.AreEqual(1, Count(events, SimEventType.FinalCountdown));
        }

        [Test] // US-3.1, US-6.1: clock frozen while paused, resume goes through the countdown
        public void ClockStopsWhilePausedAndDuringResumeCountdown()
        {
            var sim = TimedSim(60f, new[] { true, true, false, false });
            RunSeconds(sim, 1f);
            float before = sim.State.ClockRemaining;

            Assert.IsTrue(sim.Pause());
            RunSeconds(sim, 5f);
            Assert.AreEqual(before, sim.State.ClockRemaining);

            Assert.IsTrue(sim.Resume());
            Assert.AreEqual(MatchPhase.Countdown, sim.State.Phase);
            RunSeconds(sim, sim.Config.CountdownSeconds - 0.1f);
            Assert.AreEqual(before, sim.State.ClockRemaining, "countdown does not use match time");

            RunSeconds(sim, 1f);
            Assert.AreEqual(MatchPhase.Playing, sim.State.Phase);
            Assert.Less(sim.State.ClockRemaining, before);
        }

        [Test] // US-3.2
        public void TieGoesToSuddenDeathBetweenTiedPlayersOnly()
        {
            var sim = TimedSim(2f, new[] { true, true, true, false });
            sim.OnGoal(0, 0, Vec2.Zero);
            sim.OnGoal(1, 0, Vec2.Zero);
            sim.OnGoal(2, 0, Vec2.Zero);
            sim.OnGoal(2, 0, Vec2.Zero);

            var events = RunSeconds(sim, 2.1f);

            Assert.AreEqual(1, Count(events, SimEventType.SuddenDeathStarted));
            Assert.IsTrue(sim.State.SuddenDeath);
            Assert.AreEqual(MatchPhase.Playing, sim.State.Phase);
            Assert.AreEqual(SeatStatus.Spectating, sim.State.Seats[2].Status);
            Assert.IsFalse(sim.State.Seats[2].GoalOpen, "non-tied goals become walls");

            sim.OnGoal(1, 0, Vec2.Zero);

            Assert.AreEqual(MatchPhase.Ended, sim.State.Phase);
            Assert.AreEqual(0, sim.State.WinnerSeat);
            Assert.AreEqual(1, sim.State.Seats[0].Placement);
            Assert.AreEqual(2, sim.State.Seats[1].Placement);
            Assert.AreEqual(3, sim.State.Seats[2].Placement);
        }

        [Test] // US-3.2: three-way sudden death repeats until one remains
        public void ThreeWaySuddenDeathRunsUntilOneLeft()
        {
            var sim = TimedSim(1f, new[] { true, true, true, false });
            RunSeconds(sim, 1.1f);
            Assert.IsTrue(sim.State.SuddenDeath);

            sim.OnGoal(2, 0, Vec2.Zero);
            Assert.AreEqual(MatchPhase.Playing, sim.State.Phase);
            sim.OnGoal(0, 0, Vec2.Zero);
            Assert.AreEqual(MatchPhase.Ended, sim.State.Phase);
            Assert.AreEqual(1, sim.State.WinnerSeat);
            Assert.AreEqual(2, sim.State.Seats[0].Placement, "knocked out second = second place");
            Assert.AreEqual(3, sim.State.Seats[2].Placement);
        }

        [Test] // US-3.2: host may allow a draw
        public void AllowDrawEndsTiedMatchAsDraw()
        {
            var sim = TimedSim(1f, new[] { true, true, true, true }, TieRule.AllowDraw);
            sim.OnGoal(3, 0, Vec2.Zero);
            RunSeconds(sim, 1.1f);

            Assert.AreEqual(MatchPhase.Ended, sim.State.Phase);
            Assert.IsTrue(sim.State.IsDraw);
            Assert.AreEqual(-1, sim.State.WinnerSeat);
            Assert.AreEqual(1, sim.State.Seats[0].Placement);
            Assert.AreEqual(1, sim.State.Seats[1].Placement);
            Assert.AreEqual(1, sim.State.Seats[2].Placement);
            Assert.AreEqual(4, sim.State.Seats[3].Placement);
        }
    }

    public class EliminationModeTests
    {
        [Test] // US-3.3
        public void PlayersAreEliminatedAndLastOneStandingWins()
        {
            var sim = Create(GameMode.Elimination, seats: new[] { true, true, true, false },
                configure: s => s.GoalsToEliminate = 3);
            StartPlayingWithoutBalls(sim);
            Assert.AreEqual(3, sim.State.Seats[1].Lives);

            for (int i = 0; i < 3; i++) sim.OnGoal(1, 0, Vec2.Zero);
            Assert.AreEqual(SeatStatus.Eliminated, sim.State.Seats[1].Status);
            Assert.AreEqual(0, sim.State.Seats[1].Lives);
            Assert.AreEqual(MatchPhase.Playing, sim.State.Phase);

            sim.OnGoal(0, 0, Vec2.Zero);
            for (int i = 0; i < 3; i++) sim.OnGoal(2, 0, Vec2.Zero);

            Assert.AreEqual(MatchPhase.Ended, sim.State.Phase);
            Assert.AreEqual(0, sim.State.WinnerSeat);
            Assert.AreEqual(1, sim.State.Seats[0].Placement);
            Assert.AreEqual(2, sim.State.Seats[2].Placement);
            Assert.AreEqual(3, sim.State.Seats[1].Placement);
        }

        [Test] // US-3.3: an eliminated player's goal is sealed by a wall
        public void EliminatedGoalBlocksBalls()
        {
            var sim = Create(GameMode.Elimination, configure: s => s.GoalsToEliminate = 3);
            StartPlayingWithoutBalls(sim);
            for (int i = 0; i < 3; i++) sim.OnGoal(1, 0, Vec2.Zero);

            PlaceBall(sim, 0, new Vec2(6f, 0f), new Vec2(8f, 0f)); // straight at the East goal
            var events = Run(sim, 40);

            Assert.AreEqual(0, Count(events, SimEventType.Goal));
            Assert.Less(sim.State.Balls[0].Velocity.X, 0f);
        }
    }

    public class PracticeAndSettingsTests
    {
        [Test] // US-3.5
        public void PracticeNeverEndsAndOptionsApply()
        {
            var sim = Create(GameMode.Practice, balls: 1);
            RunSeconds(sim, 30f);
            Assert.AreNotEqual(MatchPhase.Ended, sim.State.Phase);

            sim.SetPracticeOptions(3, 1.5f);
            RunSeconds(sim, 2f);
            Assert.AreEqual(1.5f, sim.State.SpeedMultiplier);
            int active = 0;
            for (int i = 0; i < MatchState.MaxBalls; i++) if (sim.State.Balls[i].Active || sim.State.Balls[i].PendingSpawn) active++;
            Assert.AreEqual(3, active);
        }

        [Test] // US-1.1, US-1.3, US-1.5
        public void InvalidSettingsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => Create(seats: new[] { true, false, false, false }));
            Assert.Throws<ArgumentException>(() => Create(balls: 5));
            Assert.Throws<ArgumentException>(() => Create(GameMode.Practice, seats: new[] { true, true, false, false }));
            Assert.Throws<ArgumentException>(() => Create(GameMode.Elimination, configure: s => s.GoalsToEliminate = 2));
            Assert.DoesNotThrow(() => Create(seats: new[] { false, true, false, true }));
        }

        [Test] // US-1.1: closed seats are walls with no paddle
        public void ClosedSeatHasNoPaddleAndAWall()
        {
            var sim = Create(seats: new[] { true, true, false, true });
            Assert.AreEqual(SeatStatus.Closed, sim.State.Seats[2].Status);
            Assert.IsFalse(sim.State.Seats[2].GoalOpen);
        }

        [Test] // TDD 10.4: arena symmetry check
        public void AsymmetricArenaIsRejected()
        {
            var arena = ArenaDefinition.CreateDefault();
            arena.Bumpers = new[] { new CircleObstacle(new Vec2(3f, 0f), 0.5f) };
            Assert.IsNotEmpty(arena.Validate());
            Assert.Throws<ArgumentException>(() => Create(arena: arena));

            arena.Bumpers = new[]
            {
                new CircleObstacle(new Vec2(3f, 0f), 0.5f),
                new CircleObstacle(new Vec2(0f, 3f), 0.5f),
                new CircleObstacle(new Vec2(-3f, 0f), 0.5f),
                new CircleObstacle(new Vec2(0f, -3f), 0.5f),
            };
            Assert.IsEmpty(arena.Validate());
        }
    }
}
