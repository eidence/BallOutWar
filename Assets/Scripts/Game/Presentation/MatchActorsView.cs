using BOW.Simulation;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Draws the bumper-cars with their drivers, the balls and blast rings, interpolating between the last two
    /// simulation ticks so motion is smooth at any frame rate (TDD 6.2).
    /// </summary>
    public sealed class MatchActorsView
    {
        private const float MaxLeanDegrees = 9f;
        private const float PunchSeconds = 0.18f;
        private const float CarDepth = 3.1f;

        /// <summary>
        /// Builds a bumper car in the car's local space (+X along the goal line, +Z into the arena). The front of
        /// the rubber bumper sits on the front of the simulation's paddle capsule, so balls visibly bounce off it;
        /// the tub reaches back into the goal pocket. Returns the leaning "Body" and where the driver sits.
        /// </summary>
        private static Transform BuildBumperCar(Transform car, GameplayConfig cfg, Color color, Material rubber, out Vector3 seat)
        {
            float width = (cfg.PaddleHalfLength + cfg.PaddleRadius) * 2f - 0.3f; // bumper bulge brings it back to full length
            var l = new BumperCarMesh.Layout(width, cfg.PaddleRadius - 0.15f, CarDepth);

            var paint = PrototypeMaterials.Create(color, 0.8f, 0.25f);
            var interior = PrototypeMaterials.Create(Color.Lerp(color, Color.black, 0.75f), 0.3f);
            var chrome = PrototypeMaterials.Create(new Color(0.85f, 0.87f, 0.9f), 0.9f, 1f);
            var lamp = PrototypeMaterials.Emissive(new Color(1f, 0.85f, 0.35f), 3f);
            var tip = PrototypeMaterials.Emissive(color, 4f);

            var body = new GameObject("Body").transform;
            body.SetParent(car, false);

            var shell = new GameObject("Shell");
            shell.transform.SetParent(body, false);
            shell.AddComponent<MeshFilter>().sharedMesh = BumperCarMesh.Get(l);
            shell.AddComponent<MeshRenderer>().sharedMaterials = new[] { paint, rubber, interior };

            // A ring of round lamps studding the bumper.
            const int lamps = 22;
            for (int i = 0; i < lamps; i++)
            {
                float t = (i + 0.5f) * Mathf.PI * 2f / lamps;
                PrototypeMaterials.Part(PrimitiveType.Sphere, "Lamp", body, lamp,
                    BumperCarMesh.Outer(l, t, 0.15f, 0.26f), Vector3.one * 0.13f);
            }

            // Steering wheel on a column rising from the front of the cockpit, tilted toward the driver.
            float wheelZ = l.CockpitZ + l.CockpitHalfDepth - 0.08f;
            PrototypeMaterials.Part(PrimitiveType.Cylinder, "Steering Column", body, rubber,
                new Vector3(0f, 0.7f, wheelZ + 0.05f), new Vector3(0.07f, 0.22f, 0.07f), new Vector3(-30f, 0f, 0f));
            PrototypeMaterials.Part(PrimitiveType.Cylinder, "Steering Wheel", body, rubber,
                new Vector3(0f, 0.92f, wheelZ - 0.05f), new Vector3(0.42f, 0.025f, 0.42f), new Vector3(-55f, 0f, 0f));
            PrototypeMaterials.Part(PrimitiveType.Sphere, "Horn", body, tip,
                new Vector3(0f, 0.935f, wheelZ - 0.05f), new Vector3(0.12f, 0.05f, 0.12f), new Vector3(-55f, 0f, 0f));

            // The pole at the back with a glowing spark tip.
            float poleZ = l.BackZ + 0.3f;
            PrototypeMaterials.Part(PrimitiveType.Cylinder, "Pole", body, chrome,
                new Vector3(0f, l.BackRimY + 0.45f, poleZ), new Vector3(0.06f, 0.5f, 0.06f));
            PrototypeMaterials.Part(PrimitiveType.Sphere, "Pole Tip", body, tip,
                new Vector3(0f, l.BackRimY + 0.98f, poleZ), Vector3.one * 0.15f);

            seat = new Vector3(0f, l.FloorY - 0.05f, l.CockpitZ + 0.02f);
            return body;
        }

        private readonly MatchSimulation _sim;
        private readonly Transform[] _cars = new Transform[SeatFrame.SeatCount];
        private readonly Transform[] _bodies = new Transform[SeatFrame.SeatCount];
        private readonly Transform[] _drivers = new Transform[SeatFrame.SeatCount];
        private readonly float[] _punch = new float[SeatFrame.SeatCount];
        private readonly Vector3[] _seats = new Vector3[SeatFrame.SeatCount];
        private readonly Transform[] _balls = new Transform[MatchState.MaxBalls];
        private readonly TrailRenderer[] _trails = new TrailRenderer[MatchState.MaxBalls];
        private readonly BlastRing[] _rings = new BlastRing[8];

        public MatchActorsView(MatchSimulation sim, Transform parent, int[] characterBySeat)
        {
            _sim = sim;
            var root = new GameObject("Actors").transform;
            root.SetParent(parent, false);
            var cfg = sim.Config;
            var rubber = PrototypeMaterials.Create(new Color(0.07f, 0.07f, 0.08f), 0.3f);

            for (int seat = 0; seat < SeatFrame.SeatCount; seat++)
            {
                var color = SimSpace.SeatColors[seat];
                var car = new GameObject($"{SimSpace.SeatNames[seat]} Car").transform;
                car.SetParent(root, false);
                car.localRotation = SimSpace.SeatRotation(seat);
                // Local space: +X along the goal line, +Z into the arena.

                var body = BuildBumperCar(car, cfg, color, rubber, out var seatPos);
                _seats[seat] = seatPos;

                var driver = Characters.Build(characterBySeat[seat], body, seated: true);
                driver.localPosition = seatPos;
                driver.localScale = Vector3.one * 1.05f;

                _cars[seat] = car;
                _bodies[seat] = body;
                _drivers[seat] = driver;
            }

            var chrome = PrototypeMaterials.Create(new Color(0.92f, 0.94f, 0.97f), 0.95f, 1f);
            var trailMat = PrototypeMaterials.Sprites();
            for (int i = 0; i < MatchState.MaxBalls; i++)
            {
                var ball = PrototypeMaterials.Primitive(PrimitiveType.Sphere, $"Ball {i}", root, chrome);
                ball.transform.localScale = Vector3.one * (cfg.BallRadius * 2f);

                var trail = ball.AddComponent<TrailRenderer>();
                trail.sharedMaterial = trailMat;
                trail.time = 0.22f;
                trail.minVertexDistance = 0.05f;
                trail.widthMultiplier = cfg.BallRadius * 1.6f;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                trail.colorGradient = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(new Color(0.6f, 0.95f, 1f), 0f), new GradientColorKey(new Color(0.2f, 0.6f, 1f), 1f) },
                    alphaKeys = new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) },
                };
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                ball.SetActive(false);
                _balls[i] = ball.transform;
                _trails[i] = trail;
            }

            for (int i = 0; i < _rings.Length; i++) _rings[i] = new BlastRing(root);
        }

        /// <summary>Positions everything between <paramref name="previous"/> and <paramref name="current"/>.</summary>
        public void Sync(MatchState previous, MatchState current, float alpha, float deltaTime)
        {
            float h = _sim.Arena.HalfSize;
            var cfg = _sim.Config;
            float time = Time.time;

            for (int seat = 0; seat < SeatFrame.SeatCount; seat++)
            {
                var s = current.Seats[seat];
                bool show = s.IsActive;
                var go = _cars[seat].gameObject;
                if (go.activeSelf != show) go.SetActive(show);
                if (!show) continue;

                float pos = Mathf.Lerp(previous.Seats[seat].PaddlePosition, s.PaddlePosition, alpha);
                _cars[seat].localPosition = SimSpace.ToWorld(SeatFrame.ToWorld(seat, h, pos, cfg.PaddleOffset));

                // Lean into turns, bob gently, and punch on a blast.
                float lean = -s.PaddleVelocity / cfg.PaddleMaxSpeed * MaxLeanDegrees;
                _bodies[seat].localRotation = Quaternion.Slerp(_bodies[seat].localRotation, Quaternion.Euler(0f, 0f, lean), 1f - Mathf.Exp(-12f * deltaTime));
                if (_punch[seat] > 0f) _punch[seat] = Mathf.Max(0f, _punch[seat] - deltaTime);
                float punch = Mathf.Sin(_punch[seat] / PunchSeconds * Mathf.PI) * 0.18f;
                _bodies[seat].localScale = new Vector3(1f + punch, 1f - punch * 0.5f, 1f + punch);
                _drivers[seat].localPosition = _seats[seat] + new Vector3(0f, Mathf.Sin(time * 5f + seat) * 0.03f, 0f);
            }

            float r = cfg.BallRadius;
            for (int i = 0; i < MatchState.MaxBalls; i++)
            {
                var b = current.Balls[i];
                var go = _balls[i].gameObject;
                var p = previous.Balls[i];
                bool spawned = b.Active && !p.Active;
                if (go.activeSelf != b.Active) go.SetActive(b.Active);
                if (!b.Active) continue;

                // Only interpolate when the ball existed last tick, so a respawn doesn't streak across the arena.
                Vector3 now = SimSpace.ToWorld(b.Position, r);
                _balls[i].localPosition = p.Active ? Vector3.Lerp(SimSpace.ToWorld(p.Position, r), now, alpha) : now;
                if (spawned) _trails[i].Clear();

                // Roll the ball in its direction of travel.
                Vector3 v = SimSpace.Direction(b.Velocity);
                if (v.sqrMagnitude > 1e-4f)
                    _balls[i].Rotate(Vector3.Cross(Vector3.up, v.normalized), v.magnitude * deltaTime / r * Mathf.Rad2Deg, Space.World);
            }

            foreach (var ring in _rings) ring.Tick(deltaTime);
        }

        public void PlayBlast(int seat, Vec2 center)
        {
            _punch[seat] = PunchSeconds;
            foreach (var ring in _rings)
            {
                if (ring.Busy) continue;
                ring.Play(SimSpace.ToWorld(center, 0.1f), _sim.Config.BlastRadius + _sim.Config.BallRadius, SimSpace.SeatColors[seat]);
                return;
            }
        }

        /// <summary>Expanding glowing ring (prototype blast effect).</summary>
        private sealed class BlastRing
        {
            private const int Segments = 56;
            private const float Duration = 0.28f;
            private readonly LineRenderer _line;
            private float _time = -1f;
            private float _radius;
            private Color _color;

            public bool Busy => _time >= 0f;

            public BlastRing(Transform parent)
            {
                var go = new GameObject("Blast Ring");
                go.transform.SetParent(parent, false);
                _line = go.AddComponent<LineRenderer>();
                _line.loop = true;
                _line.useWorldSpace = false;
                _line.positionCount = Segments;
                _line.sharedMaterial = PrototypeMaterials.Sprites();
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
            }

            public void Play(Vector3 position, float radius, Color color)
            {
                _line.transform.localPosition = position;
                _radius = radius;
                _color = Color.Lerp(color, Color.white, 0.4f);
                _time = 0f;
                _line.gameObject.SetActive(true);
                Draw(0f);
            }

            public void Tick(float dt)
            {
                if (_time < 0f) return;
                _time += dt;
                float t = Mathf.Clamp01(_time / Duration);
                Draw(t);
                if (t < 1f) return;
                _time = -1f;
                _line.gameObject.SetActive(false);
            }

            private void Draw(float t)
            {
                float radius = Mathf.Lerp(0.3f, _radius, 1f - (1f - t) * (1f - t));
                _line.widthMultiplier = Mathf.Lerp(0.35f, 0.03f, t);
                var c = _color;
                c.a = 1f - t * t;
                _line.startColor = _line.endColor = c;
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * Mathf.PI * 2f / Segments;
                    _line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                }
            }
        }
    }
}
