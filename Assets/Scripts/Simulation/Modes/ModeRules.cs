using System;

namespace BOW.Simulation
{
    /// <summary>
    /// Scoring and end conditions for a game mode (TDD 5.3-5.6). Rules are stateless:
    /// everything they need lives in <see cref="MatchState"/>, so snapshots and resume just work.
    /// </summary>
    internal abstract class ModeRules
    {
        public static ModeRules For(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Timed: return new TimedRules();
                case GameMode.Elimination: return new EliminationRules();
                case GameMode.Practice: return new PracticeRules();
                default: throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        public virtual void Initialize(MatchSimulation sim) { }

        /// <summary>Called after a ball fully crosses <paramref name="seat"/>'s goal line.</summary>
        public abstract void OnGoal(MatchSimulation sim, int seat);

        /// <summary>Called once per Playing tick, after balls have moved.</summary>
        public virtual void Update(MatchSimulation sim, float dt) { }

        /// <summary>Negative when seat <paramref name="a"/> finished ahead of <paramref name="b"/>; 0 for a shared place.</summary>
        public abstract int Compare(MatchState state, int a, int b);

        protected static void EndIfOneLeft(MatchSimulation sim)
        {
            var seats = sim.State.Seats;
            int remaining = -1, count = 0;
            for (int i = 0; i < MatchState.MaxSeats; i++)
            {
                if (!seats[i].IsActive) continue;
                remaining = i;
                count++;
            }
            if (count == 1) sim.EndMatch(remaining, false);
            else if (count == 0) sim.EndMatch(-1, true);
        }

        /// <summary>0 = never knocked out (best), otherwise later knock-outs rank higher.</summary>
        protected static int KnockoutRank(in SeatState s) => s.EliminationOrder == 0 ? int.MaxValue : s.EliminationOrder;
    }

    /// <summary>Fewest goals against when the clock runs out wins (US-3.1), with sudden death or a draw on a tie (US-3.2).</summary>
    internal sealed class TimedRules : ModeRules
    {
        public override void OnGoal(MatchSimulation sim, int seat)
        {
            ref SeatState s = ref sim.State.Seats[seat];
            s.GoalsAgainst++;
            if (!sim.State.SuddenDeath) return;

            // Sudden death: first tied player to concede is out.
            sim.Eliminate(seat);
            EndIfOneLeft(sim);
        }

        public override void Update(MatchSimulation sim, float dt)
        {
            var state = sim.State;
            if (state.SuddenDeath) return;

            state.ClockRemaining -= dt;
            if (!state.FinalCountdownRaised && state.ClockRemaining <= sim.Config.FinalCountdownSeconds)
            {
                state.FinalCountdownRaised = true;
                sim.Emit(SimEventType.FinalCountdown, -1, -1, Vec2.Zero);
            }

            if (state.ClockRemaining > 1e-4f) return;
            state.ClockRemaining = 0f;
            ResolveTimeUp(sim);
        }

        private static void ResolveTimeUp(MatchSimulation sim)
        {
            var seats = sim.State.Seats;
            int best = int.MaxValue;
            for (int i = 0; i < MatchState.MaxSeats; i++)
                if (seats[i].IsActive && seats[i].GoalsAgainst < best) best = seats[i].GoalsAgainst;

            int tied = 0, leader = -1;
            for (int i = 0; i < MatchState.MaxSeats; i++)
            {
                if (!seats[i].IsActive || seats[i].GoalsAgainst != best) continue;
                tied++;
                leader = i;
            }

            if (tied == 1)
            {
                sim.EndMatch(leader, false);
                return;
            }

            if (sim.Settings.TieRule == TieRule.AllowDraw)
            {
                sim.EndMatch(-1, true);
                return;
            }

            // Sudden death: only the tied players' goals stay open.
            sim.State.SuddenDeath = true;
            for (int i = 0; i < MatchState.MaxSeats; i++)
                if (seats[i].IsActive && seats[i].GoalsAgainst != best)
                    seats[i].Status = SeatStatus.Spectating;
            sim.Emit(SimEventType.SuddenDeathStarted, -1, -1, Vec2.Zero);
        }

        public override int Compare(MatchState state, int a, int b)
        {
            var sa = state.Seats[a];
            var sb = state.Seats[b];
            // Anyone who made sudden death (tied for first at time-up) beats everyone who only watched it.
            int c = (sa.Status == SeatStatus.Spectating).CompareTo(sb.Status == SeatStatus.Spectating);
            if (c != 0) return c;
            c = sa.GoalsAgainst.CompareTo(sb.GoalsAgainst);
            if (c != 0) return c;
            return KnockoutRank(sb).CompareTo(KnockoutRank(sa));
        }
    }

    /// <summary>Each player is knocked out after conceding N goals; last one standing wins (US-3.3).</summary>
    internal sealed class EliminationRules : ModeRules
    {
        public override void Initialize(MatchSimulation sim)
        {
            var seats = sim.State.Seats;
            for (int i = 0; i < MatchState.MaxSeats; i++)
                seats[i].Lives = seats[i].IsActive ? sim.Settings.GoalsToEliminate : 0;
        }

        public override void OnGoal(MatchSimulation sim, int seat)
        {
            ref SeatState s = ref sim.State.Seats[seat];
            s.GoalsAgainst++;
            s.Lives = Math.Max(0, s.Lives - 1);
            if (s.Lives > 0) return;

            sim.Eliminate(seat);
            EndIfOneLeft(sim);
        }

        public override int Compare(MatchState state, int a, int b)
        {
            var sa = state.Seats[a];
            var sb = state.Seats[b];
            int c = KnockoutRank(sb).CompareTo(KnockoutRank(sa));
            if (c != 0) return c;
            return sa.GoalsAgainst.CompareTo(sb.GoalsAgainst);
        }
    }

    /// <summary>No clock and no winner; goals are counted only for the player's own feedback (US-3.5).</summary>
    internal sealed class PracticeRules : ModeRules
    {
        public override void OnGoal(MatchSimulation sim, int seat) => sim.State.Seats[seat].GoalsAgainst++;

        public override int Compare(MatchState state, int a, int b) => 0;
    }
}
