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

        public RenderTexture[] Portraits { get; } = new RenderTexture[Characters.Count];

        public PortraitStudio(Transform parent)
        {
            var root = new GameObject("Portrait Studio").transform;
            root.SetParent(parent, false);
            root.position = Origin;

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
            }
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
