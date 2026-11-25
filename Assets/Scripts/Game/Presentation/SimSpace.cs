using BOW.Simulation;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Maps the simulation's 2D floor plane onto Unity's world: simulation (x, y) is Unity (x, 0, z).
    /// </summary>
    public static class SimSpace
    {
        public static Vector3 ToWorld(Vec2 v, float height = 0f) => new Vector3(v.X, height, v.Y);

        public static Vec2 ToSim(Vector3 p) => new Vec2(p.x, p.z);

        public static Vector3 Direction(Vec2 v) => new Vector3(v.X, 0f, v.Y);

        /// <summary>Rotation whose local +Z points into the arena and local +X along the seat's goal line to the player's right.</summary>
        public static Quaternion SeatRotation(int seat) => Quaternion.LookRotation(Direction(SeatFrame.Inward(seat)), Vector3.up);

        public static readonly Color[] SeatColors =
        {
            new Color(0.20f, 0.55f, 1.00f), // South: blue
            new Color(1.00f, 0.30f, 0.30f), // East: red
            new Color(1.00f, 0.78f, 0.15f), // North: yellow
            new Color(0.30f, 0.90f, 0.45f), // West: green
        };

        public static readonly string[] SeatNames = { "South", "East", "North", "West" };
    }

    /// <summary>
    /// Runtime materials for the prototype's neon look. Final art will replace these with authored materials.
    /// </summary>
    public static class PrototypeMaterials
    {
        private static Shader _lit;
        private static Shader _sprites;

        private static Shader Lit
        {
            get
            {
                if (_lit != null) return _lit;
                _lit = Shader.Find("Universal Render Pipeline/Lit");
                if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (_lit == null) _lit = Shader.Find("Standard");
                return _lit;
            }
        }

        /// <summary>Lit surface. <paramref name="smoothness"/> 0 = matte, 1 = mirror.</summary>
        public static Material Create(Color color, float smoothness = 0.35f, float metallic = 0f)
        {
            var material = new Material(Lit) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            return material;
        }

        /// <summary>Glowing surface. Intensity above 1 drives the bloom post-effect.</summary>
        public static Material Emissive(Color color, float intensity)
        {
            var material = Create(color * 0.2f, 0.6f);
            SetGlow(material, color, intensity);
            return material;
        }

        public static void SetGlow(Material material, Color color, float intensity)
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            material.SetColor("_EmissionColor", color * intensity);
        }

        /// <summary>Unlit, vertex-coloured material for lines and trails.</summary>
        public static Material Sprites()
        {
            if (_sprites == null) _sprites = Shader.Find("Sprites/Default");
            return new Material(_sprites);
        }

        /// <summary>Creates a primitive without a collider (the simulation does all collision).</summary>
        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Primitive with a local position, rotation (Euler) and scale in one call.</summary>
        public static GameObject Part(PrimitiveType type, string name, Transform parent, Material material,
            Vector3 position, Vector3 scale, Vector3 euler = default)
        {
            var go = Primitive(type, name, parent, material);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            return go;
        }
    }

    /// <summary>Textures and sprites generated in code, so the prototype needs no imported art.</summary>
    public static class ProceduralTextures
    {
        private static Sprite _circle;
        private static Sprite _white;

        /// <summary>Dark brushed-metal floor with panel seams, a launch pad in the middle and a faint lane to each goal.</summary>
        public static Texture2D Floor(int size = 1024)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Arena Floor", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            var rng = new System.Random(7);
            float half = size * 0.5f;
            int panel = size / 8;

            for (int y = 0; y < size; y++)
            {
                float streak = (float)rng.NextDouble() * 0.03f; // brushed-metal streaks run along X
                for (int x = 0; x < size; x++)
                {
                    float v = 0.20f + streak + (float)rng.NextDouble() * 0.025f;

                    // Panel seams.
                    if (x % panel < 2 || y % panel < 2) v *= 0.55f;

                    // Lanes toward each goal (darker band down the middle of the cross).
                    float dx = Mathf.Abs(x - half) / half, dy = Mathf.Abs(y - half) / half;
                    if (dx < 0.12f || dy < 0.12f) v *= 0.85f;

                    // Launch pad rings in the centre.
                    float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    if (Mathf.Abs(r - 0.16f) < 0.006f || Mathf.Abs(r - 0.10f) < 0.004f) v = 0.55f;
                    else if (r < 0.10f) v *= 0.7f;

                    byte c = (byte)(Mathf.Clamp01(v) * 255f);
                    byte b = (byte)(Mathf.Clamp01(v * 1.12f) * 255f);
                    pixels[y * size + x] = new Color32(c, c, b, 255);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Anti-aliased white circle, for round UI elements.</summary>
        public static Sprite Circle()
        {
            if (_circle != null) return _circle;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Circle" };
            var pixels = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                byte a = (byte)(Mathf.Clamp01(c - d) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circle;
        }

        public static Sprite White()
        {
            if (_white != null) return _white;
            var tex = Texture2D.whiteTexture;
            _white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return _white;
        }
    }
}
