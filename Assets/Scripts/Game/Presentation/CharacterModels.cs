using System.Collections.Generic;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Real (Meshy) character models, loaded from Resources/Models when they exist; a character without one
    /// keeps its primitive stand-in. Models are scaled to the stand-ins' height and posed with
    /// <see cref="HumanoidPoses"/>, so a seated driver needs no animation clip.
    ///
    /// Meshy's standard rig (Bolt, FBX) already has Unity's humanoid bone names and imports as Humanoid. Its
    /// "Smart Rig" (Mitzi, GLB) numbers its bones instead, so those models carry a bone map here and get their
    /// Humanoid avatar built in code.
    /// </summary>
    public static class CharacterModels
    {
        private sealed class Entry
        {
            public string Path;
            /// <summary>Unity humanoid bone name → the model's bone name; null when the import is already Humanoid.</summary>
            public Dictionary<string, string> Bones;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { Path = "Models/bolt" },
            new Entry { Path = "Models/mitzi", Bones = SmartRigMitzi() },
            new Entry { Path = "Models/hopper", Bones = SmartRigHopper() },
            new Entry { Path = "Models/pip", Bones = SmartRigPip() },
        };

        private static readonly GameObject[] Loaded = new GameObject[Characters.Count];
        private static readonly bool[] Tried = new bool[Characters.Count];
        private static readonly Avatar[] BuiltAvatars = new Avatar[Characters.Count];

        /// <summary>Standing height to match the primitive characters.</summary>
        public const float StandingHeight = 1.3f;
        /// <summary>Drivers are scaled as if this tall standing: big and readable over the cockpit rim from the match camera.</summary>
        public const float DrivingHeight = 2.2f;
        /// <summary>Height of the hip joint above the seat point when sitting.</summary>
        private const float SeatedHipHeight = 0.14f;

        public static bool Has(int character) => Load(character) != null;

        /// <summary>
        /// Adds the character's model under <paramref name="root"/>. Standing: feet on the origin, facing +Z.
        /// Seated: hips just above the origin (the seat), in the driving pose.
        /// </summary>
        public static bool TryBuild(int character, Transform root, bool seated)
        {
            var animator = Spawn(character, root, seated ? DrivingHeight : StandingHeight);
            if (animator == null) return false;
            if (seated && animator.isHuman)
            {
                var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                HumanoidPoses.Apply(ref pose, HumanoidPoses.Motion.Drive, 0f);
                handler.SetHumanPose(ref pose);
                handler.Dispose();
                SeatHips(animator, root);
            }
            animator.enabled = false; // the pose is set by hand; nothing should overwrite it
            return true;
        }

        /// <summary>
        /// Instantiates the model scaled to <see cref="StandingHeight"/> with a Humanoid-ready Animator (no
        /// controller), or returns null if the character has no model. The animation test drives it directly.
        /// </summary>
        public static Animator Spawn(int character, Transform root, float targetHeight = StandingHeight)
        {
            var prefab = Load(character);
            if (prefab == null) return null;

            var model = Object.Instantiate(prefab);
            model.name = "Model";
            model.transform.position = Vector3.zero;
            model.transform.rotation = Quaternion.identity;
            float height = MeasureHeight(model);

            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            if ((animator.avatar == null || !animator.avatar.isHuman) && Entries[character].Bones != null)
            {
                if (BuiltAvatars[character] == null) BuiltAvatars[character] = BuildAvatar(model, Entries[character].Bones);
                animator.avatar = BuiltAvatars[character];
                animator.Rebind();
            }

            model.transform.SetParent(root, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * (height > 0.01f ? targetHeight / height : 1f);

            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = false;
            }
            return animator;
        }

        private static GameObject Load(int character)
        {
            if (character < 0 || character >= Characters.Count || Entries[character] == null) return null;
            if (!Tried[character])
            {
                Tried[character] = true;
                Loaded[character] = Resources.Load<GameObject>(Entries[character].Path);
                if (Loaded[character] == null) Debug.LogWarning($"No model at Resources/{Entries[character].Path}; using the stand-in for {Characters.Names[character]}.");
            }
            return Loaded[character];
        }

        private static float MeasureHeight(GameObject model)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any ? bounds.size.y : 0f;
        }

        /// <summary>Moves the model so its hips sit on the seat point (the root's origin).</summary>
        private static void SeatHips(Animator a, Transform root)
        {
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return;
            Vector3 hipsLocal = root.InverseTransformPoint(hips.position);
            a.transform.localPosition += new Vector3(-hipsLocal.x, SeatedHipHeight - hipsLocal.y, -hipsLocal.z);
        }

        /// <summary>Builds a Humanoid avatar from the model's current (rest) pose and an explicit bone map.</summary>
        private static Avatar BuildAvatar(GameObject model, Dictionary<string, string> bones)
        {
            var human = new List<HumanBone>();
            foreach (var pair in bones)
                human.Add(new HumanBone { humanName = pair.Key, boneName = pair.Value, limit = new HumanLimit { useDefaultValues = true } });

            // Humanoid muscles measure from a T-pose, but Meshy rigs in an A-pose (arms ~45° down). Straighten the
            // arms out sideways while recording the reference skeleton, as the FBX importer's "Enforce T-Pose" does,
            // or every arm motion ends up 45° too low and through the body. The mesh keeps its own pose.
            var transforms = model.GetComponentsInChildren<Transform>(true);
            var saved = new Quaternion[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) saved[i] = transforms[i].localRotation;
            Transform Find(string human) => bones.TryGetValue(human, out var n) ? System.Array.Find(transforms, t => t.name == n) : null;
            var hips = Find("Hips");
            foreach (var side in new[] { "Left", "Right" })
            {
                var upper = Find(side + "UpperArm");
                var lower = Find(side + "LowerArm");
                var hand = Find(side + "Hand");
                if (hips == null || upper == null || lower == null || hand == null) continue;
                var outward = new Vector3(Mathf.Sign(upper.position.x - hips.position.x), 0f, 0f);
                Align(upper, lower.position, outward);
                Align(lower, hand.position, outward);
            }

            var skeleton = new List<SkeletonBone>();
            foreach (var t in transforms)
                skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            for (int i = 0; i < transforms.Length; i++) transforms[i].localRotation = saved[i];

            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(model, description);
            avatar.name = model.name + " Avatar";
            if (!avatar.isValid || !avatar.isHuman) Debug.LogWarning($"Could not build a humanoid avatar for {model.name}.");
            return avatar;
        }

        /// <summary>Turns <paramref name="bone"/> so the direction to <paramref name="childPosition"/> points along <paramref name="direction"/>.</summary>
        private static void Align(Transform bone, Vector3 childPosition, Vector3 direction)
        {
            Vector3 from = childPosition - bone.position;
            if (from.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(from, direction) * bone.rotation;
        }

        /// <summary>
        /// Mitzi's Smart Rig skeleton, mapped by bone position: spine up the middle, arms out to each side
        /// (+X in the file is her left), digitigrade legs, and a tail (Bone_002–007) and ears left unmapped.
        /// </summary>
        private static Dictionary<string, string> SmartRigMitzi() => new Dictionary<string, string>
        {
            { "Hips", "Bone_001" },
            { "Spine", "Bone_010" },
            { "Chest", "Bone_009" },
            { "Neck", "Bone_024" },
            { "Head", "Bone_023" },
            { "LeftShoulder", "Bone_029" }, { "LeftUpperArm", "Bone_028" }, { "LeftLowerArm", "Bone_027" }, { "LeftHand", "Bone_026" },
            { "RightShoulder", "Bone_034" }, { "RightUpperArm", "Bone_033" }, { "RightLowerArm", "Bone_032" }, { "RightHand", "Bone_031" },
            { "LeftUpperLeg", "Bone_016" }, { "LeftLowerLeg", "Bone_015" }, { "LeftFoot", "Bone_014" }, { "LeftToes", "Bone_013" },
            { "RightUpperLeg", "Bone_022" }, { "RightLowerLeg", "Bone_021" }, { "RightFoot", "Bone_020" }, { "RightToes", "Bone_019" },
        };

        /// <summary>Hopper's Smart Rig skeleton (the head is most of his height; Bone_026 is its top).</summary>
        private static Dictionary<string, string> SmartRigHopper() => new Dictionary<string, string>
        {
            { "Hips", "Bone_001" },
            { "Spine", "Bone_005" },
            { "Chest", "Bone_004" },
            { "UpperChest", "Bone_003" },
            { "Neck", "Bone_028" },
            { "Head", "Bone_027" },
            { "LeftShoulder", "Bone_025" }, { "LeftUpperArm", "Bone_024" }, { "LeftLowerArm", "Bone_023" }, { "LeftHand", "Bone_022" },
            { "RightShoulder", "Bone_020" }, { "RightUpperArm", "Bone_019" }, { "RightLowerArm", "Bone_018" }, { "RightHand", "Bone_017" },
            { "LeftUpperLeg", "Bone_015" }, { "LeftLowerLeg", "Bone_014" }, { "LeftFoot", "Bone_013" }, { "LeftToes", "Bone_012" },
            { "RightUpperLeg", "Bone_010" }, { "RightLowerLeg", "Bone_009" }, { "RightFoot", "Bone_008" }, { "RightToes", "Bone_007" },
        };

        /// <summary>Pip's Smart Rig skeleton: wings map to arms; the tail (Bone_014–017) and crest are left unmapped.</summary>
        private static Dictionary<string, string> SmartRigPip() => new Dictionary<string, string>
        {
            { "Hips", "Bone_001" },
            { "Spine", "Bone_003" },
            { "Chest", "Bone_002" },
            { "Neck", "Bone_030" },
            { "Head", "Bone_029" },
            { "LeftShoulder", "Bone_027" }, { "LeftUpperArm", "Bone_026" }, { "LeftLowerArm", "Bone_025" }, { "LeftHand", "Bone_024" },
            { "RightShoulder", "Bone_022" }, { "RightUpperArm", "Bone_021" }, { "RightLowerArm", "Bone_020" }, { "RightHand", "Bone_019" },
            { "LeftUpperLeg", "Bone_008" }, { "LeftLowerLeg", "Bone_007" }, { "LeftFoot", "Bone_006" }, { "LeftToes", "Bone_005" },
            { "RightUpperLeg", "Bone_013" }, { "RightLowerLeg", "Bone_012" }, { "RightFoot", "Bone_011" }, { "RightToes", "Bone_010" },
        };
    }
}
