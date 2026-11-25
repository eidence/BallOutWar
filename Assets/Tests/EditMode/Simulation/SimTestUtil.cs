using System;
using System.Collections.Generic;

namespace BOW.Simulation.Tests
{
    /// <summary>Helpers for building and driving simulations in tests.</summary>
    internal static class SimTestUtil
    {
        public static MatchSimulation Create(
            GameMode mode = GameMode.Timed,
            int balls = 1,
            bool[] seats = null,
            Action<MatchSettings> configure = null,
            GameplayConfig config = null,
            ArenaDefinition arena = null)
        {
            var settings = new MatchSettings
            {
                Mode = mode,
                BallCount = balls,
                SeatActive = seats ?? (mode == GameMode.Practice
                    ? new[] { true, false, false, false }
                    : new[] { true, true, true, true }),
            };
            configure?.Invoke(settings);
            return new MatchSimulation(settings, config, arena);
        }

        /// <summary>Skips the countdown and clears all balls, so a test controls every ball itself.</summary>
        public static void StartPlayingWithoutBalls(MatchSimulation sim)
        {
            sim.State.Phase = MatchPhase.Playing;
            sim.State.CountdownRemaining = 0f;
            sim.State.BallsLaunched = true;
            for (int i = 0; i < MatchState.MaxBalls; i++)
                sim.State.Balls[i] = new BallState { LastTouchSeat = -1 };
        }

        public static void PlaceBall(MatchSimulation sim, int index, Vec2 position, Vec2 velocity)
        {
            sim.State.Balls[index] = new BallState
            {
                Active = true,
                Position = position,
                Velocity = velocity,
                LastTouchSeat = -1,
            };
        }

        public static PlayerInput[] Inputs(params PlayerInput[] perSeat)
        {
            var inputs = new PlayerInput[MatchState.MaxSeats];
            for (int i = 0; i < perSeat.Length && i < inputs.Length; i++) inputs[i] = perSeat[i];
            return inputs;
        }

        /// <summary>Steps the simulation and returns every event raised along the way.</summary>
        public static List<SimEvent> Run(MatchSimulation sim, int ticks, PlayerInput[] inputs = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                sim.Step(inputs);
                events.AddRange(sim.Events);
            }
            return events;
        }

        public static List<SimEvent> RunSeconds(MatchSimulation sim, float seconds, PlayerInput[] inputs = null)
            => Run(sim, (int)Math.Round(seconds * sim.Config.TickRate), inputs);

        public static int Count(List<SimEvent> events, SimEventType type, int seat = int.MinValue)
        {
            int n = 0;
            foreach (var e in events)
                if (e.Type == type && (seat == int.MinValue || e.Seat == seat)) n++;
            return n;
        }

        /// <summary>Deterministic pseudo-random inputs for all four seats.</summary>
        public static PlayerInput[] RandomInputs(ref SimRandom rng, PlayerInput[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = rng.NextFloat() < 0.3f
                    ? PlayerInput.ToTarget(rng.Range(-4f, 4f), rng.NextFloat() < 0.05f)
                    : PlayerInput.Analog(rng.Range(-1f, 1f), rng.NextFloat() < 0.05f);
            }
            return buffer;
        }
    }
}
