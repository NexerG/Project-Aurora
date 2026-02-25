using Silk.NET.GLFW;
using Silk.NET.Maths;
using Keys = Silk.NET.GLFW.Keys;

namespace ArctisAurora.EngineWork
{
    public class Keybind
    {
        public Keys? keyboardKey;
        public MouseButton? mouseButton;

        public Keybind(Keys key)
        {
            keyboardKey = key;
        }

        public Keybind(MouseButton key)
        {
            mouseButton = key;
        }
    }

    public class KeybindComparer : IEqualityComparer<Keybind>
    {
        bool IEqualityComparer<Keybind>.Equals(Keybind? x, Keybind? y)
        {
            if(x == null || y == null) return false;

            if (x.keyboardKey != null && y.keyboardKey != null)
            {
                if (x.keyboardKey == y.keyboardKey) return true;
                else return false;
            }
            else if (x.mouseButton != null && y.mouseButton != null)
            {
                if (x.mouseButton == y.mouseButton) return true;
                else return false;
            }

            return false;
        }
        
        int IEqualityComparer<Keybind>.GetHashCode(Keybind obj)
        {
            if (obj.mouseButton != null) return obj.mouseButton.GetHashCode();
            if (obj.keyboardKey != null) return obj.keyboardKey.GetHashCode();
            return base.GetHashCode();
        }
    }

    public unsafe class InputHandler
    {
        public static InputHandler instance { get; private set; }

        private static HashSet<Keybind> keysDown = new HashSet<Keybind>(new KeybindComparer());
        public static Vector2D<float> mousePos = new Vector2D<float>(0, 0);

        public static Dictionary<string, Dictionary<Keybind, Action>> keyBinds = new Dictionary<string, Dictionary<Keybind, Action>>();

        public bool IsKeyDown(Keybind k) => keysDown.Contains(k);

        public InputHandler()
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
                keysDown.Add(new Keybind(button));
            }
            else
            {
                keysDown.Remove(new Keybind(button));
            }
        }

        internal void ProcessKeyboard(WindowHandle* window, Keys key, int _scanCode, InputAction action, KeyModifiers mods)
        {
            if (action == InputAction.Press || action == InputAction.Repeat)
            {
                keysDown.Add(new Keybind(key));
            }
            else
            {
                keysDown.Remove(new Keybind(key));
            }
        }
    }
}
