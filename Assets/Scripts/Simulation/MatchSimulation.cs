using System;
using System.Collections.Generic;

namespace BOW.Simulation
{
    /// <summary>
    /// The match simulation (TDD 4, 5.1). Pure C#, stepped at a fixed tick rate by whoever owns the match:
    /// the offline device or the multiplayer host. Presentation reads <see cref="State"/> and
    /// <see cref="Events"/> but never changes them.
    /// </summary>
    public sealed class MatchSimulation
    {
        private readonly List<SimEvent> _events = new List<SimEvent>(64);
        private readonly ModeRules _rules;
        private readonly int[] _placementOrder = new int[MatchState.MaxSeats];

        public GameplayConfig Config { get; }
        public ArenaDefinition Arena { get; }
        public MatchSettings Settings { get; }
        public MatchState State { get; }

        /// <summary>Events raised by the most recent <see cref="Step"/>, pause or resume call.</summary>
        public IReadOnlyList<SimEvent> Events => _events;

        public float DeltaTime => Config.FixedDeltaTime;
        public bool IsEnded => State.Phase == MatchPhase.Ended;

        public MatchSimulation(MatchSettings settings, GameplayConfig config = null, ArenaDefinition arena = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string error = settings.Validate();
            if (error != null) throw new ArgumentException(error, nameof(settings));

            Arena = arena ?? ArenaDefinition.CreateDefault();
            var arenaErrors = Arena.Validate();
            if (arenaErrors.Count > 0) throw new ArgumentException(string.Join(" ", arenaErrors), nameof(arena));

            Settings = settings.Clone();
            Config = (config ?? GameplayConfig.CreateDefault()).Clone();
            _rules = ModeRules.For(Settings.Mode);
            State = new MatchState();
            Reset();
        }

        /// <summary>Puts the match back to the start of its opening countdown (offline restart, rematch).</summary>
        public void Reset()
        {
            _events.Clear();
            var s = State;
            s.Tick = 0;
            s.Phase = MatchPhase.Countdown;
            s.CountdownRemaining = Config.CountdownSeconds;
            s.ClockRemaining = Settings.Mode == GameMode.Timed ? Settings.DurationSeconds : 0f;
            s.Elapsed = 0f;
            s.BallsLaunched = false;
            s.SuddenDeath = false;
            s.FinalCountdownRaised = false;
            s.EliminationCount = 0;
            s.WinnerSeat = -1;
            s.IsDraw = false;
            s.SpeedMultiplier = 1f;
            s.Random = new SimRandom(Settings.Seed);

            for (int i = 0; i < MatchState.MaxSeats; i++)
                s.Seats[i] = new SeatState { Status = Settings.SeatActive[i] ? SeatStatus.Active : SeatStatus.Closed };
            for (int i = 0; i < MatchState.MaxBalls; i++)
                s.Balls[i] = new BallState { LastTouchSeat = -1 };

            _rules.Initialize(this);
        }

        /// <summary>
        /// Advances one fixed tick. <paramref name="inputs"/> is indexed by seat (missing entries mean no input).
        /// Does nothing while paused or after the match has ended.
        /// </summary>
        public void Step(PlayerInput[] inputs)
        {
            _events.Clear();
            var s = State;
            if (s.Phase == MatchPhase.Paused || s.Phase == MatchPhase.Ended) return;

            s.Tick++;
            float dt = DeltaTime;

            switch (s.Phase)
            {
                case MatchPhase.Countdown:
                    PaddleSystem.Update(this, inputs, dt, blastAllowed: false);
                    s.CountdownRemaining -= dt;
                    if (s.CountdownRemaining <= 1e-4f)
                    {
                        s.CountdownRemaining = 0f;
                        SetPhase(MatchPhase.Playing);
                        if (!s.BallsLaunched) LauncherSystem.QueueInitialLaunch(this);
                    }
                    break;

                case MatchPhase.Playing:
                    PaddleSystem.Update(this, inputs, dt, blastAllowed: true);
                    LauncherSystem.Update(this, dt);
                    BallSystem.Update(this, dt);
                    if (s.Phase != MatchPhase.Playing) break;
                    s.Elapsed += dt;
                    UpdateCleanTime(dt);
                    _rules.Update(this, dt);
                    break;
            }
        }

        /// <summary>Freezes everything, including the clock (US-6.1, US-6.2). Returns false if not pausable.</summary>
        public bool Pause()
        {
            _events.Clear();
            if (State.Phase != MatchPhase.Playing && State.Phase != MatchPhase.Countdown) return false;
            SetPhase(MatchPhase.Paused);
            return true;
        }

        /// <summary>Resumes through a fresh 3-second countdown (US-6.1). Returns false if not paused.</summary>
        public bool Resume()
        {
            _events.Clear();
            if (State.Phase != MatchPhase.Paused) return false;
            State.CountdownRemaining = Config.CountdownSeconds;
            SetPhase(MatchPhase.Countdown);
            return true;
        }

        /// <summary>Replaces the state wholesale (offline resume, applying a host snapshot).</summary>
        public void LoadState(MatchState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            _events.Clear();
            State.CopyFrom(state);
        }

        /// <summary>Practice only: change ball count and speed mid-session (US-3.5).</summary>
        public void SetPracticeOptions(int ballCount, float speedMultiplier)
        {
            if (Settings.Mode != GameMode.Practice)
                throw new InvalidOperationException("Practice options only apply in Practice mode.");

            ballCount = Math.Max(MatchSettings.MinBalls, Math.Min(MatchSettings.MaxBalls, ballCount));
            State.SpeedMultiplier = SimMath.Clamp(speedMultiplier, 0.5f, 2f);
            Settings.BallCount = ballCount;

            for (int i = 0; i < MatchState.MaxBalls; i++)
            {
                ref BallState ball = ref State.Balls[i];
                if (i >= ballCount)
                {
                    ball = new BallState { LastTouchSeat = -1 };
                }
                else if (State.BallsLaunched && !ball.Active && !ball.PendingSpawn)
                {
                    ball.PendingSpawn = true;
                    ball.SpawnTimer = Config.RespawnDelay;
                }
            }
        }

        // ----- Called by systems and rules -----

        internal void Emit(SimEventType type, int seat, int ball, Vec2 position)
        {
            _events.Add(new SimEvent { Type = type, Tick = State.Tick, Seat = seat, Ball = ball, Position = position });
        }

        internal void OnGoal(int seat, int ball, Vec2 position)
        {
            if (State.Phase == MatchPhase.Ended) return;
            ref SeatState s = ref State.Seats[seat];
            s.TimeSinceConceded = 0f;
            Emit(SimEventType.Goal, seat, ball, position);
            _rules.OnGoal(this, seat);
        }

        internal void Eliminate(int seat)
        {
            ref SeatState s = ref State.Seats[seat];
            if (s.Status == SeatStatus.Eliminated) return;
            s.Status = SeatStatus.Eliminated;
            s.PaddleVelocity = 0f;
            State.EliminationCount++;
            s.EliminationOrder = State.EliminationCount;
            Emit(SimEventType.Eliminated, seat, -1, SeatFrame.GoalCenter(seat, Arena.HalfSize));
        }

        internal void EndMatch(int winner, bool draw)
        {
            if (State.Phase == MatchPhase.Ended) return;
            State.WinnerSeat = draw ? -1 : winner;
            State.IsDraw = draw;
            AssignPlacements();
            SetPhase(MatchPhase.Ended);
            Emit(SimEventType.MatchEnded, State.WinnerSeat, -1, Vec2.Zero);
        }

        private void AssignPlacements()
        {
            var seats = State.Seats;
            int n = 0;
            for (int i = 0; i < MatchState.MaxSeats; i++)
            {
                seats[i].Placement = 0;
                if (seats[i].Status != SeatStatus.Closed) _placementOrder[n++] = i;
            }

            // Insertion sort: at most 4 seats, and no allocation.
            for (int i = 1; i < n; i++)
            {
                int key = _placementOrder[i];
                int j = i - 1;
                while (j >= 0 && _rules.Compare(State, _placementOrder[j], key) > 0)
                {
                    _placementOrder[j + 1] = _placementOrder[j];
                    j--;
                }
                _placementOrder[j + 1] = key;
            }

            for (int i = 0; i < n; i++)
            {
                int seat = _placementOrder[i];
                bool sharesPlace = i > 0 && _rules.Compare(State, _placementOrder[i - 1], seat) == 0;
                seats[seat].Placement = sharesPlace ? seats[_placementOrder[i - 1]].Placement : i + 1;
            }
        }

        private void SetPhase(MatchPhase phase)
        {
            State.Phase = phase;
            Emit(SimEventType.PhaseChanged, -1, -1, Vec2.Zero);
        }

        private void UpdateCleanTime(float dt)
        {
            var seats = State.Seats;
            for (int i = 0; i < MatchState.MaxSeats; i++)
            {
                if (!seats[i].IsActive) continue;
                seats[i].TimeSinceConceded += dt;
                if (seats[i].TimeSinceConceded > seats[i].LongestCleanTime)
                    seats[i].LongestCleanTime = seats[i].TimeSinceConceded;
            }
        }
    }
}
