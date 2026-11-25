using System;
using System.Collections.Generic;
using BOW.Simulation;

namespace BOW.Meta
{
    /// <summary>Store product IDs (TDD 10.1). The same IDs are used in Google Play Console.</summary>
    public static class Products
    {
        /// <summary>One-time unlock for Elimination and every mode added later (US-8.8).</summary>
        public const string ModesUnlock = "modes_unlock";

        public static readonly string[] All = { ModesUnlock };
    }

    public enum ModeStatus : byte
    {
        /// <summary>Free, or unlocked by purchase.</summary>
        Available,
        /// <summary>Locked, but one solo trial match is left (US-8.9).</summary>
        Trial,
        Locked,
    }

    /// <summary>
    /// What this player owns, and the mode rules built on it (TDD 10.1, 10.3). Engine-free: the game layer
    /// loads and saves it, and the simulation never sees it, so nothing for sale changes play (US-8.2).
    /// </summary>
    public sealed class Entitlements
    {
        private readonly HashSet<string> _owned = new HashSet<string>();
        private readonly HashSet<GameMode> _trialsUsed = new HashSet<GameMode>();

        /// <summary>Raised when something is granted or a trial is used, so menus refresh without a restart (US-8.8).</summary>
        public event Action Changed;

        public IEnumerable<string> Owned => _owned;
        public IEnumerable<GameMode> TrialsUsed => _trialsUsed;

        /// <summary>Timed and Practice are free (US-8.6); every other mode, including future ones, needs the Modes unlock.</summary>
        public static bool IsFree(GameMode mode) => mode == GameMode.Timed || mode == GameMode.Practice;

        public bool Owns(string productId) => _owned.Contains(productId);

        /// <summary>Hosting (or playing solo without a trial) needs the mode to be free or unlocked (US-8.3).</summary>
        public bool CanHost(GameMode mode) => IsFree(mode) || Owns(Products.ModesUnlock);

        /// <summary>One free solo match against AI for each locked mode (US-8.9).</summary>
        public bool CanTrial(GameMode mode) => !CanHost(mode) && !_trialsUsed.Contains(mode);

        public ModeStatus StatusFor(GameMode mode) => CanHost(mode) ? ModeStatus.Available : CanTrial(mode) ? ModeStatus.Trial : ModeStatus.Locked;

        /// <summary>Grants a product. Returns true if it was new.</summary>
        public bool Grant(string productId)
        {
            if (string.IsNullOrEmpty(productId) || !_owned.Add(productId)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Uses up the trial for a locked mode. Returns false if it isn't available.</summary>
        public bool UseTrial(GameMode mode)
        {
            if (!CanTrial(mode)) return false;
            _trialsUsed.Add(mode);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Restores saved state without raising <see cref="Changed"/>.</summary>
        public void Load(IEnumerable<string> owned, IEnumerable<GameMode> trialsUsed)
        {
            _owned.Clear();
            _trialsUsed.Clear();
            if (owned != null) foreach (var id in owned) if (!string.IsNullOrEmpty(id)) _owned.Add(id);
            if (trialsUsed != null) foreach (var m in trialsUsed) _trialsUsed.Add(m);
        }
    }
}
