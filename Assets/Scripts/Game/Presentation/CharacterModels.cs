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

        /// <summary>
        /// Clears every cache at the start of each Play session. The project enters Play mode without a domain reload,
        /// so statics survive between sessions while the materials and avatars made at runtime do not; without this,
        /// a second Play found "already tried" flags and dead references (Bolt lost his texture).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            System.Array.Clear(Loaded, 0, Loaded.Length);
            System.Array.Clear(Tried, 0, Tried.Length);
            System.Array.Clear(BuiltAvatars, 0, BuiltAvatars.Length);
            System.Array.Clear(Textured, 0, Textured.Length);
            System.Array.Clear(TexturedTried, 0, TexturedTried.Length);
        }
        private static readonly Avatar[] BuiltAvatars = new Avatar[Characters.Count];

        /// <summary>Standing height to match the primitive characters.</summary>
        public const float StandingHeight = 1.3f;
        /// <summary>Drivers are scaled as if this tall standing: big and readable over the cockpit rim from the match camera.</summary>
        public const float DrivingHeight = 1.9f;
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

            var material = TexturedMaterial(character);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (material != null)
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = material;
                    r.sharedMaterials = mats;
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = false;
            }
            return animator;
        }

        private static readonly Material[] Textured = new Material[Characters.Count];
        private static readonly bool[] TexturedTried = new bool[Characters.Count];

        /// <summary>
        /// A material from loose texture files next to the model (<c>&lt;path&gt;_baseColor</c>, optional <c>_normal</c>), or
        /// null to keep the imported materials. Meshy's FBX embeds its textures under material slots Unity's URP
        /// importer doesn't map, so Bolt came in untextured; his textures are extracted to these files instead.
        /// </summary>
        private static Material TexturedMaterial(int character)
        {
            if (TexturedTried[character]) return Textured[character];
            TexturedTried[character] = true;
            string path = Entries[character].Path;
            var baseColor = Resources.Load<Texture2D>(path + "_baseColor");
            if (baseColor == null) return null;
            var material = PrototypeMaterials.Create(Color.white, 0.35f);
            material.name = Characters.Names[character];
            material.mainTexture = baseColor;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", baseColor);
            var normal = Resources.Load<Texture2D>(path + "_normal");
            if (normal != null && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            Textured[character] = material;
            return material;
        }

        /// <summary>
        /// Puts a seated driver's hands on the steering wheel: slides the model forward if its arms can't reach
        /// (chibi arms are short), then bends each arm with a two-bone solve so the wrists land on the grips.
        /// Grips are world positions; the driver should already be placed in the car.
        /// </summary>
        public static void GripWheel(Transform driver, Vector3 leftGrip, Vector3 rightGrip)
        {
            var animator = driver.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null) return;
            bool wasEnabled = animator.enabled;
            animator.enabled = true; // bone lookups need it on; with no controller it never animates
            try { GripWheel(animator, driver, leftGrip, rightGrip); }
            finally { animator.enabled = wasEnabled; }
        }

        private static void GripWheel(Animator animator, Transform driver, Vector3 leftGrip, Vector3 rightGrip)
        {
            if (!animator.isHuman) return;
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            Transform lu = B(HumanBodyBones.LeftUpperArm), ll = B(HumanBodyBones.LeftLowerArm), lh = B(HumanBodyBones.LeftHand);
            Transform ru = B(HumanBodyBones.RightUpperArm), rl = B(HumanBodyBones.RightLowerArm), rh = B(HumanBodyBones.RightHand);
            if (lu == null || ll == null || lh == null || ru == null || rl == null || rh == null) return;

            Vector3 forward = driver.forward, up = driver.up, right = driver.right;

            // Hands reach at most ~90% of the arm's length, so slide the driver toward the wheel if needed.
            float reach = 0.9f * (Vector3.Distance(lu.position, ll.position) + Vector3.Distance(ll.position, lh.position));
            Vector3 shoulders = (lu.position + ru.position) * 0.5f;
            Vector3 toGrips = (leftGrip + rightGrip) * 0.5f - shoulders;
            float along = Vector3.Dot(toGrips, forward);
            float sideways = (toGrips - forward * along).sqrMagnitude;
            float halfSpan = Vector3.Distance(leftGrip, rightGrip) * 0.5f;
            float reachForward = Mathf.Sqrt(Mathf.Max(0f, reach * reach - sideways - halfSpan * halfSpan));
            float slide = Mathf.Clamp(along - reachForward, 0f, reach * 1.5f);
            if (slide > 0f) animator.transform.position += forward * slide;

            // Elbows point down, out and slightly back.
            SolveArm(lu, ll, lh, leftGrip, lu.position - right * 0.6f - up - forward * 0.3f);
            SolveArm(ru, rl, rh, rightGrip, ru.position + right * 0.6f - up - forward * 0.3f);
        }

        /// <summary>Analytic two-bone IK: places the elbow on the circle allowed by the bone lengths, toward <paramref name="pole"/>.</summary>
        private static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            Vector3 s = upper.position;
            float a = Vector3.Distance(s, lower.position), b = Vector3.Distance(lower.position, hand.position);
            if (a < 1e-4f || b < 1e-4f) return;
            Vector3 toTarget = target - s;
            float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 1e-3f, (a + b) * 0.999f);
            Vector3 dir = toTarget.normalized;
            Vector3 bend = pole - s;
            bend -= Vector3.Dot(bend, dir) * dir;
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.Cross(dir, Vector3.right);
            bend.Normalize();
            float x = (a * a - b * b + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            Vector3 elbow = s + dir * x + bend * h;
            upper.rotation = Quaternion.FromToRotation(lower.position - s, elbow - s) * upper.rotation;
            Vector3 wrist = s + dir * d;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, wrist - lower.position) * lower.rotation;
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

        /// <summary>
        /// The part of a standing model a portrait should show: from the chest up to the top of the head, ignoring
        /// the thinnest 3% at the top (antenna tips, crest points) so they don't shrink the face. Returns the
        /// world-space centre of that region and its height. False for the primitive stand-ins.
        /// </summary>
        public static bool PortraitFocus(Transform root, out Vector3 centre, out float size)
        {
            centre = Vector3.zero;
            size = 0f;
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null) return false;
            bool wasEnabled = animator.enabled;
            animator.enabled = true;
            var chest = animator.isHuman
                ? (animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine))
                : null;
            animator.enabled = wasEnabled;

            var points = new List<Vector3>();
            CollectWorldVertices(animator.gameObject, points);
            if (points.Count == 0) return false;
            float floor = chest != null ? chest.position.y : float.MinValue;
            var heights = new List<float>();
            foreach (var p in points) if (p.y >= floor) heights.Add(p.y);
            if (heights.Count < 10) return false;
            heights.Sort();
            float bottom = heights[0], top = heights[(int)((heights.Count - 1) * 0.97f)];

            // Centre horizontally on the body itself (median), not on arms or a tail sticking out.
            var xs = new List<float>();
            var zs = new List<float>();
            foreach (var p in points)
                if (p.y >= floor && p.y <= top) { xs.Add(p.x); zs.Add(p.z); }
            xs.Sort();
            zs.Sort();
            size = top - bottom;
            centre = new Vector3(xs[xs.Count / 2], (bottom + top) * 0.5f, zs[zs.Count / 2]);
            return size > 0.01f;
        }

        private static void CollectWorldVertices(GameObject model, List<Vector3> into)
        {
            var baked = new Mesh();
            var local = new List<Vector3>();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                Matrix4x4 toWorld;
                if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    skinned.BakeMesh(baked, true);
                    baked.GetVertices(local);
                    toWorld = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                }
                else if (r.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    filter.sharedMesh.GetVertices(local);
                    toWorld = r.transform.localToWorldMatrix;
                }
                else continue;
                foreach (var v in local) into.Add(toWorld.MultiplyPoint3x4(v));
            }
            Object.Destroy(baked);
        }

        /// <summary>
        /// Height of the model's actual vertices as posed. Renderer bounds aren't reliable here: a skinned mesh's
        /// bounds are whatever the importer set (and differ between models), which made Mitzi come out oversized.
        /// </summary>
        private static float MeasureHeight(GameObject model)
        {
            float min = float.MaxValue, max = float.MinValue;
            var baked = new Mesh();
            var vertices = new List<Vector3>();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                Matrix4x4 toWorld;
                if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    skinned.BakeMesh(baked, true); // vertices in the renderer's space, scale applied
                    baked.GetVertices(vertices);
                    toWorld = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                }
                else if (r.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    filter.sharedMesh.GetVertices(vertices);
                    toWorld = r.transform.localToWorldMatrix;
                }
                else continue;
                foreach (var v in vertices)
                {
                    float y = toWorld.MultiplyPoint3x4(v).y;
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
            }
            Object.Destroy(baked);
            return max > min ? max - min : 0f;
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
