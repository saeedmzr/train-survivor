using UnityEngine;
using UnityEngine.InputSystem;
using TrainSurvivor.Core;
using TrainSurvivor.Game.UI;

namespace TrainSurvivor.Game
{
    /// <summary>
    /// One thumb: press = accelerate, release = brake and shoot.
    /// Touch is primary; mouse is the editor/desktop fallback.
    /// </summary>
    public sealed class InputRouter : MonoBehaviour
    {
        public Intent Current { get; private set; } = Intent.Stop;
        public bool LastWasTouch { get; private set; }

        void Update()
        {
            bool held = !UiState.Modal;
            float steer = 0f;

            var touch = Touchscreen.current;
            if (held && touch != null && touch.primaryTouch.press.isPressed)
            {
                held = true;
                LastWasTouch = true;
                float w = Mathf.Max(1f, Screen.width);
                steer = Mathf.Clamp(touch.primaryTouch.position.ReadValue().x / w * 2f - 1f, -1f, 1f);
            }
            else if (held && Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                held = true;
                LastWasTouch = false;
                float w = Mathf.Max(1f, Screen.width);
                steer = Mathf.Clamp(Mouse.current.position.ReadValue().x / w * 2f - 1f, -1f, 1f);
            }
            else
            {
                held = false;
            }

            Current = new Intent { Held = held, SteerX = steer };
        }
    }
}
