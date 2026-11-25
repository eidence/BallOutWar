using System;

namespace BOW.Simulation
{
    /// <summary>
    /// One seat's input for one tick (TDD 4.1). Humans, AI and network peers all produce this,
    /// which keeps every seat on identical rules (US-4.2).
    /// </summary>
    [Serializable]
    public struct PlayerInput
    {
        /// <summary>Analog movement, -1 (left) to 1 (right). Ignored when <see cref="HasTarget"/> is set.</summary>
        public float Move;

        /// <summary>When true, the paddle steers toward <see cref="Target"/> (drag control and AI).</summary>
        public bool HasTarget;

        /// <summary>Target position along the goal line, in arena units (+ = player's right).</summary>
        public float Target;

        /// <summary>True only on the tick the blast button went down.</summary>
        public bool BlastPressed;

        /// <summary>Input sequence number, used by network prediction and acknowledgement.</summary>
        public uint Sequence;

        public static PlayerInput None => default;

        public static PlayerInput Analog(float move, bool blast = false)
            => new PlayerInput { Move = move, BlastPressed = blast };

        public static PlayerInput ToTarget(float target, bool blast = false)
            => new PlayerInput { HasTarget = true, Target = target, BlastPressed = blast };
    }
}
