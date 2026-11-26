using System;

namespace BOW.Game
{
    public enum MenuIcon { Play, Local, Online, Store, Settings, Exit, Back, Next }

    /// <summary>
    /// Pixel art for the menu, drawn from signed distance fields so it stays crisp at any size: button bodies,
    /// rims and glows (made to be 9-sliced) and the menu icons. Pure maths with no Unity types, so it can be
    /// previewed outside the editor; <see cref="MenuStyle"/> turns the results into sprites.
    /// All arrays are row-major, bottom row first (Unity's texture order).
    /// </summary>
    public static class MenuArt
    {
        /// <summary>Coverage (0..1) of an icon drawn white on a transparent square.</summary>
        public static float[] Icon(MenuIcon icon, int size)
        {
            var alpha = new float[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Icon space: -1..1, y up. Distances come back in icon units; convert to pixels to anti-alias.
                float px = (x + 0.5f - half) / half, py = (y + 0.5f - half) / half;
                float d = IconDistance(icon, px, py) * half;
                alpha[y * size + x] = Clamp01(0.5f - d);
            }
            return alpha;
        }

        /// <summary>
        /// A rounded rectangle filling the square: <paramref name="fill"/> is coverage, <paramref name="shade"/> a
        /// top-lit gradient with a glossy upper half (multiply it into the tint), <paramref name="rim"/> the edge line.
        /// </summary>
        public static void Body(int size, float radius, float rimWidth, out float[] fill, out float[] shade, out float[] rim)
        {
            fill = new float[size * size];
            shade = new float[size * size];
            rim = new float[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundBox(x + 0.5f - half, y + 0.5f - half, half - 1f, half - 1f, radius);
                int i = y * size + x;
                fill[i] = Clamp01(0.5f - d);
                rim[i] = Clamp01(0.5f - (Math.Abs(d + rimWidth * 0.5f) - rimWidth * 0.5f));
                float v = (y + 0.5f) / size; // 0 bottom, 1 top
                float s = 0.62f + 0.38f * v;
                if (v > 0.52f) s += 0.10f;          // gloss band on the upper half
                if (d > -rimWidth - 3f) s += 0.12f;  // a lighter inner bevel just inside the rim
                shade[i] = Clamp01(s);
            }
        }

        /// <summary>
        /// Soft glow around a rounded rectangle inset by <paramref name="spread"/> pixels on every side: full strength
        /// inside, fading to nothing at the texture's edge.
        /// </summary>
        public static float[] Glow(int size, int spread, float radius)
        {
            var alpha = new float[size * size];
            float half = size * 0.5f, inner = half - spread;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundBox(x + 0.5f - half, y + 0.5f - half, inner, inner, radius);
                float t = d <= 0f ? 1f : Clamp01(1f - d / spread);
                alpha[y * size + x] = t * t * t;
            }
            return alpha;
        }

        /// <summary>Horizontal fade: opaque at the left edge, gone at the right (eased so there is no visible edge).</summary>
        public static float[] FadeRight(int width)
        {
            var alpha = new float[width];
            for (int x = 0; x < width; x++)
            {
                float t = 1f - (x + 0.5f) / width;
                alpha[x] = t * t * (3f - 2f * t);
            }
            return alpha;
        }

        // ----- Icons -----

        private const float Line = 0.085f; // half-width of icon strokes

        private static float IconDistance(MenuIcon icon, float x, float y)
        {
            switch (icon)
            {
                case MenuIcon.Play:
                    // A triangle with softened corners.
                    return Polygon(x, y, new[] { -0.42f, 0.62f, -0.42f, -0.62f, 0.66f, 0f }) - 0.1f;

                case MenuIcon.Local:
                {
                    // Two players: one in front, one behind and to the right with a gap cut around the front one.
                    float front = Math.Min(Circle(x + 0.24f, y - 0.30f, 0.27f), RoundBox(x + 0.24f, y + 0.52f, 0.46f, 0.30f, 0.28f));
                    float back = Math.Min(Circle(x - 0.36f, y - 0.42f, 0.22f), RoundBox(x - 0.42f, y + 0.30f, 0.38f, 0.28f, 0.26f));
                    back = Math.Max(back, -(front - 0.1f));
                    return Math.Min(front, back);
                }

                case MenuIcon.Online:
                {
                    // A globe: outline, one meridian, the equator and two latitude lines, all inside the outline.
                    const float r = 0.78f;
                    float outline = Math.Abs(Circle(x, y, r)) - Line;
                    float meridian = Math.Abs(Ellipse(x, y, 0.34f, r)) - Line * 0.85f;
                    float equator = Segment(x, y, -r, 0f, r, 0f) - Line * 0.85f;
                    float lat = Math.Min(Segment(x, y, -0.64f, 0.42f, 0.64f, 0.42f), Segment(x, y, -0.64f, -0.42f, 0.64f, -0.42f)) - Line * 0.85f;
                    float inner = Math.Max(Math.Min(meridian, Math.Min(equator, lat)), Circle(x, y, r));
                    return Math.Min(outline, inner);
                }

                case MenuIcon.Store:
                {
                    // A shopping cart: handle, basket, base rail and two wheels.
                    float handle = Math.Min(Segment(x, y, -0.92f, 0.62f, -0.68f, 0.62f), Segment(x, y, -0.68f, 0.62f, -0.42f, -0.30f)) - Line;
                    float basket = Polygon(x, y, new[] { -0.56f, 0.40f, 0.86f, 0.40f, 0.66f, -0.16f, -0.40f, -0.16f }) - 0.04f;
                    float rail = Segment(x, y, -0.42f, -0.34f, 0.62f, -0.34f) - Line;
                    float wheels = Math.Min(Circle(x + 0.28f, y + 0.66f, 0.15f), Circle(x - 0.48f, y + 0.66f, 0.15f));
                    return Math.Min(Math.Min(handle, basket), Math.Min(rail, wheels));
                }

                case MenuIcon.Settings:
                {
                    // A gear: eight teeth around a ring with a hole.
                    float len = (float)Math.Sqrt(x * x + y * y);
                    float angle = (float)Math.Atan2(y, x);
                    float tooth = Clamp01(((float)Math.Cos(angle * 8f) + 0.1f) * 3f);
                    float outer = len - (0.60f + 0.24f * tooth);
                    return Math.Max(outer * 0.8f, -(len - 0.27f));
                }

                case MenuIcon.Exit:
                {
                    // A power symbol: a ring open at the top, with a bar through the gap.
                    const float r = 0.6f;
                    float ring = Math.Abs(Circle(x, y, r)) - Line * 1.1f;
                    // Cut a 70-degree wedge out of the top.
                    float s = (float)Math.Sin(35f * Math.PI / 180f), c = (float)Math.Cos(35f * Math.PI / 180f);
                    float wedge = Math.Max(c * x - s * y, -c * x - s * y); // negative inside the wedge
                    ring = Math.Max(ring, -wedge);
                    float ends = Math.Min(Circle(x - s * r, y - c * r, Line * 1.1f), Circle(x + s * r, y - c * r, Line * 1.1f));
                    float bar = Segment(x, y, 0f, 0.02f, 0f, 0.86f) - Line * 1.15f;
                    return Math.Min(Math.Min(ring, ends), bar);
                }
                case MenuIcon.Back:
                    return Math.Min(Segment(x, y, 0.22f, 0.62f, -0.34f, 0f), Segment(x, y, -0.34f, 0f, 0.22f, -0.62f)) - Line * 1.4f;

                case MenuIcon.Next:
                    return Math.Min(Segment(x, y, -0.22f, 0.62f, 0.34f, 0f), Segment(x, y, 0.34f, 0f, -0.22f, -0.62f)) - Line * 1.4f;
            }
            return 1f;
        }

        // ----- Distance functions (negative inside) -----

        private static float Circle(float x, float y, float r) => (float)Math.Sqrt(x * x + y * y) - r;

        /// <summary>Rounded box centred on the origin with half-size (hx, hy) and corner radius r.</summary>
        private static float RoundBox(float x, float y, float hx, float hy, float r)
        {
            r = Math.Min(r, Math.Min(hx, hy));
            float qx = Math.Abs(x) - hx + r, qy = Math.Abs(y) - hy + r;
            float ox = Math.Max(qx, 0f), oy = Math.Max(qy, 0f);
            return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0f) - r;
        }

        private static float Segment(float x, float y, float ax, float ay, float bx, float by)
        {
            float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
            float h = Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
            float dx = pax - bax * h, dy = pay - bay * h;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Approximate distance to an axis-aligned ellipse (good enough for thin outlines).</summary>
        private static float Ellipse(float x, float y, float rx, float ry)
        {
            float k = (float)Math.Sqrt((x / rx) * (x / rx) + (y / ry) * (y / ry));
            if (k < 1e-5f) return -Math.Min(rx, ry);
            float gx = x / (rx * rx), gy = y / (ry * ry);
            float g = (float)Math.Sqrt(gx * gx + gy * gy) / k;
            return (k - 1f) / Math.Max(g, 1e-5f);
        }

        /// <summary>Distance to a polygon given as x0, y0, x1, y1, ... (any winding).</summary>
        private static float Polygon(float x, float y, float[] v)
        {
            int n = v.Length / 2;
            float d = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i, i++)
            {
                float ax = v[j * 2], ay = v[j * 2 + 1], bx = v[i * 2], by = v[i * 2 + 1];
                d = Math.Min(d, Segment(x, y, ax, ay, bx, by));
                if ((by > y) != (ay > y) && x < (ax - bx) * (y - by) / (ay - by) + bx) inside = !inside;
            }
            return inside ? -d : d;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
