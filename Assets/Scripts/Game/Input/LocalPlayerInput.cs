using BOW.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using SimInput = BOW.Simulation.PlayerInput;

namespace BOW.Game
{
    /// <summary>
    /// Reads the local player's controls every frame and turns them into a simulation input (TDD 6.3).
    /// Keyboard: A/D or arrows (or numpad 4/6) to move, Space to blast. Gamepad: left stick or D-pad,
    /// south button to blast. Mouse: hold left button to drag the paddle, right button to blast.
    /// Touch: drag anywhere to move; the on-screen Blast button blasts.
    /// </summary>
    public sealed class LocalPlayerInput
    {
        private bool _blastLatched;
        private float _move;
        private bool _hasTarget;
        private float _target;

        /// <summary>Called by the on-screen Blast button.</summary>
        public void PressBlast() => _blastLatched = true;

        /// <summary>Samples devices. Call once per rendered frame.</summary>
        public void Sample(Camera cam, int seat, float halfSize)
        {
            _move = 0f;
            _hasTarget = false;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // Number-pad 4/6 too: with Num Lock off they act as arrows but report as numpad keys.
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed || keyboard.numpad4Key.isPressed) _move -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed || keyboard.numpad6Key.isPressed) _move += 1f;
                if (keyboard.spaceKey.wasPressedThisFrame) _blastLatched = true;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                float x = pad.leftStick.x.ReadValue() + pad.dpad.x.ReadValue();
                if (Mathf.Abs(x) > 0.15f) _move = Mathf.Clamp(x, -1f, 1f);
                if (pad.buttonSouth.wasPressedThisFrame) _blastLatched = true;
            }

            var touch = Touchscreen.current;
            if (touch != null)
            {
                foreach (var t in touch.touches)
                {
                    if (!t.press.isPressed) continue;
                    // Touches on HUD buttons (Blast, Pause) don't steer the paddle.
                    if (OverUi(t.touchId.ReadValue())) continue;
                    if (TryProject(cam, t.position.ReadValue(), seat, halfSize, out float along))
                    {
                        _hasTarget = true;
                        _target = along;
                    }
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && (touch == null || !touch.press.isPressed))
            {
                if (mouse.rightButton.wasPressedThisFrame) _blastLatched = true;
                if (mouse.leftButton.isPressed && !OverUi(-1)
                    && TryProject(cam, mouse.position.ReadValue(), seat, halfSize, out float along))
                {
                    _hasTarget = true;
                    _target = along;
                }
            }
        }

        /// <summary>Input for one simulation tick. The blast press is consumed so it fires exactly once.</summary>
        public SimInput Consume()
        {
            var input = _hasTarget ? SimInput.ToTarget(_target, _blastLatched) : SimInput.Analog(_move, _blastLatched);
            _blastLatched = false;
            return input;
        }

        private static bool OverUi(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId < 0 ? es.IsPointerOverGameObject() : es.IsPointerOverGameObject(pointerId);
        }

        /// <summary>Projects a screen point onto the floor and returns its position along the seat's goal line.</summary>
        private static bool TryProject(Camera cam, Vector2 screen, int seat, float halfSize, out float along)
        {
            along = 0f;
            Ray ray = cam.ScreenPointToRay(screen);
            var floor = new Plane(Vector3.up, Vector3.zero);
            if (!floor.Raycast(ray, out float distance)) return false;
            along = SeatFrame.Along(seat, halfSize, SimSpace.ToSim(ray.GetPoint(distance)));
            return true;
        }

        public static bool PausePressed()
        {
            // On Android the system back button arrives as Escape.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            return pad != null && pad.startButton.wasPressedThisFrame;
        }
    }
}
