using System;
using BOW.AI;
using BOW.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SimInput = BOW.Simulation.PlayerInput;

namespace BOW.Game
{
    /// <summary>
    /// Match driver for the Match scene (prototype). Builds the arena, cars, drivers and HUD from the simulation,
    /// steps the simulation at its fixed tick rate, and feeds it the local player's input and the AI opponents'.
    /// The local player sits at <see cref="localSeat"/>; every other active seat is an AI.
    /// The menus and lobby will set these fields later; for now edit them on the Match object in the Inspector.
    /// </summary>
    public sealed class MatchBootstrap : MonoBehaviour
    {
        [Header("Match")]
        [SerializeField] private GameMode mode = GameMode.Timed;
        [SerializeField, Range(1, 4)] private int ballCount = 2;
        [SerializeField, Min(10f)] private float durationSeconds = 120f;
        [SerializeField, Range(3, 20)] private int goalsToEliminate = 5;
        [SerializeField] private TieRule tieRule = TieRule.SuddenDeath;

        [Header("Seats (South, East, North, West)")]
        [SerializeField] private bool[] seatActive = { true, true, true, true };
        [SerializeField, Range(0, 3)] private int localSeat = 0;
        [Tooltip("Character for each seat: 0 Bolt, 1 Mitzi, 2 Hopper, 3 Pip")]
        [SerializeField] private int[] seatCharacter = { 0, 1, 2, 3 };
        [SerializeField] private AIDifficulty[] seatDifficulty = { AIDifficulty.Medium, AIDifficulty.Medium, AIDifficulty.Medium, AIDifficulty.Medium };

        [Header("Camera")]
        [SerializeField, Range(30f, 80f)] private float cameraPitch = 55f;
        [SerializeField, Range(20f, 60f)] private float cameraFieldOfView = 35f;

        private MatchSimulation _sim;
        private MatchState _previous;
        private readonly SimInput[] _inputs = new SimInput[SeatFrame.SeatCount];
        private readonly AIController[] _ai = new AIController[SeatFrame.SeatCount];
        private readonly LocalPlayerInput _local = new LocalPlayerInput();
        private int[] _characters;
        private PortraitStudio _studio;
        private ArenaView _arena;
        private MatchActorsView _actors;
        private MatchHud _hud;
        private Camera _camera;
        private Transform _worldRoot;
        private float _accumulator;
        private int _screenWidth, _screenHeight;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            _camera = Camera.main;
            if (_camera == null) _camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            SetUpEnvironment();

            _characters = new int[SeatFrame.SeatCount];
            for (int i = 0; i < _characters.Length; i++)
                _characters[i] = seatCharacter != null && i < seatCharacter.Length ? Mathf.Clamp(seatCharacter[i], 0, Characters.Count - 1) : i;

            _studio = new PortraitStudio(transform);
            StartCoroutine(_studio.RenderOnce());
            StartMatch();
        }

        private void SetUpEnvironment()
        {
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            _camera.farClipPlane = 300f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.36f, 0.45f);
            RenderSettings.fog = false;

            // Bloom makes the neon glow. Use an instance of the scene profile so the asset is never modified.
            var volume = FindFirstObjectByType<Volume>();
            if (volume != null && volume.profile.TryGet(out Bloom bloom))
            {
                bloom.active = true;
                bloom.threshold.Override(1.0f);
                bloom.intensity.Override(1.1f);
                bloom.scatter.Override(0.65f);
            }
        }

        private void StartMatch()
        {
            if (_worldRoot != null) Destroy(_worldRoot.gameObject);
            _hud?.Destroy();

            var settings = new MatchSettings
            {
                Mode = mode,
                BallCount = ballCount,
                DurationSeconds = durationSeconds,
                GoalsToEliminate = goalsToEliminate,
                TieRule = tieRule,
                Seed = (uint)Environment.TickCount,
                SeatActive = BuildSeatList(),
            };

            _sim = new MatchSimulation(settings);
            _previous = _sim.State.Clone();
            _accumulator = 0f;

            for (int i = 0; i < SeatFrame.SeatCount; i++)
            {
                var level = seatDifficulty != null && i < seatDifficulty.Length ? seatDifficulty[i] : AIDifficulty.Medium;
                _ai[i] = i != localSeat && settings.SeatActive[i] ? new AIController(i, level, settings.Seed * 7919u + (uint)i) : null;
            }

            _worldRoot = new GameObject("Match World").transform;
            _arena = new ArenaView(_sim.Arena, _worldRoot, localSeat);
            _actors = new MatchActorsView(_sim, _worldRoot, _characters);

            _hud = new MatchHud(_sim, localSeat, _characters, _studio.Portraits);
            _hud.PauseClicked += TogglePause;
            _hud.RestartClicked += StartMatch;
            _hud.BlastPressed += _local.PressBlast;

            PlaceCamera();
        }

        private bool[] BuildSeatList()
        {
            var seats = new bool[SeatFrame.SeatCount];
            for (int i = 0; i < seats.Length; i++) seats[i] = seatActive != null && i < seatActive.Length && seatActive[i];
            seats[localSeat] = true;
            if (mode == GameMode.Practice)
                for (int i = 0; i < seats.Length; i++) seats[i] = i == localSeat;
            return seats;
        }

        private void Update()
        {
            if (Screen.width != _screenWidth || Screen.height != _screenHeight) PlaceCamera();
            if (LocalPlayerInput.PausePressed()) TogglePause();

            _local.Sample(_camera, localSeat, _sim.Arena.HalfSize);

            float dt = _sim.DeltaTime;
            _accumulator = Mathf.Min(_accumulator + Time.deltaTime, 0.25f);
            while (_accumulator >= dt)
            {
                _accumulator -= dt;
                _previous.CopyFrom(_sim.State);

                for (int i = 0; i < SeatFrame.SeatCount; i++)
                {
                    if (i == localSeat) _inputs[i] = _local.Consume();
                    else _inputs[i] = _ai[i] != null ? _ai[i].Think(_sim) : default;
                }

                _sim.Step(_inputs);
                HandleEvents();
            }

            bool moving = _sim.State.Phase == MatchPhase.Playing || _sim.State.Phase == MatchPhase.Countdown;
            float alpha = moving ? _accumulator / dt : 1f;
            _arena.Sync(_sim.State, Time.deltaTime);
            _actors.Sync(_previous, _sim.State, alpha, Time.deltaTime);
            _hud.Update(_sim.State, Time.deltaTime);
        }

        private void HandleEvents()
        {
            foreach (var e in _sim.Events)
            {
                switch (e.Type)
                {
                    case SimEventType.Blast:
                        _actors.PlayBlast(e.Seat, e.Position);
                        break;
                    case SimEventType.Goal:
                        _arena.FlashGoal(e.Seat);
                        _hud.OnGoal(e.Seat);
                        _hud.ShowBanner(e.Seat == localSeat ? "OUCH!" : $"{_hud.Label(e.Seat).ToUpperInvariant()} CONCEDES!",
                            SimSpace.SeatColors[e.Seat]);
                        break;
                    case SimEventType.Eliminated:
                        _hud.ShowBanner(e.Seat == localSeat ? "YOU'RE OUT!" : $"{_hud.Label(e.Seat).ToUpperInvariant()} IS OUT!", Color.white, 2f);
                        break;
                    case SimEventType.SuddenDeathStarted:
                        _hud.ShowBanner("SUDDEN DEATH!", new Color(1f, 0.35f, 0.3f), 2f);
                        break;
                    case SimEventType.FinalCountdown:
                        _hud.ShowBanner("10 SECONDS!", new Color(1f, 0.82f, 0.25f));
                        break;
                }
            }
        }

        private void TogglePause()
        {
            if (_sim.State.Phase == MatchPhase.Paused) _sim.Resume();
            else _sim.Pause();
        }

        private void OnApplicationPause(bool paused)
        {
            // US-6.1: pause automatically when the app goes to the background.
            if (paused && _sim != null) _sim.Pause();
        }

        private void PlaceCamera()
        {
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            if (_sim != null) MatchCameraRig.Place(_camera, localSeat, _sim.Arena.HalfSize, cameraPitch, cameraFieldOfView);
        }
    }
}
