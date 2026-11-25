using System;
using NUnit.Framework;
using static BOW.Simulation.Tests.SimTestUtil;

namespace BOW.Simulation.Tests
{
    public class BallTests
    {
        [Test] // US-2.4
        public void BallBouncesOffSideWall()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(8.5f, 8f), new Vec2(8f, 0f)); // heading for the East wall beside the goal

            Run(sim, 30);

            Assert.IsTrue(sim.State.Balls[0].Active);
            Assert.Less(sim.State.Balls[0].Velocity.X, 0f);
            Assert.Less(sim.State.Balls[0].Position.X, sim.Arena.HalfSize);
        }

        [Test] // US-2.4: no tunnelling, even at the hard speed cap
        public void FastBallCannotTunnelThroughClosedGoal()
        {
            var sim = Create(seats: new[] { true, false, true, true }); // East closed = wall
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(7f, 0f), new Vec2(60f, 0f));

            var events = Run(sim, 30);

            Assert.AreEqual(0, Count(events, SimEventType.Goal));
            Assert.IsTrue(sim.State.Balls[0].Active);
            Assert.LessOrEqual(sim.State.Balls[0].Position.X, sim.Arena.HalfSize - sim.Config.BallRadius + 1e-3f);
        }

        [Test] // US-2.5
        public void GoalCountsAgainstSeatOwnerAndBallRespawns()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(4f, -5f), new Vec2(0f, -8f)); // past the paddle's reach at the South goal
            sim.State.Balls[0].LastTouchSeat = 2;                        // last touched by North: irrelevant to scoring

            var events = RunSeconds(sim, 1f);

            Assert.AreEqual(1, Count(events, SimEventType.Goal, 0));
            Assert.AreEqual(1, sim.State.Seats[0].GoalsAgainst);
            Assert.AreEqual(0, sim.State.Seats[2].GoalsAgainst);

            RunSeconds(sim, sim.Config.RespawnDelay + 0.1f);
            Assert.IsTrue(sim.State.Balls[0].Active, "replacement ball launched after the respawn delay");
        }

        [Test] // US-2.5: a ball touching the line is not yet a goal
        public void BallOnTheLineIsNotAGoal()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            float r = sim.Config.BallRadius;
            // Half the ball is over the line, but it is heading back into the arena.
            PlaceBall(sim, 0, new Vec2(4f, -sim.Arena.HalfSize - r * 0.5f), new Vec2(0f, 3f));

            var events = Run(sim, 30);

            Assert.AreEqual(0, Count(events, SimEventType.Goal));
            Assert.AreEqual(0, sim.State.Seats[0].GoalsAgainst);
            Assert.IsTrue(sim.State.Balls[0].Active);
        }

        [Test] // US-2.4: block, deflect, count the save
        public void PaddleBlocksBallAndCountsSave()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(0f, -5f), new Vec2(0f, -8f));

            var events = RunSeconds(sim, 1f);

            Assert.AreEqual(0, Count(events, SimEventType.Goal));
            Assert.Greater(sim.State.Balls[0].Velocity.Y, 0f);
            Assert.AreEqual(1, sim.State.Seats[0].Saves);
            Assert.AreEqual(0, sim.State.Balls[0].LastTouchSeat);
            Assert.AreEqual(0f, sim.State.Balls[0].Velocity.X, 0.2f, "centre hit returns straight");
        }

        [Test] // US-2.4: edge hits deflect at sharper angles
        public void EdgeHitDeflectsTowardThatEdge()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(1.5f, -5f), new Vec2(0f, -8f));

            RunSeconds(sim, 0.6f);

            Vec2 v = sim.State.Balls[0].Velocity;
            Assert.Greater(v.Y, 0f);
            Assert.Greater(v.X, 0f, "right-edge hit goes right");
            Assert.Greater(Math.Abs(v.X), v.Y * 0.5f, "noticeably angled");
        }

        [Test] // US-2.4
        public void BallsCollideElastically()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(-1f, 0f), new Vec2(5f, 0f));
            PlaceBall(sim, 1, new Vec2(1f, 0f), new Vec2(-5f, 0f));

            var events = Run(sim, 20);

            Assert.GreaterOrEqual(Count(events, SimEventType.BallCollision), 1);
            Assert.Less(sim.State.Balls[0].Velocity.X, 0f);
            Assert.Greater(sim.State.Balls[1].Velocity.X, 0f);
        }

        [Test] // US-2.4: speed stays between min and max; blasts decay back to max
        public void SpeedIsKeptWithinLimits()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(0f, 0f), new Vec2(0.1f, 0f));
            PlaceBall(sim, 1, new Vec2(0f, 8f), new Vec2(sim.Config.BlastSpeed, 0f));

            Run(sim, 1);
            Assert.GreaterOrEqual(sim.State.Balls[0].Velocity.Length, sim.Config.MinSpeed - 1e-4f);

            RunSeconds(sim, 4f);
            Assert.LessOrEqual(sim.State.Balls[1].Velocity.Length, sim.Config.MaxSpeed * 1.05f);
        }

        [Test] // US-2.4: a slow ball is nudged after 5 seconds
        public void SlowBallIsNudgedTowardAGoal()
        {
            var config = GameplayConfig.CreateDefault();
            config.Friction = 0f;
            var sim = Create(config: config);
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(0f, 8f), new Vec2(config.MinSpeed, 0f));

            var early = RunSeconds(sim, config.StallSeconds - 0.5f);
            Assert.AreEqual(0, Count(early, SimEventType.StallNudge));

            var late = RunSeconds(sim, 1f);
            Assert.AreEqual(1, Count(late, SimEventType.StallNudge));
        }
    }
}
