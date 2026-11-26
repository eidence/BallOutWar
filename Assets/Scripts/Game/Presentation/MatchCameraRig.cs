using BOW.Simulation;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>
    /// Low, elevated camera behind the local player's goal (US-2.1, TDD 6.1). It frames the whole board
    /// (all four corners, every goal mouth, your own car and driver) as closely as it can; only the back of
    /// your goal pocket may crop off the bottom edge. Distance and aim are solved
    /// for the current aspect ratio, so the framing holds on any phone shape.
    /// </summary>
    public static class MatchCameraRig
    {
        private static readonly Vector3[] Local = new Vector3[16];
        private static readonly Vector3[] Points = new Vector3[16];

        /// <summary>Downward tilt: lower is a flatter, more side-on view; 90 would look straight down.</summary>
        public const float PitchDegrees = 32f;
        public const float FieldOfView = 50f;

        public static void Place(Camera cam, int localSeat, ArenaDefinition arena)
        {
            cam.fieldOfView = FieldOfView;
            float h = arena.HalfSize, g = arena.GoalHalfWidth;

            // In the local player's frame: +z away from you, x to your right.
            int n = 0;
            const float far = 2.0f; // far goal pocket
            Local[n++] = new Vector3(-h, 0f, h);          // far corners
            Local[n++] = new Vector3(h, 0f, h);
            Local[n++] = new Vector3(-g, 0.9f, h + far);   // far goal pocket back
            Local[n++] = new Vector3(g, 0.9f, h + far);
            Local[n++] = new Vector3(-h - 0.4f, 1.2f, g);  // side goal mouths, both ends
            Local[n++] = new Vector3(h + 0.4f, 1.2f, g);
            Local[n++] = new Vector3(-h, 0.2f, -g);
            Local[n++] = new Vector3(h, 0.2f, -g);
            Local[n++] = new Vector3(-h, 0f, -h);                 // near corners: the whole board stays on screen
            Local[n++] = new Vector3(h, 0f, -h);
            Local[n++] = new Vector3(0f, 0.3f, -h + 1.2f);       // your bumper
            Local[n++] = new Vector3(0f, 1.9f, -h - 0.7f);        // your driver's head

            Vector3 inward = SimSpace.Direction(SeatFrame.Inward(localSeat));
            Quaternion facing = Quaternion.LookRotation(inward, Vector3.up);
            for (int i = 0; i < n; i++) Points[i] = facing * Local[i];

            Quaternion rotation = facing * Quaternion.Euler(PitchDegrees, 0f, 0f);
            Vector3 forward = rotation * Vector3.forward;
            cam.transform.rotation = rotation;

            // Closest camera that still shows every point, trying aim points along the line toward the far goal.
            for (float distance = 6f; distance < 120f; distance += 0.25f)
            for (float aim = -0.4f; aim <= 0.3f; aim += 0.05f)
            {
                cam.transform.position = inward * (h * aim) - forward * distance;
                if (AllVisible(cam, n)) return;
            }
        }

        private static bool AllVisible(Camera cam, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 v = cam.WorldToViewportPoint(Points[i]);
                // Leave room at the top for the score bar.
                if (v.z <= 0f || v.x < 0.01f || v.x > 0.99f || v.y < 0.01f || v.y > 0.86f) return false;
            }
            return true;
        }
    }
}
