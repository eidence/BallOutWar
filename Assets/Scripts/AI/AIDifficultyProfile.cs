using System;
using BOW.Simulation;

namespace BOW.AI
{
    public enum AIDifficulty : byte
    {
        Easy,
        Medium,
        Hard,
        Expert,
    }

    /// <summary>
    /// Tunable numbers behind one difficulty level (TDD 7.4). Difficulty only changes perception,
    /// reaction and decisions; the AI always plays with the same paddle and blast as a human (US-4.2).
    /// </summary>
    [Serializable]
    public sealed class AIDifficultyProfile
    {
        /// <summary>How old the AI's view of the balls is, in seconds.</summary>
        public float ReactionDelay;
        /// <summary>Standard deviation of the aim error, in arena units.</summary>
        public float AimErrorSigma;
        /// <summary>How many balls the AI keeps track of (nearest first); 0 = all.</summary>
        public int ThreatsTracked;
        /// <summary>Chance a defensive blast is mistimed and skipped.</summary>
        public float MistimeChance;
        /// <summary>Blasts balls at opponents' goals (Hard and up).</summary>
        public bool OffensiveBlasts;
        /// <summary>Offensive blasts aim away from the target goal's paddle (Expert).</summary>
        public bool AimsForOpenSpace;
        /// <summary>Predicts one wall bounce when working out where a ball will arrive (Expert).</summary>
        public bool PredictsBounces;

        public static AIDifficultyProfile For(AIDifficulty level)
        {
            switch (level)
            {
                case AIDifficulty.Easy:
                    return new AIDifficultyProfile { ReactionDelay = 0.40f, AimErrorSigma = 1.6f, ThreatsTracked = 1, MistimeChance = 0.35f };
                case AIDifficulty.Medium:
                    return new AIDifficultyProfile { ReactionDelay = 0.25f, AimErrorSigma = 0.9f, ThreatsTracked = 2 };
                case AIDifficulty.Hard:
                    return new AIDifficultyProfile { ReactionDelay = 0.15f, AimErrorSigma = 0.4f, ThreatsTracked = 0, OffensiveBlasts = true };
                case AIDifficulty.Expert:
                    return new AIDifficultyProfile
                    {
                        ReactionDelay = 0.08f, AimErrorSigma = 0.15f, ThreatsTracked = 0,
                        OffensiveBlasts = true, AimsForOpenSpace = true, PredictsBounces = true,
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }

        internal int DelayTicks(GameplayConfig cfg) => Math.Max(0, (int)Math.Round(ReactionDelay * cfg.TickRate));
    }
}
