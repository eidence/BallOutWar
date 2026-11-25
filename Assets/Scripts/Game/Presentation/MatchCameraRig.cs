using BOW.Simulation;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Angled top-down camera with the local player's goal at the bottom of the screen (US-2.1, TDD 6.1).
    /// Distance is solved so the whole arena, including every goal pocket, fits the current aspect ratio.
    /// </summary>
    public static class MatchCameraRig
    {
        private static readonly Vector3[] Points = new Vector3[24];

        public static void Place(Camera cam, int localSeat, float halfSize, float pitchDegrees, float fieldOfView)
        {
            cam.fieldOfView = fieldOfView;

            Vector3 inward = SimSpace.Direction(SeatFrame.Inward(localSeat));
            Quaternion rotation = Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Euler(pitchDegrees, 0f, 0f);
            Vector3 forward = rotation * Vector3.forward;
            // Aim a little toward the local player's side so their goal gets more of the screen.
            Vector3 target = -inward * (halfSize * 0.06f);

            int n = 0;
            float e = halfSize + 1.6f; // walls plus goal pockets
            foreach (float y in new[] { 0f, 0.9f })
            {
                Points[n++] = new Vector3(-e, y, -e);
                Points[n++] = new Vector3(e, y, -e);
                Points[n++] = new Vector3(-e, y, e);
                Points[n++] = new Vector3(e, y, e);
            }

            cam.transform.rotation = rotation;
            for (float distance = 8f; distance < 120f; distance += 0.25f)
            {
                cam.transform.position = target - forward * distance;
                if (AllVisible(cam, n)) return;
            }
        }

        private static bool AllVisible(Camera cam, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 v = cam.WorldToViewportPoint(Points[i]);
                // Leave room at the top for the score bar.
                if (v.z <= 0f || v.x < 0.02f || v.x > 0.98f || v.y < 0.02f || v.y > 0.90f) return false;
            }
            return true;
        }
    }
}
