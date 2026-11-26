using System.Collections.Generic;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Standard humanoid motions written as Mecanim muscle values, so they play on any rig Unity understands as
    /// Humanoid, whatever its bone names or axes, exactly as an imported animation clip would. Used for the
    /// in-car driving pose and by the character animation test.
    /// </summary>
    public static class HumanoidPoses
    {
        public enum Motion { Idle, Walk, Run, Wave, Cheer, Drive, Hit, Jump }

        public static readonly Motion[] All = { Motion.Idle, Motion.Walk, Motion.Run, Motion.Wave, Motion.Cheer, Motion.Drive, Motion.Hit, Motion.Jump };

        private static Dictionary<string, int> _muscles;

        /// <summary>Sets one muscle by its Unity name (e.g. "Left Arm Down-Up"); values run -1 to 1.</summary>
        public static void Set(ref HumanPose pose, string muscle, float value)
        {
            if (_muscles == null)
            {
                _muscles = new Dictionary<string, int>();
                for (int i = 0; i < HumanTrait.MuscleCount; i++) _muscles[HumanTrait.MuscleName[i]] = i;
            }
            if (_muscles.TryGetValue(muscle, out int index)) pose.muscles[index] = Mathf.Clamp(value, -1f, 1f);
        }

        private static void Both(ref HumanPose pose, string muscle, float value)
        {
            Set(ref pose, "Left " + muscle, value);
            Set(ref pose, "Right " + muscle, value);
        }

        /// <summary>
        /// Writes <paramref name="motion"/> at time <paramref name="t"/> (seconds) into the pose's muscles and returns
        /// how far to lift the body (metres, for hops and bobs). Starts from a relaxed standing pose each call.
        /// </summary>
        public static float Apply(ref HumanPose pose, Motion motion, float t)
        {
            for (int i = 0; i < pose.muscles.Length; i++) pose.muscles[i] = 0f;
            // Relaxed stance: arms down by the sides, elbows and knees soft.
            Both(ref pose, "Arm Down-Up", -0.45f);
            Both(ref pose, "Forearm Stretch", 0.85f);
            Both(ref pose, "Lower Leg Stretch", 0.95f);
            Both(ref pose, "Upper Leg In-Out", 0f);
            float lift = 0f;

            switch (motion)
            {
                case Motion.Idle:
                {
                    float breathe = Mathf.Sin(t * 2f);
                    Set(ref pose, "Spine Front-Back", 0.04f * breathe);
                    Set(ref pose, "Chest Front-Back", 0.05f * breathe);
                    Set(ref pose, "Head Turn Left-Right", 0.25f * Mathf.Sin(t * 0.7f));
                    Both(ref pose, "Arm Down-Up", -0.45f + 0.03f * breathe);
                    break;
                }
                case Motion.Walk:
                case Motion.Run:
                {
                    bool run = motion == Motion.Run;
                    float speed = run ? 9f : 5.5f;
                    float swing = run ? 0.75f : 0.45f;
                    float s = Mathf.Sin(t * speed), c = Mathf.Cos(t * speed);
                    Set(ref pose, "Left Upper Leg Front-Back", swing * s);
                    Set(ref pose, "Right Upper Leg Front-Back", -swing * s);
                    // Knees bend while the leg swings forward.
                    Set(ref pose, "Left Lower Leg Stretch", 0.95f - (run ? 1.3f : 0.8f) * Mathf.Max(0f, c));
                    Set(ref pose, "Right Lower Leg Stretch", 0.95f - (run ? 1.3f : 0.8f) * Mathf.Max(0f, -c));
                    Set(ref pose, "Left Arm Front-Back", -swing * 0.8f * s);
                    Set(ref pose, "Right Arm Front-Back", swing * 0.8f * s);
                    if (run) Both(ref pose, "Forearm Stretch", 0.1f);
                    Set(ref pose, "Spine Front-Back", run ? 0.25f : 0.06f);
                    Set(ref pose, "Spine Twist Left-Right", 0.12f * s);
                    lift = Mathf.Abs(s) * (run ? 0.05f : 0.02f);
                    break;
                }
                case Motion.Wave:
                {
                    Set(ref pose, "Right Arm Down-Up", 0.9f);
                    Set(ref pose, "Right Arm Front-Back", 0.3f);
                    Set(ref pose, "Right Forearm Stretch", 0.2f);
                    Set(ref pose, "Right Forearm Twist In-Out", 0.4f * Mathf.Sin(t * 8f));
                    Set(ref pose, "Right Hand In-Out", 0.6f * Mathf.Sin(t * 8f));
                    Set(ref pose, "Head Tilt Left-Right", 0.2f);
                    break;
                }
                case Motion.Cheer:
                {
                    float pump = Mathf.Sin(t * 7f);
                    Both(ref pose, "Arm Down-Up", 0.75f + 0.2f * pump);
                    Both(ref pose, "Arm Front-Back", 0.3f);
                    Both(ref pose, "Forearm Stretch", 0.4f + 0.4f * pump);
                    Set(ref pose, "Spine Front-Back", -0.15f);
                    Set(ref pose, "Head Nod Down-Up", -0.3f);
                    lift = 0.05f * Mathf.Max(0f, pump);
                    break;
                }
                case Motion.Drive:
                {
                    // Seated: thighs forward, knees bent, hands out on the wheel, steering side to side.
                    Both(ref pose, "Upper Leg Front-Back", 1f);
                    Both(ref pose, "Lower Leg Stretch", -0.9f);
                    Both(ref pose, "Upper Leg In-Out", 0.2f);
                    float steer = 0.25f * Mathf.Sin(t * 1.6f);
                    Both(ref pose, "Arm Down-Up", -0.25f);
                    Set(ref pose, "Left Arm Front-Back", 0.75f + steer);
                    Set(ref pose, "Right Arm Front-Back", 0.75f - steer);
                    Both(ref pose, "Forearm Stretch", 0.2f);
                    Both(ref pose, "Shoulder Front-Back", 0.3f);
                    Set(ref pose, "Spine Front-Back", 0.1f);
                    Set(ref pose, "Spine Left-Right", -0.2f * steer);
                    Set(ref pose, "Head Turn Left-Right", -0.6f * steer);
                    break;
                }
                case Motion.Hit:
                {
                    // A knock-back every 1.5 s that settles quickly.
                    float since = t % 1.5f;
                    float k = Mathf.Exp(-since * 5f);
                    Set(ref pose, "Spine Front-Back", -0.5f * k);
                    Set(ref pose, "Chest Front-Back", -0.4f * k);
                    Set(ref pose, "Head Nod Down-Up", -0.6f * k);
                    Both(ref pose, "Arm Down-Up", -0.45f + 0.9f * k);
                    Both(ref pose, "Arm Front-Back", 0.5f * k);
                    Both(ref pose, "Lower Leg Stretch", 0.95f - 0.6f * k);
                    break;
                }
                case Motion.Jump:
                {
                    float phase = t % 1.2f / 1.2f;
                    float air = Mathf.Sin(phase * Mathf.PI);
                    float crouch = 1f - air;
                    Both(ref pose, "Upper Leg Front-Back", 0.4f * crouch);
                    Both(ref pose, "Lower Leg Stretch", 0.95f - 0.9f * crouch);
                    Both(ref pose, "Arm Down-Up", -0.45f + 1.2f * air);
                    Set(ref pose, "Spine Front-Back", 0.2f * crouch);
                    lift = 0.35f * air * air;
                    break;
                }
            }
            return lift;
        }
    }
}
