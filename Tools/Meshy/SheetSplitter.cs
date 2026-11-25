// Splits concept-art sheets (white or transparent background) into one image per object,
// so they can be sent to Meshy as clean single views. Loaded by SplitSheet.ps1 via Add-Type.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class SheetSplitter
{
    private static int _w, _h;
    private static byte[] _px; // BGRA

    private static void Load(string path)
    {
        using (var b = new Bitmap(path))
        {
            _w = b.Width; _h = b.Height;
            var d = b.LockBits(new Rectangle(0, 0, _w, _h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            _px = new byte[_w * _h * 4];
            Marshal.Copy(d.Scan0, _px, 0, _px.Length);
            b.UnlockBits(d);
        }
    }

    /// True where the pixel is part of the artwork rather than the (near-white or transparent) background.
    private static bool Ink(int x, int y)
    {
        int i = (y * _w + x) * 4;
        if (_px[i + 3] < 40) return false;
        int b = _px[i], g = _px[i + 1], r = _px[i + 2];
        int min = Math.Min(r, Math.Min(g, b)), max = Math.Max(r, Math.Max(g, b));
        return min < 205 || max - min > 30;
    }

    /// Turnaround sheets: figures standing in a row on a ground line, labels and swatches below it.
    /// Writes one square PNG per figure (left to right) and returns their paths.
    public static List<string> SplitTurnaround(string path, string outPrefix, int size)
    {
        Load(path);
        // Ground line: the first row below the middle where ink spans nearly the whole width.
        int ground = _h - 1;
        for (int y = _h / 2; y < _h; y++)
        {
            int n = 0;
            for (int x = 0; x < _w; x++) if (Ink(x, y)) n++;
            if (n > _w * 0.85) { ground = y; break; }
        }
        int top = (int)(_h * 0.10), bottom = ground - 3;

        // Column runs that contain figure ink.
        var cols = new int[_w];
        for (int x = 0; x < _w; x++)
            for (int y = top; y < bottom; y++) if (Ink(x, y)) cols[x]++;
        var runs = new List<int[]>();
        int start = -1;
        for (int x = 0; x <= _w; x++)
        {
            bool on = x < _w && cols[x] > 4;
            if (on && start < 0) start = x;
            if (!on && start >= 0)
            {
                if (runs.Count > 0 && start - runs[runs.Count - 1][1] < 10) runs[runs.Count - 1][1] = x;
                else runs.Add(new[] { start, x });
                start = -1;
            }
        }
        runs.RemoveAll(r => r[1] - r[0] < _w * 0.04);

        var outputs = new List<string>();
        int k = 0;
        foreach (var r in runs)
        {
            int minY = bottom, maxY = top;
            for (int x = r[0]; x < r[1]; x++)
                for (int y = top; y < bottom; y++)
                    if (Ink(x, y)) { if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (maxY - minY < _h * 0.2) continue; // a title or label, not a figure
            string outPath = outPrefix + "-" + (++k) + ".png";
            WriteSquare(path, new Rectangle(r[0], minY, r[1] - r[0], maxY - minY + 1), outPath, size);
            outputs.Add(outPath);
        }
        return outputs;
    }

    /// Any sheet: finds separate objects (ink regions after joining parts closer than `join` pixels),
    /// keeps those at least `minFraction` of the image's height, and writes them in reading order.
    public static List<string> SplitObjects(string path, string outPrefix, int size, int join, double minFraction)
    {
        Load(path);
        // Work on a coarse grid so nearby parts (an antenna, a floating ball) join their object.
        int cell = Math.Max(2, join / 2);
        int gw = (_w + cell - 1) / cell, gh = (_h + cell - 1) / cell;
        var grid = new bool[gw * gh];
        for (int y = 0; y < _h; y++) for (int x = 0; x < _w; x++) if (Ink(x, y)) grid[(y / cell) * gw + x / cell] = true;
        // Dilate by one cell.
        var dil = new bool[gw * gh];
        for (int y = 0; y < gh; y++) for (int x = 0; x < gw; x++)
        {
            if (!grid[y * gw + x]) continue;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < gw && ny < gh) dil[ny * gw + nx] = true;
            }
        }
        var seen = new bool[gw * gh];
        var boxes = new List<Rectangle>();
        var stack = new Stack<int>();
        for (int i = 0; i < gw * gh; i++)
        {
            if (!dil[i] || seen[i]) continue;
            int minX = gw, minY = gh, maxX = 0, maxY = 0;
            stack.Push(i); seen[i] = true;
            while (stack.Count > 0)
            {
                int p = stack.Pop(), x = p % gw, y = p / gw;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) continue;
                    int n = ny * gw + nx;
                    if (dil[n] && !seen[n]) { seen[n] = true; stack.Push(n); }
                }
            }
            var box = Rectangle.FromLTRB(minX * cell, minY * cell, Math.Min(_w, (maxX + 1) * cell), Math.Min(_h, (maxY + 1) * cell));
            if (box.Height >= _h * minFraction) boxes.Add(box);
        }
        // Reading order: rows (by vertical centre, banded), then left to right.
        boxes.Sort((a, b) =>
        {
            int ra = (a.Top + a.Height / 2) / (_h / 4), rb = (b.Top + b.Height / 2) / (_h / 4);
            return ra != rb ? ra.CompareTo(rb) : a.Left.CompareTo(b.Left);
        });
        var outputs = new List<string>();
        for (int i = 0; i < boxes.Count; i++)
        {
            string outPath = outPrefix + "-" + (i + 1) + ".png";
            WriteSquare(path, TightBox(boxes[i]), outPath, size);
            outputs.Add(outPath);
        }
        return outputs;
    }

    /// Shrinks a region to its main artwork: the union of pixel-level ink components at least 3% the size of the biggest,
    /// which drops the letters of nearby labels.
    private static Rectangle TightBox(Rectangle region)
    {
        int rw = region.Width, rh = region.Height;
        var label = new int[rw * rh];
        var comps = new List<int[]>(); // minX, minY, maxX, maxY, count
        var stack = new Stack<int>();
        for (int i = 0; i < rw * rh; i++)
        {
            if (label[i] != 0 || !Ink(region.Left + i % rw, region.Top + i / rw)) continue;
            var c = new[] { rw, rh, 0, 0, 0 };
            comps.Add(c);
            stack.Push(i); label[i] = comps.Count;
            while (stack.Count > 0)
            {
                int p = stack.Pop(), x = p % rw, y = p / rw;
                c[0] = Math.Min(c[0], x); c[1] = Math.Min(c[1], y); c[2] = Math.Max(c[2], x); c[3] = Math.Max(c[3], y); c[4]++;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= rw || ny >= rh) continue;
                    int n = ny * rw + nx;
                    if (label[n] == 0 && Ink(region.Left + nx, region.Top + ny)) { label[n] = comps.Count; stack.Push(n); }
                }
            }
        }
        int biggest = 0;
        foreach (var c in comps) biggest = Math.Max(biggest, c[4]);
        int l = rw, t = rh, r = 0, b = 0;
        foreach (var c in comps)
        {
            if (c[4] < biggest * 0.03) continue;
            l = Math.Min(l, c[0]); t = Math.Min(t, c[1]); r = Math.Max(r, c[2]); b = Math.Max(b, c[3]);
        }
        return r < l ? region : Rectangle.FromLTRB(region.Left + l, region.Top + t, region.Left + r + 1, region.Top + b + 1);
    }

    /// Copies a region, centred on a white square canvas with a margin, scaled to size x size.
    private static void WriteSquare(string sourcePath, Rectangle region, string outPath, int size)
    {
        using (var src = new Bitmap(sourcePath))
        using (var dst = new Bitmap(size, size, PixelFormat.Format24bppRgb))
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.White);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            double scale = size * 0.86 / Math.Max(region.Width, region.Height);
            int w = (int)(region.Width * scale), h = (int)(region.Height * scale);
            g.DrawImage(src, new Rectangle((size - w) / 2, (size - h) / 2, w, h), region, GraphicsUnit.Pixel);
            dst.Save(outPath, ImageFormat.Png);
        }
    }
}

