using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Keys = Silk.NET.GLFW.Keys;

namespace ArctisAurora.EngineWork
{
    internal unsafe class InputHandler
    {
        public static InputHandler instance { get; private set; }

        public HashSet<Keys> KeysDown = new HashSet<Keys>();
        public HashSet<MouseButton> MouseButtons = new HashSet<MouseButton>();
        public static Vector2D<float> mousePos = new Vector2D<float>(0, 0);

        public bool IsKeyDown(Keys k) => KeysDown.Contains(k);
        public bool IsMouseDown(MouseButton button) => MouseButtons.Contains(button);

        internal InputHandler()
        {
            instance = this;
        }

        internal void ProcessMouseMove(WindowHandle* window, double xPos, double yPos)
        {
            mousePos.X = (float)xPos;
            mousePos.Y = (float)yPos;
        }

        internal void ProcessMouseClick(WindowHandle* window, MouseButton button, InputAction action, KeyModifiers mods)
        {
            if (action == InputAction.Press || action == InputAction.Repeat)
            {
                MouseButtons.Add(button);
            }
            else
            {
                MouseButtons.Remove(button);
            }
        }

        internal void ProcessKeyboard(WindowHandle* window, Keys key, int _scanCode, InputAction action, KeyModifiers mods)
        {
            if (action == InputAction.Press || action == InputAction.Repeat)
            {
                KeysDown.Add(key);
            }
            else
            {
                KeysDown.Remove(key);
            }
        }
    }
}
