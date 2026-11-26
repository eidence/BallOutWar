using System.Collections.Generic;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// A bumper-car tub generated in code until the real model exists: a fat rubber bumper ring, a painted body
    /// that rises from a low hood at the front to a tall back, and an open cockpit the driver sits in.
    /// Built by lofting rounded-rectangle rings, so it stays smooth without any imported art.
    /// Local space: +X along the goal line, +Z forward (into the arena), +Y up. Submeshes: 0 paint, 1 rubber, 2 interior.
    /// </summary>
    public static class BumperCarMesh
    {
        public const int PaintSubmesh = 0, RubberSubmesh = 1, InteriorSubmesh = 2;
        private const int Segments = 64;
        private const float Roundness = 3.2f; // superellipse exponent: 2 = ellipse, higher = squarer

        public readonly struct Layout
        {
            public readonly float HalfWidth, HalfDepth, CentreZ;
            public readonly float CockpitHalfWidth, CockpitHalfDepth, CockpitZ;
            public readonly float FloorY, FrontRimY, BackRimY;

            public Layout(float width, float front, float depth)
            {
                HalfWidth = width * 0.5f;
                HalfDepth = depth * 0.5f;
                CentreZ = front - HalfDepth;
                CockpitHalfWidth = HalfWidth - 0.5f;
                CockpitHalfDepth = 0.55f;
                CockpitZ = CentreZ - 0.2f;
                FloorY = 0.45f;
                FrontRimY = 0.82f;
                BackRimY = 1.5f;
            }

            public float FrontZ => CentreZ + HalfDepth;
            public float BackZ => CentreZ - HalfDepth;
        }

        private static readonly Dictionary<long, Mesh> Cache = new Dictionary<long, Mesh>();

        public static Mesh Get(Layout l)
        {
            long key = ((long)Mathf.RoundToInt(l.HalfWidth * 1000f) << 32) | (uint)Mathf.RoundToInt(l.HalfDepth * 1000f);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var mesh = Build(l);
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>A point on the outline of the outer body, grown (d &gt; 0) or shrunk (d &lt; 0) by d.</summary>
        public static Vector3 Outer(in Layout l, float t, float d, float y)
        {
            var p = Superellipse(t, l.HalfWidth + d, l.HalfDepth + d);
            return new Vector3(p.x, y, l.CentreZ + p.y);
        }

        /// <summary>Height of the body's top rim at angle <paramref name="t"/>: low over the hood, tall at the back.</summary>
        public static float Rim(in Layout l, float t)
        {
            float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.15f, 0.85f, -Mathf.Sin(t)));
            return Mathf.Lerp(l.FrontRimY, l.BackRimY, back);
        }

        private static Vector2 Superellipse(float t, float a, float b)
        {
            float c = Mathf.Cos(t), s = Mathf.Sin(t);
            float e = 2f / Roundness;
            return new Vector2(a * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e), b * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e));
        }

        private delegate Vector3 Ring(float t);

        private static Mesh Build(Layout l)
        {
            var verts = new List<Vector3>();
            var tris = new[] { new List<int>(), new List<int>(), new List<int>() };

            Vector3 Cockpit(float t, float d, float y)
            {
                var p = Superellipse(t, l.CockpitHalfWidth + d, l.CockpitHalfDepth + d);
                return new Vector3(p.x, y, l.CockpitZ + p.y);
            }

            // Rubber bumper: a fat tyre-like ring bulging out round the base.
            Loft(verts, tris[RubberSubmesh],
                t => Outer(l, t, -0.06f, 0.03f),
                t => Outer(l, t, 0.10f, 0.12f),
                t => Outer(l, t, 0.15f, 0.25f),
                t => Outer(l, t, 0.10f, 0.38f),
                t => Outer(l, t, -0.06f, 0.44f));

            // Painted body: wall rising to a rounded rim, then the deck sweeping in to the cockpit edge.
            Loft(verts, tris[PaintSubmesh],
                t => Outer(l, t, -0.06f, 0.44f),
                t => Outer(l, t, -0.14f, 0.52f),
                t => Outer(l, t, -0.2f, Rim(l, t) - 0.12f),
                t => Outer(l, t, -0.26f, Rim(l, t) - 0.02f),
                t => Outer(l, t, -0.34f, Rim(l, t)),
                t => Cockpit(t, 0.06f, Rim(l, t) - 0.02f),
                t => Cockpit(t, 0f, Rim(l, t) - 0.08f));

            // Cockpit: inner walls down to the floor (the tall back wall doubles as the seat back).
            var floorCentre = new Vector3(0f, l.FloorY, l.CockpitZ);
            Loft(verts, tris[InteriorSubmesh],
                t => Cockpit(t, 0f, Rim(l, t) - 0.08f),
                t => Cockpit(t, -0.04f, l.FloorY),
                t => floorCentre);

            var mesh = new Mesh { name = "Bumper Car" };
            mesh.SetVertices(verts);
            mesh.subMeshCount = 3;
            for (int i = 0; i < 3; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Joins consecutive rings with quads. Each ring runs anticlockwise seen from above; faces point to the
        /// left of the direction of travel from one ring to the next (outward for a rising wall, up for a deck
        /// moving inward, into the cockpit for a falling inner wall).
        /// </summary>
        private static void Loft(List<Vector3> verts, List<int> tris, params Ring[] rings)
        {
            int start = verts.Count;
            int stride = Segments;
            foreach (var ring in rings)
                for (int k = 0; k < Segments; k++)
                    verts.Add(ring(k * Mathf.PI * 2f / Segments));

            for (int r = 0; r < rings.Length - 1; r++)
            for (int k = 0; k < Segments; k++)
            {
                int a0 = start + r * stride + k, a1 = start + r * stride + (k + 1) % Segments;
                int b0 = a0 + stride, b1 = a1 + stride;
                tris.Add(a0); tris.Add(b0); tris.Add(a1);
                tris.Add(a1); tris.Add(b0); tris.Add(b1);
            }
        }
    }
}
