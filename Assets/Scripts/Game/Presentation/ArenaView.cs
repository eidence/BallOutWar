using BOW.Simulation;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Builds the neon sci-fi arena from an <see cref="ArenaDefinition"/> and keeps it in step with the match:
    /// sealed goals show an energy barrier, and a goal briefly flares when someone concedes.
    /// All geometry is placeholder primitives; the layout matches the simulation exactly.
    /// </summary>
    public sealed class ArenaView
    {
        private const float WallHeight = 0.9f;
        private const float WallThickness = 0.5f;
        private const float PocketDepth = 2.0f;
        private const float GateHeight = 2.2f;
        private const float FlashSeconds = 0.6f;

        private static readonly Color Cyan = new Color(0.15f, 0.95f, 1f);

        private readonly GameObject[] _barriers = new GameObject[SeatFrame.SeatCount];
        private readonly Material[] _gateMaterials = new Material[SeatFrame.SeatCount];
        private readonly float[] _flash = new float[SeatFrame.SeatCount];
        private readonly Color[] _gateColors = new Color[SeatFrame.SeatCount];

        public Transform Root { get; }

        public ArenaView(ArenaDefinition arena, Transform parent, int localSeat)
        {
            Root = new GameObject("Arena").transform;
            Root.SetParent(parent, false);

            float h = arena.HalfSize;
            float g = arena.GoalHalfWidth;

            var floorMat = PrototypeMaterials.Create(Color.white, 0.8f, 0.4f);
            floorMat.mainTexture = ProceduralTextures.Floor();
            if (floorMat.HasProperty("_BaseMap")) floorMat.SetTexture("_BaseMap", floorMat.mainTexture);
            var metal = PrototypeMaterials.Create(new Color(0.16f, 0.18f, 0.22f), 0.7f, 0.8f);
            var darkMetal = PrototypeMaterials.Create(new Color(0.08f, 0.09f, 0.11f), 0.5f, 0.6f);
            var neon = PrototypeMaterials.Emissive(Cyan, 3.5f);
            var barrierMat = PrototypeMaterials.Emissive(new Color(1f, 0.35f, 0.25f), 1.6f);

            // Floor, plus a dark apron outside the arena so no sky shows at the edges.
            PrototypeMaterials.Part(PrimitiveType.Cube, "Floor", Root, floorMat,
                new Vector3(0f, -0.05f, 0f), new Vector3(2f * h, 0.1f, 2f * h));
            PrototypeMaterials.Part(PrimitiveType.Cube, "Apron", Root, darkMetal,
                new Vector3(0f, -0.6f, 0f), new Vector3(8f * h, 0.1f, 8f * h));

            for (int seat = 0; seat < SeatFrame.SeatCount; seat++)
            {
                Quaternion rot = SimSpace.SeatRotation(seat);
                string side = SimSpace.SeatNames[seat];
                Color seatColor = SimSpace.SeatColors[seat];
                var side_ = new GameObject(side).transform;
                side_.SetParent(Root, false);
                side_.localPosition = SimSpace.ToWorld(SeatFrame.GoalCenter(seat, h));
                side_.localRotation = rot;
                // In this local space: +X runs along the goal line, +Z points into the arena, origin = goal centre.

                // Wall stretches either side of the goal (extended outward to close the corners), each with a neon strip.
                float length = h - g + WallThickness;
                float along = (h + g + WallThickness) * 0.5f;
                foreach (float sign in new[] { -1f, 1f })
                {
                    PrototypeMaterials.Part(PrimitiveType.Cube, "Wall", side_, metal,
                        new Vector3(sign * along, WallHeight * 0.5f, -WallThickness * 0.5f), new Vector3(length, WallHeight, WallThickness));
                    PrototypeMaterials.Part(PrimitiveType.Cube, "Neon Strip", side_, neon,
                        new Vector3(sign * (h + g) * 0.5f, WallHeight * 0.55f, 0.02f), new Vector3(h - g - 0.3f, 0.12f, 0.06f));
                    PrototypeMaterials.Part(PrimitiveType.Cube, "Floor Trim", side_, neon,
                        new Vector3(sign * (h + g) * 0.5f, 0.01f, 0.12f), new Vector3(h - g - 0.3f, 0.02f, 0.06f));
                }

                // Goal pocket: dark floor with a glowing seat-coloured back light.
                PrototypeMaterials.Part(PrimitiveType.Cube, "Pocket", side_, darkMetal,
                    new Vector3(0f, -0.07f, -PocketDepth * 0.5f), new Vector3(2f * g, 0.1f, PocketDepth));
                PrototypeMaterials.Part(PrimitiveType.Cube, "Pocket Back", side_, metal,
                    new Vector3(0f, WallHeight * 0.5f, -PocketDepth - 0.15f), new Vector3(2f * g + 1f, WallHeight, 0.3f));
                PrototypeMaterials.Part(PrimitiveType.Cube, "Pocket Light", side_, PrototypeMaterials.Emissive(seatColor, 2.5f),
                    new Vector3(0f, WallHeight * 0.5f, -PocketDepth), new Vector3(2f * g - 0.4f, 0.15f, 0.05f));

                // Goal gate: two glowing uprights on the posts and a crossbeam, in the seat colour. Flares on a goal.
                var gateMat = PrototypeMaterials.Emissive(seatColor, 2f);
                _gateMaterials[seat] = gateMat;
                _gateColors[seat] = seatColor;
                float postWidth = arena.PostRadius * 2f;
                // The near (local) goal gets low posts and no crossbeam, so nothing blocks the view of your own paddle.
                float gateHeight = seat == localSeat ? WallHeight * 1.2f : GateHeight;
                foreach (float sign in new[] { -1f, 1f })
                {
                    PrototypeMaterials.Part(PrimitiveType.Cylinder, "Post", side_, metal,
                        new Vector3(sign * g, gateHeight * 0.5f, 0f), new Vector3(postWidth, gateHeight * 0.5f, postWidth));
                    PrototypeMaterials.Part(PrimitiveType.Cylinder, "Post Ring", side_, gateMat,
                        new Vector3(sign * g, WallHeight * 0.55f, 0f), new Vector3(postWidth * 1.15f, 0.06f, postWidth * 1.15f));
                    PrototypeMaterials.Part(PrimitiveType.Cylinder, "Post Cap", side_, gateMat,
                        new Vector3(sign * g, gateHeight, 0f), new Vector3(postWidth * 1.1f, 0.08f, postWidth * 1.1f));
                }
                if (seat != localSeat)
                {
                    PrototypeMaterials.Part(PrimitiveType.Cube, "Crossbeam", side_, metal,
                        new Vector3(0f, GateHeight, -0.1f), new Vector3(2f * g, 0.35f, 0.35f));
                    PrototypeMaterials.Part(PrimitiveType.Cube, "Crossbeam Light", side_, gateMat,
                        new Vector3(0f, GateHeight - 0.2f, 0.08f), new Vector3(2f * g - 0.6f, 0.08f, 0.04f));
                }

                // Energy barrier shown when the goal is sealed (closed seat, knocked out, or watching sudden death).
                _barriers[seat] = PrototypeMaterials.Part(PrimitiveType.Cube, "Barrier", side_, barrierMat,
                    new Vector3(0f, WallHeight * 0.4f, -WallThickness * 0.5f), new Vector3(2f * g, WallHeight * 0.8f, WallThickness * 0.6f));
            }

            // Corner towers with glowing bands.
            float corner = h + WallThickness * 0.5f;
            foreach (var c in new[] { new Vector3(-corner, 0f, -corner), new Vector3(corner, 0f, -corner), new Vector3(-corner, 0f, corner), new Vector3(corner, 0f, corner) })
            {
                PrototypeMaterials.Part(PrimitiveType.Cylinder, "Tower", Root, metal, c + Vector3.up * 0.9f, new Vector3(1.6f, 0.9f, 1.6f));
                PrototypeMaterials.Part(PrimitiveType.Cylinder, "Tower Band", Root, neon, c + Vector3.up * 1.2f, new Vector3(1.7f, 0.05f, 1.7f));
                PrototypeMaterials.Part(PrimitiveType.Cylinder, "Tower Top", Root, neon, c + Vector3.up * 1.82f, new Vector3(1.0f, 0.03f, 1.0f));
            }

            foreach (var bumper in arena.Bumpers)
            {
                PrototypeMaterials.Part(PrimitiveType.Cylinder, "Bumper", Root, metal,
                    SimSpace.ToWorld(bumper.Center, 0.4f), new Vector3(bumper.Radius * 2f, 0.4f, bumper.Radius * 2f));
                PrototypeMaterials.Part(PrimitiveType.Cylinder, "Bumper Ring", Root, neon,
                    SimSpace.ToWorld(bumper.Center, 0.6f), new Vector3(bumper.Radius * 2.1f, 0.04f, bumper.Radius * 2.1f));
            }
        }

        public void FlashGoal(int seat) => _flash[seat] = FlashSeconds;

        public void Sync(MatchState state, float deltaTime)
        {
            for (int seat = 0; seat < SeatFrame.SeatCount; seat++)
            {
                bool sealedGoal = !state.Seats[seat].GoalOpen;
                if (_barriers[seat].activeSelf != sealedGoal) _barriers[seat].SetActive(sealedGoal);

                if (_flash[seat] > 0f)
                {
                    _flash[seat] = Mathf.Max(0f, _flash[seat] - deltaTime);
                    float t = _flash[seat] / FlashSeconds;
                    PrototypeMaterials.SetGlow(_gateMaterials[seat], Color.Lerp(_gateColors[seat], Color.white, t), 2f + 10f * t);
                }
            }
        }
    }
}
