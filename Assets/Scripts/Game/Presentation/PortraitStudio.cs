using System.Collections;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Renders a head-and-shoulders portrait of each character into a texture for the HUD.
    /// The studio sits far outside the arena and its cameras render once, then switch off,
    /// so portraits cost nothing per frame.
    /// </summary>
    public sealed class PortraitStudio
    {
        private const int Resolution = 256;
        private static readonly Vector3 Origin = new Vector3(5000f, 0f, 0f);

        private readonly Camera[] _cameras = new Camera[Characters.Count];

        /// <summary>
        /// How far to raise each character's head in their portrait ring, as a fraction of the framed size
        /// (Bolt, Mitzi, Hopper, Pip). Set by eye so the tops of the heads line up; antennae, ears and crests stick
        /// up above that line. Positive = higher in the ring.
        /// </summary>
        private static readonly float[] PortraitRaise = { 0.17f, 0f, 0f, 0f };

        /// <summary>How far to move each character right in their ring, as a fraction of the framed size. Positive = right.</summary>
        private static readonly float[] PortraitShiftRight = { 0f, 0.08f, 0f, 0f };

        public RenderTexture[] Portraits { get; } = new RenderTexture[Characters.Count];

        public PortraitStudio(Transform parent)
        {
            var root = new GameObject("Portrait Studio").transform;
            root.SetParent(parent, false);
            root.position = Origin;

            var critters = new Transform[Characters.Count];
            var spots = new Transform[Characters.Count];
            for (int c = 0; c < Characters.Count; c++)
            {
                var spot = new GameObject($"Spot {Characters.Names[c]}").transform;
                spot.SetParent(root, false);
                spot.localPosition = new Vector3(c * 20f, 0f, 0f);

                var critter = Characters.Build(c, spot);
                critter.localRotation = Quaternion.Euler(0f, -20f, 0f); // faces the camera, turned slightly for a three-quarter view

                var rt = new RenderTexture(Resolution, Resolution, 16, RenderTextureFormat.ARGB32) { name = $"Portrait {Characters.Names[c]}", antiAliasing = 2 };
                rt.Create();
                Portraits[c] = rt;

                var cam = new GameObject("Camera").AddComponent<Camera>();
                cam.transform.SetParent(spot, false);
                cam.transform.localPosition = new Vector3(0f, 0.95f, 1.9f);
                cam.transform.LookAt(spot.position + new Vector3(0f, 0.72f, 0f));
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 6f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 1f);
                cam.targetTexture = rt;
                _cameras[c] = cam;
                critters[c] = critter;
                spots[c] = spot;
            }

            // Frame every real model on its chest-up region, then apply its hand-set nudge (heads are judged by eye:
            // goggles, ears and antennae defeat any automatic "top of the head").
            for (int c = 0; c < Characters.Count; c++)
            {
                if (!CharacterModels.PortraitFocus(critters[c], out var centre, out float size)) continue;
                centre.y -= PortraitRaise[c] * size; // aim lower, so the head rises in the ring
                FrameOnModel(_cameras[c], spots[c], centre, size);
                _cameras[c].transform.position -= _cameras[c].transform.right * (PortraitShiftRight[c] * size); // camera left = character right
            }
        }

        /// <summary>
        /// For a real model, frames the chest-up region so every character fills the ring the same way, whatever their
        /// ears, antennae or proportions. The stand-ins keep the fixed framing.
        /// </summary>
        private static void FrameOnModel(Camera cam, Transform spot, Vector3 centre, float size)
        {
            const float margin = 1.2f; // the round mask hides the corners, so leave a little room
            float distance = size * 0.5f * margin / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var direction = (spot.forward + Vector3.up * 0.12f).normalized;
            cam.transform.position = centre + direction * distance;
            cam.transform.LookAt(centre);
            cam.farClipPlane = distance + size * 2f;
        }

        /// <summary>Lets the cameras draw for a couple of frames, then turns them off.</summary>
        public IEnumerator RenderOnce()
        {
            yield return null;
            yield return null;
            foreach (var cam in _cameras) if (cam != null) cam.enabled = false;
        }
    }
}
