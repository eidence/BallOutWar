using BOW.Simulation;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// The Meshy bumper car (Resources/Cars), cut out of the "Whisker Racer" model with the driver removed. One mesh,
    /// with a paint texture per seat colour (blue, red, yellow, green, made by hue-shifting the blue paint).
    /// Falls back to the code-built car when the files are missing.
    /// </summary>
    public static class CarModel
    {
        /// <summary>The model is round; stretch it this much front to back so the car is longer than it is wide.</summary>
        public const float LengthStretch = 1.2f;
        /// <summary>Where the driver sits, as fractions of the model's height (from the floor) and depth (from the back).</summary>
        private const float SeatHeightFraction = 0.36f, SeatDepthFraction = 0.26f;
        /// <summary>Where the hands hold the wheel rim, in the source model's own units (measured from the mesh: the wheel is
        /// centred at x 0, about 0.1 either side, its rim around y -0.07, z 0.24, tilted toward the seat).</summary>
        private static readonly Vector3 GripInModel = new Vector3(0.09f, -0.07f, 0.23f);

        private static readonly string[] Paints = { "blue", "red", "yellow", "green" }; // matches SimSpace.SeatColors
        private static Mesh _mesh;
        private static bool _tried;

        /// <summary>Statics survive between Play sessions here (no domain reload), but runtime materials don't.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            _mesh = null;
            _tried = false;
            System.Array.Clear(Materials, 0, Materials.Length);
        }
        private static readonly Material[] Materials = new Material[Paints.Length];

        public static bool Available => Mesh != null;

        private static Mesh Mesh
        {
            get
            {
                if (_tried) return _mesh;
                _tried = true;
                var prefab = Resources.Load<GameObject>("Cars/bumper_car");
                var filter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
                _mesh = filter != null ? filter.sharedMesh : null;
                if (_mesh == null) Debug.LogWarning("No bumper car model at Resources/Cars/bumper_car; using the code-built car.");
                return _mesh;
            }
        }

        /// <summary>
        /// Adds the car under <paramref name="body"/> (local space: +X along the goal line, +Z into the arena),
        /// sized to the paddle's hit length with its front bumper on the front of the hit capsule.
        /// </summary>
        public static bool TryBuild(Transform body, GameplayConfig cfg, int seat, out Vector3 seatPosition, out Vector3 leftGrip, out Vector3 rightGrip)
        {
            seatPosition = Vector3.zero;
            leftGrip = rightGrip = Vector3.zero;
            var mesh = Mesh;
            if (mesh == null) return false;

            var go = new GameObject("Car Model");
            go.transform.SetParent(body, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = PaintFor(seat);

            Bounds b = mesh.bounds;
            float width = (cfg.PaddleHalfLength + cfg.PaddleRadius) * 2f;
            float s = width / Mathf.Max(0.01f, b.size.x);
            var scale = new Vector3(s, s, s * LengthStretch);
            float front = cfg.PaddleRadius;
            var offset = new Vector3(-b.center.x * scale.x, -b.min.y * scale.y, front - b.max.z * scale.z);
            go.transform.localScale = scale;
            go.transform.localPosition = offset;

            var seatInModel = new Vector3(b.center.x, b.min.y + b.size.y * SeatHeightFraction, b.min.z + b.size.z * SeatDepthFraction);
            seatPosition = offset + Vector3.Scale(seatInModel, scale);
            leftGrip = offset + Vector3.Scale(new Vector3(-GripInModel.x, GripInModel.y, GripInModel.z), scale);
            rightGrip = offset + Vector3.Scale(GripInModel, scale);
            return true;
        }

        private static Material PaintFor(int seat)
        {
            int i = Mathf.Clamp(seat, 0, Paints.Length - 1);
            if (Materials[i] != null) return Materials[i];
            var texture = Resources.Load<Texture2D>("Cars/bumper_car_" + Paints[i]);
            var material = PrototypeMaterials.Create(Color.white, 0.55f, 0.1f);
            if (texture != null)
            {
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            }
            else material.color = SimSpace.SeatColors[i];
            Materials[i] = material;
            return material;
        }
    }
}
