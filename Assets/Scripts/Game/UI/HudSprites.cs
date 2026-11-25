using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// The HUD's sprites, cut from the UI kit concepts (Docs/Concepts/35-hud-ui-kit-v2.png and 36-blast-button-states-v1.png)
    /// into Resources/UI/Hud.
    /// Each falls back to a plain procedural shape if missing, so the HUD still works without the art.
    /// </summary>
    public static class HudSprites
    {
        private const string Folder = "UI/Hud/";

        /// <summary>Size of the blast core relative to the whole button (from the source art).</summary>
        public const float BlastCoreScale = 0.66f;

        private static Sprite _panel, _portraitRing, _timerFrame, _pause, _pipEmpty, _pipFull, _blastEmpty, _blastFill, _blastCore;

        /// <summary>Slanted player panel with a silver rim; tint it with the seat colour.</summary>
        public static Sprite Panel => Get(ref _panel, "panel", false);
        /// <summary>Silver ring with a cyan inner line, for portraits.</summary>
        public static Sprite PortraitRing => Get(ref _portraitRing, "portrait_ring", true);
        /// <summary>Six-sided frame for the match clock.</summary>
        public static Sprite TimerFrame => Get(ref _timerFrame, "timer_frame", false);
        public static Sprite Pause => Get(ref _pause, "pause", false);
        public static Sprite PipEmpty => Get(ref _pipEmpty, "pip_empty", false);
        public static Sprite PipFull => Get(ref _pipFull, "pip_full", false);
        /// <summary>Blast button with the cooldown ring empty and no starburst (36-blast-button-states-v1.png, left).</summary>
        public static Sprite BlastEmpty => Get(ref _blastEmpty, "blast_empty", true);
        /// <summary>Just the ring's segments, all lit; drawn over the empty button as a radial fill.</summary>
        public static Sprite BlastFill => Get(ref _blastFill, "blast_fill", true);
        /// <summary>The starburst core of the charged button, shown when the blast is ready.</summary>
        public static Sprite BlastCore => Get(ref _blastCore, "blast_core", true);

        /// <summary>Width / height of a sprite, for sizing rects to match the art.</summary>
        public static float Aspect(Sprite sprite) => sprite.rect.width / sprite.rect.height;

        private static Sprite Get(ref Sprite cache, string name, bool round)
        {
            if (cache != null) return cache;
            cache = Resources.Load<Sprite>(Folder + name);
            if (cache == null)
            {
                // Imported as a plain texture (for example if the import settings were reset): wrap it ourselves.
                var texture = Resources.Load<Texture2D>(Folder + name);
                if (texture != null)
                    cache = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            }
            if (cache == null) cache = round ? ProceduralTextures.Circle() : ProceduralTextures.White();
            return cache;
        }
    }
}
