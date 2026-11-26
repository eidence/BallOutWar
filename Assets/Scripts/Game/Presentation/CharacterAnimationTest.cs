using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BOW.Game
{
    /// <summary>
    /// Development check for the character rigs: lines up every character that has a real model and plays the
    /// standard humanoid motions on them (idle, walk, run, wave, cheer, drive, hit, jump). Bad skinning shows up
    /// as stretched clothing or limbs dragging the body. Started from Ball Out War > Character Animation Test.
    /// Space / click cycles motions; A toggles auto-cycling; drag to orbit.
    /// </summary>
    public sealed class CharacterAnimationTest : MonoBehaviour
    {
        private const float SecondsPerMotion = 5f;

        private sealed class Actor
        {
            public Animator Animator;
            public HumanPoseHandler Handler;
            public HumanPose Pose;
            public Vector3 BodyPosition;
            public Quaternion BodyRotation;
            public string Name;
        }

        private readonly List<Actor> _actors = new List<Actor>();
        private Camera _camera;
        private int _motion;
        private float _motionStart;
        private bool _auto = true;
        private float _yaw = 20f;
        private Vector3 _lastMouse;

        private void Start()
        {
            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.55f);

            var floor = PrototypeMaterials.Primitive(PrimitiveType.Plane, "Floor", transform, PrototypeMaterials.Create(new Color(0.18f, 0.19f, 0.22f), 0.2f));
            floor.transform.localScale = Vector3.one * 3f;

            int count = 0;
            for (int c = 0; c < Characters.Count; c++)
                if (CharacterModels.Has(c)) count++;

            int slot = 0;
            for (int c = 0; c < Characters.Count; c++)
            {
                if (!CharacterModels.Has(c)) continue;
                var spot = new GameObject(Characters.Names[c]).transform;
                spot.SetParent(transform, false);
                spot.localPosition = new Vector3((slot - (count - 1) * 0.5f) * 1.6f, 0f, 0f);
                slot++;
                var animator = CharacterModels.Spawn(c, spot);
                if (animator == null) continue;
                if (!animator.isHuman)
                {
                    Debug.LogWarning($"{Characters.Names[c]} is not Humanoid, so it can't play the test motions.");
                    continue;
                }
                var actor = new Actor
                {
                    Animator = animator,
                    Handler = new HumanPoseHandler(animator.avatar, animator.transform),
                    Pose = new HumanPose(),
                    Name = Characters.Names[c],
                };
                actor.Handler.GetHumanPose(ref actor.Pose);
                actor.BodyPosition = actor.Pose.bodyPosition;
                actor.BodyRotation = actor.Pose.bodyRotation;
                animator.enabled = false;
                _actors.Add(actor);
            }

            _camera = Camera.main != null ? Camera.main : new GameObject("Camera").AddComponent<Camera>();
            _camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.fieldOfView = 35f;
            _motionStart = Time.time;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)) Next();
            if (keyboard != null && keyboard.aKey.wasPressedThisFrame) _auto = !_auto;
            if (_auto && Time.time - _motionStart > SecondsPerMotion) Next();

            if (mouse != null && mouse.leftButton.wasPressedThisFrame) _lastMouse = mouse.position.ReadValue();
            if (mouse != null && mouse.leftButton.isPressed)
            {
                Vector3 now = mouse.position.ReadValue();
                _yaw += (now.x - _lastMouse.x) * 0.3f;
                _lastMouse = now;
            }
            float width = Mathf.Max(1, _actors.Count) * 1.6f;
            var rot = Quaternion.Euler(12f, 180f + _yaw, 0f);
            _camera.transform.rotation = rot;
            _camera.transform.position = new Vector3(0f, 0.75f, 0f) - rot * Vector3.forward * (2.5f + width);

            var motion = HumanoidPoses.All[_motion];
            float t = Time.time - _motionStart;
            foreach (var a in _actors)
            {
                float lift = HumanoidPoses.Apply(ref a.Pose, motion, t);
                a.Pose.bodyRotation = a.BodyRotation;
                a.Pose.bodyPosition = a.BodyPosition + Vector3.up * (lift / Mathf.Max(0.01f, a.Animator.humanScale));
                if (motion == HumanoidPoses.Motion.Drive) a.Pose.bodyPosition += Vector3.down * 0.35f;
                a.Handler.SetHumanPose(ref a.Pose);
            }
        }

        private void Next()
        {
            _motion = (_motion + 1) % HumanoidPoses.All.Length;
            _motionStart = Time.time;
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(20, 16, 900, 40), $"{HumanoidPoses.All[_motion]}   ({_motion + 1}/{HumanoidPoses.All.Length})", style);
            var small = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            GUI.Label(new Rect(20, 56, 900, 30), $"Space: next motion    A: auto-cycle {(_auto ? "on" : "off")}    Drag: orbit", small);
            if (_actors.Count == 0) GUI.Label(new Rect(20, 96, 900, 30), "No humanoid character models found in Resources/Models.", small);
            else GUI.Label(new Rect(20, 96, 900, 30), string.Join("   ", _actors.ConvertAll(a => a.Name)), small);
            if (GUI.Button(new Rect(20, 136, 160, 44), "Next motion")) Next();
        }

        private void OnDestroy()
        {
            foreach (var a in _actors) a.Handler?.Dispose();
        }
    }
}
