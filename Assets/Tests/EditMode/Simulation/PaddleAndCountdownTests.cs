using System;
using NUnit.Framework;
using static BOW.Simulation.Tests.SimTestUtil;

namespace BOW.Simulation.Tests
{
    public class CountdownTests
    {
        [Test] // US-2.6
        public void BallsLaunchOnlyWhenCountdownEnds()
        {
            var sim = Create(balls: 2);
            int countdownTicks = (int)Math.Round(sim.Config.CountdownSeconds * sim.Config.TickRate);

            Run(sim, countdownTicks - 1);
            Assert.AreEqual(MatchPhase.Countdown, sim.State.Phase);
            Assert.IsFalse(sim.State.Balls[0].Active);

            var events = Run(sim, 1);
            Assert.AreEqual(MatchPhase.Playing, sim.State.Phase);
            Assert.IsTrue(sim.State.Balls[0].Active, "first ball launches on Go");
            Assert.AreEqual(1, Count(events, SimEventType.BallSpawned));

            RunSeconds(sim, sim.Config.LaunchStagger + 0.05f);
            Assert.IsTrue(sim.State.Balls[1].Active, "second ball follows after the stagger");
            Assert.IsFalse(sim.State.Balls[2].Active, "only the configured ball count launches");
        }

        [Test] // US-2.6
        public void BlastIsDeniedButMovementWorksDuringCountdown()
        {
            var sim = Create();
            var events = Run(sim, 30, Inputs(PlayerInput.Analog(1f, blast: true)));

            Assert.AreEqual(0, sim.State.Seats[0].Blasts);
            Assert.AreEqual(30, Count(events, SimEventType.BlastDenied, 0));
            Assert.Greater(sim.State.Seats[0].PaddlePosition, 0f);
        }
    }

    public class PaddleTests
    {
        [Test] // US-2.2: smooth acceleration up to max speed
        public void AnalogInputAcceleratesSmoothlyToMaxSpeed()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            var hold = Inputs(PlayerInput.Analog(1f));

            Run(sim, 1, hold);
            float first = sim.State.Seats[0].PaddleVelocity;
            Assert.Greater(first, 0f);
            Assert.Less(first, sim.Config.PaddleMaxSpeed, "no instant jump to full speed");

            sim.State.Seats[0].PaddlePosition = -PaddleSystem.Limit(sim.Config, sim.Arena);
            Run(sim, 15, hold);
            Assert.AreEqual(sim.Config.PaddleMaxSpeed, sim.State.Seats[0].PaddleVelocity, 1e-4f);
        }

        [Test] // US-2.2: cannot pass the corner posts
        public void PaddleIsClampedBetweenPosts()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            float limit = PaddleSystem.Limit(sim.Config, sim.Arena);

            Run(sim, 180, Inputs(PlayerInput.Analog(1f)));
            Assert.AreEqual(limit, sim.State.Seats[0].PaddlePosition, 1e-5f);

            Run(sim, 180, Inputs(PlayerInput.Analog(-1f)));
            Assert.AreEqual(-limit, sim.State.Seats[0].PaddlePosition, 1e-5f);
        }

        [Test] // US-2.2: drag-to-position control, never faster than analog
        public void DragTargetIsReachedWithoutExceedingMaxSpeed()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            var input = Inputs(PlayerInput.ToTarget(2f));

            for (int i = 0; i < 120; i++)
            {
                sim.Step(input);
                Assert.LessOrEqual(Math.Abs(sim.State.Seats[0].PaddleVelocity), sim.Config.PaddleMaxSpeed + 1e-4f);
            }
            Assert.AreEqual(2f, sim.State.Seats[0].PaddlePosition, 0.02f);
        }

        [Test] // NFR-13: bad input can't break the paddle
        public void OutOfRangeAndNaNInputAreSanitised()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);

            Run(sim, 60, Inputs(PlayerInput.Analog(50f)));
            Assert.LessOrEqual(sim.State.Seats[0].PaddleVelocity, sim.Config.PaddleMaxSpeed + 1e-4f);

            Run(sim, 10, Inputs(PlayerInput.Analog(float.NaN), PlayerInput.ToTarget(float.PositiveInfinity)));
            Assert.IsFalse(float.IsNaN(sim.State.Seats[0].PaddlePosition));
            Assert.IsFalse(float.IsNaN(sim.State.Seats[1].PaddlePosition));
        }

        [Test] // Seat frames: every seat's "right" is the player's right with their goal at the bottom
        public void SeatFramesAreRotationsOfSouth()
        {
            for (int seat = 0; seat < 4; seat++)
            {
                Vec2 inward = SeatFrame.Inward(seat);
                Vec2 right = SeatFrame.Right(seat);
                Assert.AreEqual(0f, Vec2.Dot(inward, right), 1e-6f);
                // Right is inward rotated -90 degrees, as for South (inward +Y, right +X).
                Vec2 expected = inward.Rotate(-90f * SimMath.Deg2Rad);
                Assert.AreEqual(expected.X, right.X, 1e-5f);
                Assert.AreEqual(expected.Y, right.Y, 1e-5f);
            }
        }
    }

    public class BlastTests
    {
        [Test] // US-2.3
        public void BlastFlingsNearbyBallAwayFast()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(0f, -7.5f), new Vec2(0f, -3f));

            var events = Run(sim, 1, Inputs(PlayerInput.Analog(0f, blast: true)));

            Assert.AreEqual(1, Count(events, SimEventType.Blast, 0));
            Vec2 v = sim.State.Balls[0].Velocity;
            Assert.Greater(v.Y, 0f, "pushed away from the paddle, into the arena");
            Assert.GreaterOrEqual(v.Length, sim.Config.MaxSpeed, "faster than normal play");
            Assert.AreEqual(0, sim.State.Balls[0].LastTouchSeat);
        }

        [Test]
        public void BlastDoesNotReachDistantBalls()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            PlaceBall(sim, 0, new Vec2(0f, 0f), new Vec2(5f, 0f));

            Run(sim, 1, Inputs(PlayerInput.Analog(0f, blast: true)));

            Assert.AreEqual(1, sim.State.Seats[0].Blasts);
            Assert.Greater(sim.State.Balls[0].Velocity.X, 0f);
            Assert.AreEqual(0f, sim.State.Balls[0].Velocity.Y, 1e-4f);
        }

        [Test] // US-2.3: cooldown
        public void CooldownBlocksRepeatBlastUntilItExpires()
        {
            var sim = Create();
            StartPlayingWithoutBalls(sim);
            var blast = Inputs(PlayerInput.Analog(0f, blast: true));
            var idle = Inputs();

            Run(sim, 1, blast);
            Run(sim, 10, idle);
            var denied = Run(sim, 1, blast);
            Assert.AreEqual(1, sim.State.Seats[0].Blasts);
            Assert.AreEqual(1, Count(denied, SimEventType.BlastDenied, 0));

            RunSeconds(sim, sim.Config.BlastCooldown, idle);
            Run(sim, 1, blast);
            Assert.AreEqual(2, sim.State.Seats[0].Blasts);
        }
    }
}
