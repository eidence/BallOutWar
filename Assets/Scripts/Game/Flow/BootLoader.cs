using UnityEngine;
using UnityEngine.SceneManagement;

namespace BOW.Game
{
    /// <summary>
    /// First scene (TDD 9.1): loads saved data, then opens the main menu.
    /// The offline-resume check (US-6.6) and first-run tutorial (US-7.2) will hook in here.
    /// </summary>
    public sealed class BootLoader : MonoBehaviour
    {
        public const string MenuScene = "Menu";
        public const string MatchScene = "Match";

        private void Start()
        {
            Application.targetFrameRate = 60;
            ProfileStore.Load();
            SceneManager.LoadScene(MenuScene);
        }
    }

    /// <summary>Shared camera and lighting setup for the arena look in every scene.</summary>
    public static class SceneLook
    {
        public static void Apply(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            camera.farClipPlane = 300f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.36f, 0.45f);
            RenderSettings.fog = false;

            // Bloom makes the neon glow. volume.profile is a per-scene copy, so the asset is never modified.
            var volume = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            if (volume != null && volume.profile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom))
            {
                bloom.active = true;
                bloom.threshold.Override(1.0f);
                bloom.intensity.Override(1.1f);
                bloom.scatter.Override(0.65f);
            }
        }
    }
}
