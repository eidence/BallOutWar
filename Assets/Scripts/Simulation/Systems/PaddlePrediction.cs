namespace BOW.Simulation
{
    /// <summary>
    /// Lets a network guest move its own paddle ahead of the host (TDD 8.7) using exactly the host's paddle rules.
    /// </summary>
    public static class PaddlePrediction
    {
        /// <summary>Advances one seat's paddle by one tick, as <see cref="MatchSimulation.Step"/> would.</summary>
        public static void Advance(ref SeatState seat, PlayerInput input, GameplayConfig config, ArenaDefinition arena, float dt)
        {
            if (!seat.IsActive) return;
            PaddleSystem.Move(ref seat, input, config, PaddleSystem.Limit(config, arena), dt);
        }
    }
}
