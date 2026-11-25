using System;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Makes the game's placeholder sound effects in code, so matches have audio feedback before real sound
    /// assets exist (TDD 11.1). Each sound is a few layered oscillators and noise with simple envelopes.
    /// Swap any of them for an AudioClip asset later without touching the callers.
    /// </summary>
    public static class SfxSynth
    {
        private const int Rate = 44100;

        public static AudioClip Blast() => Make("Blast", 0.42f, (t, rnd) =>
        {
            // A deep thump with a falling pitch, plus a burst of noise for the shockwave.
            float body = Mathf.Sin(Phase(t, 140f, 45f, 0.25f)) * Env(t, 0.005f, 0.32f);
            float noise = (float)(rnd.NextDouble() * 2.0 - 1.0) * Env(t, 0.002f, 0.18f) * 0.55f;
            return Mathf.Clamp(body * 0.9f + noise, -1f, 1f);
        });

        public static AudioClip PaddleHit() => Make("Paddle hit", 0.12f, (t, rnd) =>
            (Mathf.Sin(2f * Mathf.PI * 520f * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * 1040f * t) * 0.25f) * Env(t, 0.002f, 0.1f));

        public static AudioClip WallBounce() => Make("Wall bounce", 0.07f, (t, rnd) =>
            (Mathf.Sin(2f * Mathf.PI * 300f * t) * 0.5f + (float)(rnd.NextDouble() * 2.0 - 1.0) * 0.25f) * Env(t, 0.001f, 0.06f));

        public static AudioClip BallClack() => Make("Ball clack", 0.06f, (t, rnd) =>
            Mathf.Sin(2f * Mathf.PI * 1500f * t) * Env(t, 0.001f, 0.05f) * 0.6f);

        /// <summary>Someone else conceded: a bright rising two-note chime.</summary>
        public static AudioClip Goal() => Make("Goal", 0.55f, (t, rnd) =>
        {
            float f = t < 0.14f ? 660f : 990f;
            return (Square(t, f) * 0.25f + Mathf.Sin(2f * Mathf.PI * f * t) * 0.4f) * Env(t, 0.005f, 0.5f);
        });

        /// <summary>You conceded: a low falling buzz.</summary>
        public static AudioClip Conceded() => Make("Conceded", 0.6f, (t, rnd) =>
        {
            float buzz = Square(t, Mathf.Lerp(220f, 110f, t / 0.6f)) * 0.3f;
            return buzz * Env(t, 0.005f, 0.55f);
        });

        public static AudioClip CountdownBeep() => Make("Countdown", 0.14f, (t, rnd) =>
            Mathf.Sin(2f * Mathf.PI * 660f * t) * Env(t, 0.003f, 0.13f) * 0.6f);

        public static AudioClip Go() => Make("Go", 0.4f, (t, rnd) =>
            (Mathf.Sin(2f * Mathf.PI * 990f * t) * 0.5f + Square(t, 990f) * 0.15f) * Env(t, 0.003f, 0.38f));

        public static AudioClip Eliminated() => Make("Eliminated", 0.9f, (t, rnd) =>
        {
            int step = Mathf.Min(3, (int)(t / 0.2f));
            float f = 440f * Mathf.Pow(0.84f, step);
            return Square(t, f) * 0.22f * Env(t, 0.005f, 0.88f);
        });

        public static AudioClip Win() => Make("Win", 1.1f, (t, rnd) =>
        {
            float[] notes = { 523f, 659f, 784f, 1047f };
            int i = Mathf.Min(notes.Length - 1, (int)(t / 0.13f));
            float local = t - i * 0.13f;
            float tone = Mathf.Sin(2f * Mathf.PI * notes[i] * t) * 0.45f + Square(t, notes[i]) * 0.12f;
            float tail = i == notes.Length - 1 ? Env(local, 0.003f, 0.7f) : Env(local, 0.003f, 0.14f);
            return tone * tail;
        });

        public static AudioClip Lose() => Make("Lose", 0.9f, (t, rnd) =>
        {
            float[] notes = { 392f, 330f, 262f };
            int i = Mathf.Min(notes.Length - 1, (int)(t / 0.2f));
            float local = t - i * 0.2f;
            return Mathf.Sin(2f * Mathf.PI * notes[i] * t) * 0.45f * (i == notes.Length - 1 ? Env(local, 0.003f, 0.5f) : Env(local, 0.003f, 0.2f));
        });

        public static AudioClip Click() => Make("Click", 0.04f, (t, rnd) =>
            Mathf.Sin(2f * Mathf.PI * 1800f * t) * Env(t, 0.001f, 0.035f) * 0.35f);

        // ------------------------------------------------------------------ helpers

        private static AudioClip Make(string name, float seconds, Func<float, System.Random, float> sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rnd = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++) data[i] = sample(i / (float)Rate, rnd);
            // Short fade at the end so no clip ends on a click.
            int fade = Mathf.Min(n, Rate / 200);
            for (int i = 0; i < fade; i++) data[n - 1 - i] *= i / (float)fade;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Attack-decay envelope: rises over <paramref name="attack"/> then falls to zero by <paramref name="length"/>.</summary>
        private static float Env(float t, float attack, float length)
        {
            if (t < attack) return t / attack;
            if (t >= length) return 0f;
            float d = 1f - (t - attack) / (length - attack);
            return d * d;
        }

        private static float Square(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t) >= 0f ? 1f : -1f;

        /// <summary>Phase of a sine whose frequency glides linearly from f0 to f1 over <paramref name="glide"/> seconds.</summary>
        private static float Phase(float t, float f0, float f1, float glide)
        {
            if (t < glide) return 2f * Mathf.PI * (f0 * t + (f1 - f0) * t * t / (2f * glide));
            float atGlide = f0 * glide + (f1 - f0) * glide / 2f;
            return 2f * Mathf.PI * (atGlide + f1 * (t - glide));
        }
    }
}
