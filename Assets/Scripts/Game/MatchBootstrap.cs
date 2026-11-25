using System;
using System.Collections.Generic;
using BOW.AI;
using BOW.Net;
using BOW.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using SimInput = BOW.Simulation.PlayerInput;

namespace BOW.Game
{
    /// <summary>
    /// Match driver for the Match scene. Builds the arena, cars, drivers and HUD from the simulation and runs the
    /// match in one of three modes:
    /// <list type="bullet">
    /// <item>Offline: steps its own simulation, with AI in every other active seat.</item>
    /// <item>LAN host: steps <see cref="NetHost"/>, which also feeds guests and AI seats.</item>
    /// <item>LAN guest: shows <see cref="NetGuest"/>'s view of the host's match and sends local input.</item>
    /// </list>
    /// Playing the scene directly in the editor uses the Inspector values (offline).
    /// </summary>
    public sealed class MatchBootstrap : MonoBehaviour
    {
        private enum Mode { Offline, Host, Guest }

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

        private Mode _mode;
        private MatchSimulation _sim;
        private MatchState _previous;
        private readonly SimInput[] _inputs = new SimInput[SeatFrame.SeatCount];
        private readonly AIController[] _ai = new AIController[SeatFrame.SeatCount];
        private readonly LocalPlayerInput _local = new LocalPlayerInput();
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private int[] _characters;
        private PortraitStudio _studio;
        private ArenaView _arena;
        private MatchActorsView _actors;
        private MatchHud _hud;
        private Camera _camera;
        private Transform _worldRoot;
        private float _accumulator;
        private int _screenWidth, _screenHeight;
        private int _lastCountdown;
        private MatchLaunchConfig _launchConfig;   // offline matches started from the menu; null when played from the editor
        private ResumeStore.SavedMatch _resume;
        private uint _seed;
        private float _saveTimer;
        private bool _resumeCleared;

        private void Awake()
        {
            SettingsStore.Apply();
            _camera = Camera.main;
            if (_camera == null) _camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            SceneLook.Apply(_camera);

            if (NetSession.Host != null && NetSession.Host.InMatch)
            {
                _mode = Mode.Host;
                NetSession.Host.MatchStarted += StartMatch; // rematch
            }
            else if (NetSession.Guest != null && NetSession.Guest.Status == GuestStatus.InMatch)
            {
                _mode = Mode.Guest;
                NetSession.Guest.MatchStarted += StartMatch;
            }
            else
            {
                _mode = Mode.Offline;
                ApplyLaunchConfig();
            }

            _studio = new PortraitStudio(transform);
            StartCoroutine(_studio.RenderOnce());
            StartMatch();
        }

        private void OnDestroy()
        {
            if (NetSession.Host != null) NetSession.Host.MatchStarted -= StartMatch;
            if (NetSession.Guest != null) NetSession.Guest.MatchStarted -= StartMatch;
        }

        /// <summary>
        /// Uses the setup chosen in the menu, if the match was started from there.
        /// Playing the Match scene directly in the editor uses the Inspector values instead.
        /// </summary>
        private void ApplyLaunchConfig()
        {
            var config = MatchLaunch.Pending;
            if (config == null) return;
            MatchLaunch.Pending = null;
            _launchConfig = config.Clone();
            _resume = ResumeStore.Pending;
            ResumeStore.Pending = null;

            mode = config.Mode;
            ballCount = config.BallCount;
            durationSeconds = config.DurationSeconds;
            goalsToEliminate = config.GoalsToEliminate;
            tieRule = config.TieRule;
            localSeat = MatchLaunchConfig.LocalSeat;
            seatActive = config.ActiveSeats();
            seatCharacter = config.CharactersBySeat();
            seatDifficulty = new AIDifficulty[SeatFrame.SeatCount];
            for (int i = 0; i < seatDifficulty.Length; i++) seatDifficulty[i] = config.DifficultyFor(i);
        }

        private void StartMatch()
        {
            if (_worldRoot != null) Destroy(_worldRoot.gameObject);
            _hud?.Destroy();
            _accumulator = 0f;
            Array.Clear(_ai, 0, _ai.Length);

            switch (_mode)
            {
                case Mode.Host:
                    _sim = NetSession.Host.Simulation;
                    localSeat = NetHost.LocalSeat;
                    _characters = NetSession.Host.Lobby.Characters();
                    break;

                case Mode.Guest:
                    _sim = NetSession.Guest.Simulation;
                    localSeat = NetSession.Guest.LocalSeat;
                    _characters = NetSession.Guest.Characters;
                    break;

                default:
                    _characters = new int[SeatFrame.SeatCount];
                    for (int i = 0; i < _characters.Length; i++)
                        _characters[i] = seatCharacter != null && i < seatCharacter.Length ? Mathf.Clamp(seatCharacter[i], 0, Characters.Count - 1) : i;
                    var settings = new MatchSettings
                    {
                        Mode = mode,
                        BallCount = ballCount,
                        DurationSeconds = durationSeconds,
                        GoalsToEliminate = goalsToEliminate,
                        TieRule = tieRule,
                        Seed = _seed = _resume != null ? _resume.Seed : (uint)Environment.TickCount,
                        SeatActive = BuildSeatList(),
                    };
                    _sim = new MatchSimulation(settings);
                    RestoreIfResuming();
                    for (int i = 0; i < SeatFrame.SeatCount; i++)
                    {
                        var level = seatDifficulty != null && i < seatDifficulty.Length ? seatDifficulty[i] : AIDifficulty.Medium;
                        _ai[i] = i != localSeat && settings.SeatActive[i] ? new AIController(i, level, settings.Seed * 7919u + (uint)i) : null;
                    }
                    break;
            }

            _previous = _sim.State.Clone();
            _worldRoot = new GameObject("Match World").transform;
            _arena = new ArenaView(_sim.Arena, _worldRoot, localSeat);
            _actors = new MatchActorsView(_sim, _worldRoot, _characters);

            var lobby = _mode == Mode.Host ? NetSession.Host.Lobby : _mode == Mode.Guest ? NetSession.Guest.Lobby : null;
            _hud = new MatchHud(_sim, localSeat, _characters, _studio.Portraits, PlayerNamesFrom(lobby));
            _hud.ConfigureFor(_mode == Mode.Offline ? MatchHud.Role.Offline : _mode == Mode.Host ? MatchHud.Role.Host : MatchHud.Role.Guest);
            _hud.PauseClicked += TogglePause;
            _hud.RestartClicked += Restart;
            _hud.QuitClicked += Quit;
            _hud.BlastPressed += _local.PressBlast;

            PlaceCamera();
        }

        /// <summary>Human players' names by seat; AI and empty seats are null so the HUD shows the character.</summary>
        private static string[] PlayerNamesFrom(LobbyState lobby)
        {
            if (lobby == null) return null;
            var names = new string[SeatFrame.SeatCount];
            for (int i = 0; i < names.Length; i++) names[i] = lobby.Seats[i].Type == SeatType.Human ? lobby.Seats[i].Name : null;
            return names;
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
            // The session ended under us (the host left); NetSession is already loading the menu.
            if (_mode == Mode.Host && NetSession.Host == null || _mode == Mode.Guest && NetSession.Guest == null) return;

            if (Screen.width != _screenWidth || Screen.height != _screenHeight) PlaceCamera();
            if (LocalPlayerInput.PausePressed()) TogglePause();

            _local.Sample(_camera, localSeat, _sim.Arena.HalfSize);

            float dt = _sim.DeltaTime;
            _accumulator = Mathf.Min(_accumulator + Time.deltaTime, 0.25f);
            float alpha;
            switch (_mode)
            {
                case Mode.Guest:
                    var guest = NetSession.Guest;
                    while (_accumulator >= dt)
                    {
                        _accumulator -= dt;
                        if (guest.Tick(_local.Consume()))
                        {
                            GameAudio.Play(Sfx.Blast);
                            Haptics.Pulse(20);
                            float along = guest.Simulation.State.Seats[localSeat].PaddlePosition;
                            _actors.PlayBlast(localSeat, SeatFrame.ToWorld(localSeat, _sim.Arena.HalfSize, along, _sim.Config.PaddleOffset));
                        }
                    }
                    guest.GetDisplay(_previous, _sim.State, out alpha);
                    HandleEvents(guest.TakeEvents());
                    break;

                default:
                    while (_accumulator >= dt)
                    {
                        _accumulator -= dt;
                        _previous.CopyFrom(_sim.State);
                        if (_mode == Mode.Host)
                        {
                            NetSession.Host.Step(_local.Consume(), dt);
                        }
                        else
                        {
                            for (int i = 0; i < SeatFrame.SeatCount; i++)
                                _inputs[i] = i == localSeat ? _local.Consume() : _ai[i] != null ? _ai[i].Think(_sim) : default;
                            _sim.Step(_inputs);
                        }
                        HandleEvents(_sim.Events);
                    }
                    bool moving = _sim.State.Phase == MatchPhase.Playing || _sim.State.Phase == MatchPhase.Countdown;
                    alpha = moving ? _accumulator / dt : 1f;
                    break;
            }

            CountdownBeeps();
            _arena.Sync(_sim.State, Time.deltaTime);
            _actors.Sync(_previous, _sim.State, alpha, Time.deltaTime);
            _hud.Update(_sim.State, Time.deltaTime);
            AutoSave(Time.deltaTime);
        }

        private void HandleEvents(IReadOnlyList<SimEvent> events)
        {
            _events.Clear();
            _events.AddRange(events); // the list may change while we react
            foreach (var e in _events)
            {
                switch (e.Type)
                {
                    case SimEventType.Blast:
                        _actors.PlayBlast(e.Seat, e.Position);
                        GameAudio.Play(Sfx.Blast, e.Seat == localSeat ? 1f : 0.7f);
                        if (e.Seat == localSeat) Haptics.Pulse(20);
                        break;
                    case SimEventType.PaddleHit:
                        GameAudio.Play(Sfx.PaddleHit, 0.8f, UnityEngine.Random.Range(0.95f, 1.08f));
                        break;
                    case SimEventType.WallBounce:
                        GameAudio.Play(Sfx.WallBounce, 0.5f, UnityEngine.Random.Range(0.9f, 1.1f));
                        break;
                    case SimEventType.BallCollision:
                        GameAudio.Play(Sfx.BallClack, 0.7f);
                        break;
                    case SimEventType.Goal:
                        _arena.FlashGoal(e.Seat);
                        _hud.OnGoal(e.Seat);
                        _hud.ShowBanner(e.Seat == localSeat ? "OUCH!" : $"{_hud.Label(e.Seat).ToUpperInvariant()} CONCEDES!",
                            SimSpace.SeatColors[e.Seat]);
                        if (e.Seat == localSeat) { GameAudio.Play(Sfx.Conceded); Haptics.Pulse(40); }
                        else GameAudio.Play(Sfx.Goal, 0.8f);
                        break;
                    case SimEventType.Eliminated:
                        _hud.ShowBanner(e.Seat == localSeat ? "YOU'RE OUT!" : $"{_hud.Label(e.Seat).ToUpperInvariant()} IS OUT!", Color.white, 2f);
                        GameAudio.Play(Sfx.Eliminated);
                        break;
                    case SimEventType.PhaseChanged:
                        if (_sim.State.Phase == MatchPhase.Playing) GameAudio.Play(Sfx.Go);
                        break;
                    case SimEventType.MatchEnded:
                        var s = _sim.State;
                        GameAudio.Play(!s.IsDraw && s.WinnerSeat == localSeat ? Sfx.Win : Sfx.Lose);
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

        /// <summary>A beep on each whole second of a countdown (3, 2, 1); "Go" comes from the phase change.</summary>
        private void CountdownBeeps()
        {
            var s = _sim.State;
            int n = s.Phase == MatchPhase.Countdown ? Mathf.CeilToInt(s.CountdownRemaining) : 0;
            if (n > 0 && n != _lastCountdown) GameAudio.Play(Sfx.CountdownBeep);
            _lastCountdown = n;
        }

        private void TogglePause()
        {
            bool paused = _sim.State.Phase == MatchPhase.Paused;
            switch (_mode)
            {
                case Mode.Host:
                    if (paused) NetSession.Host.Resume(); else NetSession.Host.Pause();
                    break;
                case Mode.Guest:
                    if (paused) NetSession.Guest.RequestResume(); else NetSession.Guest.RequestPause();
                    break;
                default:
                    if (paused) _sim.Resume(); else _sim.Pause();
                    break;
            }
        }

        /// <summary>Restart (offline) or Rematch (host). Guests can't restart; the button is hidden for them.</summary>
        private void Restart()
        {
            if (_mode == Mode.Host) NetSession.Host.StartMatch(); // MatchStarted rebuilds the scene
            else if (_mode == Mode.Offline) { ResumeStore.Delete(); StartMatch(); }
        }

        /// <summary>
        /// Offline: back to the menu. Host after the match: everyone back to the lobby; host mid-match: ends it for all.
        /// Guest: leaves, and an AI takes the seat.
        /// </summary>
        private void Quit()
        {
            switch (_mode)
            {
                case Mode.Host:
                    if (_sim.IsEnded) { NetSession.ReturnToLobby(); return; }
                    NetSession.End();
                    break;
                case Mode.Guest:
                    NetSession.End();
                    break;
            }
            if (_mode == Mode.Offline) ResumeStore.Delete();
            if (Application.CanStreamedLevelBeLoaded(BootLoader.MenuScene)) SceneManager.LoadScene(BootLoader.MenuScene);
            else if (_mode == Mode.Offline) StartMatch();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            // US-6.1 / TDD 8.8: pause automatically when the app goes to the background.
            // A guest can't pause others by backgrounding, so it keeps going.
            if (_mode == Mode.Host) NetSession.Host?.Pause();
            else if (_mode == Mode.Offline && _sim != null)
            {
                _sim.Pause();
                SaveForResume();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && _mode == Mode.Offline) SaveForResume();
        }

        // ------------------------------------------------------------------ offline resume (TDD 9.4, US-6.6)

        private bool CanResume => _mode == Mode.Offline && _launchConfig != null && _sim != null && _sim.Settings.Mode != GameMode.Practice;

        /// <summary>Loads the saved state into the new simulation and leaves it paused, so the player resumes through a countdown.</summary>
        private void RestoreIfResuming()
        {
            if (_resume == null) return;
            var saved = _resume;
            _resume = null; // a later restart starts fresh
            var state = _sim.State.Clone();
            if (!ResumeStore.TryRestore(saved, state)) { ResumeStore.Delete(); return; }
            _sim.LoadState(state);
            if (_sim.State.Phase == MatchPhase.Playing || _sim.State.Phase == MatchPhase.Countdown) _sim.Pause();
        }

        private void AutoSave(float deltaTime)
        {
            if (!CanResume) return;
            if (_sim.IsEnded)
            {
                if (!_resumeCleared) { ResumeStore.Delete(); _resumeCleared = true; }
                return;
            }
            _resumeCleared = false;
            _saveTimer += deltaTime;
            if (_saveTimer < 10f) return;
            _saveTimer = 0f;
            SaveForResume();
        }

        private void SaveForResume()
        {
            if (CanResume && !_sim.IsEnded) ResumeStore.Save(_launchConfig, _seed, _sim.State);
        }

        private void PlaceCamera()
        {
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            if (_sim != null) MatchCameraRig.Place(_camera, localSeat, _sim.Arena.HalfSize, cameraPitch, cameraFieldOfView);
        }
    }
}
