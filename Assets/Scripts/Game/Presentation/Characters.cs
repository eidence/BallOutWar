using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// The four original launch characters, built from primitives until real models exist.
    /// Each faces local +Z, stands on the origin and is about 1.3 units tall.
    /// </summary>
    public static class Characters
    {
        public const int Count = 4;

        public static readonly string[] Names = { "Bolt", "Mitzi", "Hopper", "Pip" };

        public static readonly string[] Descriptions =
        {
            "Scrap-yard robot",
            "Street-smart cat",
            "Bouncy pond frog",
            "Speedy sky bird",
        };

        /// <param name="seated">Pose a real model sitting in a car (the primitive stand-ins are always seated-height).</param>
        public static Transform Build(int character, Transform parent, bool seated = false)
        {
            var root = new GameObject(Names[character]).transform;
            root.SetParent(parent, false);
            if (CharacterModels.TryBuild(character, root, seated)) return root;
            switch (character)
            {
                case 0: Robot(root); break;
                case 1: Cat(root); break;
                case 2: Frog(root); break;
                default: Bird(root); break;
            }
            return root;
        }

        private static GameObject P(PrimitiveType t, Transform parent, Material m, Vector3 pos, Vector3 scale, Vector3 euler = default)
            => PrototypeMaterials.Part(t, t.ToString(), parent, m, pos, scale, euler);

        private static void Robot(Transform r)
        {
            var steel = PrototypeMaterials.Create(new Color(0.62f, 0.66f, 0.72f), 0.75f, 0.9f);
            var dark = PrototypeMaterials.Create(new Color(0.15f, 0.16f, 0.19f), 0.5f, 0.6f);
            var visor = PrototypeMaterials.Emissive(new Color(0.2f, 1f, 0.95f), 4f);
            var bulb = PrototypeMaterials.Emissive(new Color(1f, 0.25f, 0.2f), 5f);

            P(PrimitiveType.Cube, r, steel, new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 0.5f, 0.45f));
            P(PrimitiveType.Cube, r, dark, new Vector3(0f, 0.3f, 0.23f), new Vector3(0.35f, 0.2f, 0.02f)); // chest panel
            P(PrimitiveType.Cube, r, steel, new Vector3(0f, 0.78f, 0f), new Vector3(0.62f, 0.5f, 0.5f));
            P(PrimitiveType.Cube, r, visor, new Vector3(0f, 0.8f, 0.26f), new Vector3(0.5f, 0.14f, 0.04f));
            P(PrimitiveType.Cylinder, r, dark, new Vector3(-0.33f, 0.78f, 0f), new Vector3(0.2f, 0.04f, 0.2f), new Vector3(0f, 0f, 90f));
            P(PrimitiveType.Cylinder, r, dark, new Vector3(0.33f, 0.78f, 0f), new Vector3(0.2f, 0.04f, 0.2f), new Vector3(0f, 0f, 90f));
            P(PrimitiveType.Cylinder, r, dark, new Vector3(0f, 1.12f, 0f), new Vector3(0.05f, 0.12f, 0.05f));
            P(PrimitiveType.Sphere, r, bulb, new Vector3(0f, 1.28f, 0f), Vector3.one * 0.13f);
        }

        private static void Cat(Transform r)
        {
            var fur = PrototypeMaterials.Create(new Color(0.95f, 0.55f, 0.18f), 0.25f);
            var cream = PrototypeMaterials.Create(new Color(1f, 0.92f, 0.8f), 0.25f);
            var pink = PrototypeMaterials.Create(new Color(1f, 0.55f, 0.65f), 0.3f);
            var eye = PrototypeMaterials.Create(new Color(0.08f, 0.2f, 0.1f), 0.9f);

            P(PrimitiveType.Sphere, r, fur, new Vector3(0f, 0.28f, 0f), new Vector3(0.6f, 0.55f, 0.5f));
            P(PrimitiveType.Sphere, r, cream, new Vector3(0f, 0.28f, 0.14f), new Vector3(0.38f, 0.4f, 0.25f));
            P(PrimitiveType.Sphere, r, fur, new Vector3(0f, 0.8f, 0.02f), new Vector3(0.64f, 0.56f, 0.56f));
            foreach (float s in new[] { -1f, 1f })
            {
                P(PrimitiveType.Cube, r, fur, new Vector3(s * 0.2f, 1.06f, 0f), new Vector3(0.2f, 0.2f, 0.06f), new Vector3(0f, 0f, 45f));
                P(PrimitiveType.Cube, r, pink, new Vector3(s * 0.2f, 1.05f, 0.02f), new Vector3(0.11f, 0.11f, 0.06f), new Vector3(0f, 0f, 45f));
                P(PrimitiveType.Sphere, r, eye, new Vector3(s * 0.13f, 0.86f, 0.25f), new Vector3(0.11f, 0.14f, 0.08f));
            }
            P(PrimitiveType.Sphere, r, cream, new Vector3(0f, 0.72f, 0.24f), new Vector3(0.3f, 0.18f, 0.16f));
            P(PrimitiveType.Sphere, r, pink, new Vector3(0f, 0.78f, 0.31f), Vector3.one * 0.07f);
            P(PrimitiveType.Capsule, r, fur, new Vector3(0.2f, 0.5f, -0.3f), new Vector3(0.09f, 0.28f, 0.09f), new Vector3(-30f, 0f, -25f));
        }

        private static void Frog(Transform r)
        {
            var skin = PrototypeMaterials.Create(new Color(0.35f, 0.82f, 0.3f), 0.7f);
            var belly = PrototypeMaterials.Create(new Color(0.85f, 0.95f, 0.6f), 0.5f);
            var white = PrototypeMaterials.Create(Color.white, 0.8f);
            var black = PrototypeMaterials.Create(new Color(0.05f, 0.05f, 0.05f), 0.9f);
            var blush = PrototypeMaterials.Create(new Color(1f, 0.5f, 0.55f), 0.3f);

            P(PrimitiveType.Sphere, r, skin, new Vector3(0f, 0.26f, 0f), new Vector3(0.78f, 0.52f, 0.62f));
            P(PrimitiveType.Sphere, r, belly, new Vector3(0f, 0.24f, 0.16f), new Vector3(0.5f, 0.36f, 0.32f));
            P(PrimitiveType.Sphere, r, skin, new Vector3(0f, 0.62f, 0.05f), new Vector3(0.82f, 0.46f, 0.62f));
            foreach (float s in new[] { -1f, 1f })
            {
                P(PrimitiveType.Sphere, r, skin, new Vector3(s * 0.22f, 0.86f, 0.08f), Vector3.one * 0.28f);
                P(PrimitiveType.Sphere, r, white, new Vector3(s * 0.22f, 0.89f, 0.16f), Vector3.one * 0.2f);
                P(PrimitiveType.Sphere, r, black, new Vector3(s * 0.22f, 0.9f, 0.25f), Vector3.one * 0.1f);
                P(PrimitiveType.Sphere, r, blush, new Vector3(s * 0.28f, 0.58f, 0.3f), new Vector3(0.12f, 0.06f, 0.05f));
            }
            P(PrimitiveType.Cube, r, black, new Vector3(0f, 0.56f, 0.35f), new Vector3(0.42f, 0.03f, 0.04f));
        }

        private static void Bird(Transform r)
        {
            var feathers = PrototypeMaterials.Create(new Color(0.25f, 0.6f, 1f), 0.4f);
            var crest = PrototypeMaterials.Create(new Color(0.12f, 0.3f, 0.75f), 0.4f);
            var belly = PrototypeMaterials.Create(Color.white, 0.4f);
            var beak = PrototypeMaterials.Create(new Color(1f, 0.65f, 0.1f), 0.5f);
            var black = PrototypeMaterials.Create(new Color(0.05f, 0.05f, 0.05f), 0.9f);

            P(PrimitiveType.Sphere, r, feathers, new Vector3(0f, 0.3f, 0f), new Vector3(0.56f, 0.62f, 0.5f));
            P(PrimitiveType.Sphere, r, belly, new Vector3(0f, 0.28f, 0.16f), new Vector3(0.4f, 0.46f, 0.24f));
            P(PrimitiveType.Sphere, r, feathers, new Vector3(0f, 0.82f, 0f), Vector3.one * 0.52f);
            P(PrimitiveType.Cube, r, beak, new Vector3(0f, 0.78f, 0.3f), new Vector3(0.17f, 0.08f, 0.17f), new Vector3(0f, 45f, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                P(PrimitiveType.Sphere, r, black, new Vector3(s * 0.12f, 0.88f, 0.22f), Vector3.one * 0.09f);
                P(PrimitiveType.Sphere, r, feathers, new Vector3(s * 0.3f, 0.34f, -0.02f), new Vector3(0.12f, 0.36f, 0.3f), new Vector3(0f, 0f, s * 20f));
            }
            for (int i = 0; i < 3; i++)
                P(PrimitiveType.Sphere, r, crest, new Vector3(0f, 1.1f - i * 0.03f, 0.04f - i * 0.1f), new Vector3(0.08f, 0.24f, 0.12f), new Vector3(-25f + i * 25f, 0f, 0f));
        }
    }
}
