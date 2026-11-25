using BOW.AI;
using BOW.Simulation;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// The main menu's backdrop: four AIs play an endless demo match while the camera slowly circles the arena.
    /// Reuses the real simulation and views, so the menu always shows the current game.
    /// </summary>
    public sealed class MenuBackdrop
    {
        private const float OrbitDegreesPerSecond = 6f;

        private readonly Camera _camera;
        private readonly Transform _root;
        private readonly int[] _characters;
        private readonly PlayerInput[] _inputs = new PlayerInput[SeatFrame.SeatCount];
        private readonly AIController[] _ai = new AIController[SeatFrame.SeatCount];
        private MatchSimulation _sim;
        private MatchState _previous;
        private ArenaView _arena;
        private MatchActorsView _actors;
        private Transform _world;
        private float _accumulator;
        private float _angle = 200f;
        private uint _seed = 11;

        public MenuBackdrop(Camera camera, Transform root, int[] characters)
        {
            _camera = camera;
            _root = root;
            _characters = characters;
            Restart();
        }

        private void Restart()
        {
            if (_world != null) Object.Destroy(_world.gameObject);
            _seed++;
            var settings = new MatchSettings { Mode = GameMode.Timed, DurationSeconds = 600f, BallCount = 3, Seed = _seed };
            _sim = new MatchSimulation(settings);
            _previous = _sim.State.Clone();
            for (int i = 0; i < SeatFrame.SeatCount; i++)
                _ai[i] = new AIController(i, (AIDifficulty)(1 + i % 3), _seed * 31u + (uint)i);

            _world = new GameObject("Menu Backdrop").transform;
            _world.SetParent(_root, false);
            _arena = new ArenaView(_sim.Arena, _world, localSeat: -1);
            _actors = new MatchActorsView(_sim, _world, _characters);
        }

        public void Update(float deltaTime)
        {
            float dt = _sim.DeltaTime;
            _accumulator = Mathf.Min(_accumulator + deltaTime, 0.25f);
            while (_accumulator >= dt)
            {
                _accumulator -= dt;
                _previous.CopyFrom(_sim.State);
                for (int i = 0; i < SeatFrame.SeatCount; i++) _inputs[i] = _ai[i].Think(_sim);
                _sim.Step(_inputs);
                foreach (var e in _sim.Events)
                {
                    if (e.Type == SimEventType.Blast) _actors.PlayBlast(e.Seat, e.Position);
                    if (e.Type == SimEventType.Goal) _arena.FlashGoal(e.Seat);
                }
            }
            if (_sim.IsEnded) Restart();

            _arena.Sync(_sim.State, deltaTime);
            _actors.Sync(_previous, _sim.State, _accumulator / dt, deltaTime);

            // Slow orbit, looking down at the arena from a low angle.
            _angle += OrbitDegreesPerSecond * deltaTime;
            Quaternion rot = Quaternion.Euler(38f, _angle, 0f);
            _camera.fieldOfView = 40f;
            _camera.transform.rotation = rot;
            _camera.transform.position = rot * new Vector3(0f, 0f, -30f) + new Vector3(0f, -2f, 0f);
        }
    }
}
