using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork.Serialization;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using System;
using System.Reflection;
using System.Xml.Linq;
using glfwkey = Silk.NET.GLFW.Keys;

namespace ArctisAurora.EngineWork
{
    [A_XSDType("Keys", "Input")]
    public enum Keys
    {
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p, q, r, s, t, u, v, w, x, y, z,
        Apostrophe, Comma, Minus, Period, Slash, Semicolon, Equal, LeftBracket, Backslash, RightBracket, GraveAccent,
        Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9, NumpadDecimal, NumpadDivide, NumpadMultiply, NumpadSubtract, NumpadAdd, NumpadEnter, NumpadEqual,
        Num0, Num1, Num2, Num3, Num4, Num5, Num6, Num7, Num8, Num9,
        Space, Enter, Tab, Backspace, Escape, LeftControl, RightControl, LeftShift, RightShift, LeftWin, RightWin, Menu, CapsLock, ScrollLock, NumLock, PrintScreen, Pause,
        LeftAlt, RightAlt, LeftSuper, RightSuper,
        Up, Down, Left, Right, Home, End, PageUp, PageDown, Insert, Delete,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24, F25,
        Number0, Number1, Number2, Number3, Number4, Number5, Number6, Number7, Number8, Number9,
        unknown,
        MouseLeft, MouseRight, MouseMiddle, MouseButton4, MouseButton5, MouseButton6, MouseButton7, MouseButton8
    }

    [A_XSDType("Keybind", "Input")]
    public class Keybind
    {
        [A_XSDElementProperty("Button", "Input")]
        public Keys keyboardKey;
        [A_XSDElementProperty("Action", "Input")]
        public Action action;

        public Keybind(Keys key)
        {
            keyboardKey = key;
        }

        public Keybind(Keys keyboardKey, Action action)
        {
            this.keyboardKey = keyboardKey;
            this.action = action;
        }

        public void AddAction(Action action)
        {
            this.action += action;
        }

        public static Keys MapKey(Silk.NET.GLFW.Keys glfwkey) => glfwkey switch
        {
            // letters
            Silk.NET.GLFW.Keys.A => Keys.A,
            Silk.NET.GLFW.Keys.B => Keys.B,
            Silk.NET.GLFW.Keys.C => Keys.C,
            Silk.NET.GLFW.Keys.D => Keys.D,
            Silk.NET.GLFW.Keys.E => Keys.E,
            Silk.NET.GLFW.Keys.F => Keys.F,
            Silk.NET.GLFW.Keys.G => Keys.G,
            Silk.NET.GLFW.Keys.H => Keys.H,
            Silk.NET.GLFW.Keys.I => Keys.I,
            Silk.NET.GLFW.Keys.J => Keys.J,
            Silk.NET.GLFW.Keys.K => Keys.K,
            Silk.NET.GLFW.Keys.L => Keys.L,
            Silk.NET.GLFW.Keys.M => Keys.M,
            Silk.NET.GLFW.Keys.N => Keys.N,
            Silk.NET.GLFW.Keys.O => Keys.O,
            Silk.NET.GLFW.Keys.P => Keys.P,
            Silk.NET.GLFW.Keys.Q => Keys.Q,
            Silk.NET.GLFW.Keys.R => Keys.R,
            Silk.NET.GLFW.Keys.S => Keys.S,
            Silk.NET.GLFW.Keys.T => Keys.T,
            Silk.NET.GLFW.Keys.U => Keys.U,
            Silk.NET.GLFW.Keys.V => Keys.V,
            Silk.NET.GLFW.Keys.W => Keys.W,
            Silk.NET.GLFW.Keys.X => Keys.X,
            Silk.NET.GLFW.Keys.Y => Keys.Y,
            Silk.NET.GLFW.Keys.Z => Keys.Z,

            // numbers
            Silk.NET.GLFW.Keys.Number0 => Keys.Number0,
            Silk.NET.GLFW.Keys.Number1 => Keys.Number1,
            Silk.NET.GLFW.Keys.Number2 => Keys.Number2,
            Silk.NET.GLFW.Keys.Number3 => Keys.Number3,
            Silk.NET.GLFW.Keys.Number4 => Keys.Number4,
            Silk.NET.GLFW.Keys.Number5 => Keys.Number5,
            Silk.NET.GLFW.Keys.Number6 => Keys.Number6,
            Silk.NET.GLFW.Keys.Number7 => Keys.Number7,
            Silk.NET.GLFW.Keys.Number8 => Keys.Number8,
            Silk.NET.GLFW.Keys.Number9 => Keys.Number9,

            // function keys
            Silk.NET.GLFW.Keys.F1 => Keys.F1,
            Silk.NET.GLFW.Keys.F2 => Keys.F2,
            Silk.NET.GLFW.Keys.F3 => Keys.F3,
            Silk.NET.GLFW.Keys.F4 => Keys.F4,
            Silk.NET.GLFW.Keys.F5 => Keys.F5,
            Silk.NET.GLFW.Keys.F6 => Keys.F6,
            Silk.NET.GLFW.Keys.F7 => Keys.F7,
            Silk.NET.GLFW.Keys.F8 => Keys.F8,
            Silk.NET.GLFW.Keys.F9 => Keys.F9,
            Silk.NET.GLFW.Keys.F10 => Keys.F10,
            Silk.NET.GLFW.Keys.F11 => Keys.F11,
            Silk.NET.GLFW.Keys.F12 => Keys.F12,
            Silk.NET.GLFW.Keys.F13 => Keys.F13,
            Silk.NET.GLFW.Keys.F14 => Keys.F14,
            Silk.NET.GLFW.Keys.F15 => Keys.F15,
            Silk.NET.GLFW.Keys.F16 => Keys.F16,
            Silk.NET.GLFW.Keys.F17 => Keys.F17,
            Silk.NET.GLFW.Keys.F18 => Keys.F18,
            Silk.NET.GLFW.Keys.F19 => Keys.F19,
            Silk.NET.GLFW.Keys.F20 => Keys.F20,
            Silk.NET.GLFW.Keys.F21 => Keys.F21,
            Silk.NET.GLFW.Keys.F22 => Keys.F22,
            Silk.NET.GLFW.Keys.F23 => Keys.F23,
            Silk.NET.GLFW.Keys.F24 => Keys.F24,
            Silk.NET.GLFW.Keys.F25 => Keys.F25,

            // navigation
            Silk.NET.GLFW.Keys.Up => Keys.Up,
            Silk.NET.GLFW.Keys.Down => Keys.Down,
            Silk.NET.GLFW.Keys.Left => Keys.Left,
            Silk.NET.GLFW.Keys.Right => Keys.Right,
            Silk.NET.GLFW.Keys.Home => Keys.Home,
            Silk.NET.GLFW.Keys.End => Keys.End,
            Silk.NET.GLFW.Keys.PageUp => Keys.PageUp,
            Silk.NET.GLFW.Keys.PageDown => Keys.PageDown,
            Silk.NET.GLFW.Keys.Insert => Keys.Insert,
            Silk.NET.GLFW.Keys.Delete => Keys.Delete,

            // modifiers
            Silk.NET.GLFW.Keys.ShiftLeft => Keys.LeftShift,
            Silk.NET.GLFW.Keys.ShiftRight => Keys.RightShift,
            Silk.NET.GLFW.Keys.ControlLeft => Keys.LeftControl,
            Silk.NET.GLFW.Keys.ControlRight => Keys.RightControl,
            Silk.NET.GLFW.Keys.AltLeft => Keys.LeftAlt,
            Silk.NET.GLFW.Keys.AltRight => Keys.RightAlt,
            Silk.NET.GLFW.Keys.SuperLeft => Keys.LeftWin,
            Silk.NET.GLFW.Keys.SuperRight => Keys.RightWin,

            // special
            Silk.NET.GLFW.Keys.Space => Keys.Space,
            Silk.NET.GLFW.Keys.Enter => Keys.Enter,
            Silk.NET.GLFW.Keys.Escape => Keys.Escape,
            Silk.NET.GLFW.Keys.Tab => Keys.Tab,
            Silk.NET.GLFW.Keys.Backspace => Keys.Backspace,
            Silk.NET.GLFW.Keys.CapsLock => Keys.CapsLock,
            Silk.NET.GLFW.Keys.ScrollLock => Keys.ScrollLock,
            Silk.NET.GLFW.Keys.NumLock => Keys.NumLock,
            Silk.NET.GLFW.Keys.PrintScreen => Keys.PrintScreen,
            Silk.NET.GLFW.Keys.Pause => Keys.Pause,
            Silk.NET.GLFW.Keys.Menu => Keys.Menu,

            // symbols
            Silk.NET.GLFW.Keys.Apostrophe => Keys.Apostrophe,
            Silk.NET.GLFW.Keys.Comma => Keys.Comma,
            Silk.NET.GLFW.Keys.Minus => Keys.Minus,
            Silk.NET.GLFW.Keys.Period => Keys.Period,
            Silk.NET.GLFW.Keys.Slash => Keys.Slash,
            Silk.NET.GLFW.Keys.Semicolon => Keys.Semicolon,
            Silk.NET.GLFW.Keys.Equal => Keys.Equal,
            Silk.NET.GLFW.Keys.LeftBracket => Keys.LeftBracket,
            Silk.NET.GLFW.Keys.BackSlash => Keys.Backslash,
            Silk.NET.GLFW.Keys.RightBracket => Keys.RightBracket,
            Silk.NET.GLFW.Keys.GraveAccent => Keys.GraveAccent,

            // keypad
            Silk.NET.GLFW.Keys.Keypad0 => Keys.Numpad0,
            Silk.NET.GLFW.Keys.Keypad1 => Keys.Numpad1,
            Silk.NET.GLFW.Keys.Keypad2 => Keys.Numpad2,
            Silk.NET.GLFW.Keys.Keypad3 => Keys.Numpad3,
            Silk.NET.GLFW.Keys.Keypad4 => Keys.Numpad4,
            Silk.NET.GLFW.Keys.Keypad5 => Keys.Numpad5,
            Silk.NET.GLFW.Keys.Keypad6 => Keys.Numpad6,
            Silk.NET.GLFW.Keys.Keypad7 => Keys.Numpad7,
            Silk.NET.GLFW.Keys.Keypad8 => Keys.Numpad8,
            Silk.NET.GLFW.Keys.Keypad9 => Keys.Numpad9,
            Silk.NET.GLFW.Keys.KeypadDecimal => Keys.NumpadDecimal,
            Silk.NET.GLFW.Keys.KeypadDivide => Keys.NumpadDivide,
            Silk.NET.GLFW.Keys.KeypadMultiply => Keys.NumpadMultiply,
            Silk.NET.GLFW.Keys.KeypadSubtract => Keys.NumpadSubtract,
            Silk.NET.GLFW.Keys.KeypadAdd => Keys.NumpadAdd,
            Silk.NET.GLFW.Keys.KeypadEnter => Keys.NumpadEnter,
            Silk.NET.GLFW.Keys.KeypadEqual => Keys.NumpadEqual,

            _ => Keys.unknown
        };

        public static Keys MouseKey(Silk.NET.GLFW.MouseButton button) => button switch
        {
            Silk.NET.GLFW.MouseButton.Left => Keys.MouseLeft,
            Silk.NET.GLFW.MouseButton.Right => Keys.MouseRight,
            Silk.NET.GLFW.MouseButton.Middle => Keys.MouseMiddle,
            Silk.NET.GLFW.MouseButton.Button4 => Keys.MouseButton4,
            Silk.NET.GLFW.MouseButton.Button5 => Keys.MouseButton5,
            Silk.NET.GLFW.MouseButton.Button6 => Keys.MouseButton6,
            Silk.NET.GLFW.MouseButton.Button7 => Keys.MouseButton7,
            Silk.NET.GLFW.MouseButton.Button8 => Keys.MouseButton8,
            _ => Keys.unknown
        };
    }

    public class KeybindComparer : IEqualityComparer<Keybind>
    {
        bool IEqualityComparer<Keybind>.Equals(Keybind? x, Keybind? y)
        {
            if(x == null || y == null) return false;

            //if (x.keyboardKey != null && y.keyboardKey != null)
            //{
            if (x.keyboardKey == y.keyboardKey) return true;
            else return false;
            //}
            /*else if (x.mouseButton != null && y.mouseButton != null)
            {
                if (x.mouseButton == y.mouseButton) return true;
                else return false;
            }*/

            return false;
        }
        
        int IEqualityComparer<Keybind>.GetHashCode(Keybind obj)
        {
            //if (obj.mouseButton != null) return obj.mouseButton.GetHashCode();
            if (obj.keyboardKey != null) return obj.keyboardKey.GetHashCode();
            return base.GetHashCode();
        }
    }


    [A_XSDElement("KeybindMap", "Input", "Input")]
    public unsafe class InputHandler : IXMLParser
    {
        public static InputHandler instance { get; private set; }

        private static HashSet<Keybind> keysDown = new HashSet<Keybind>(new KeybindComparer());
        public static Vector2D<float> mousePos = new Vector2D<float>(0, 0);

        [A_XSDElementProperty("Keybind", "Input")]
        public List<Keybind> keybindActions = new List<Keybind>();

        public bool IsKeyDown(Keybind k) => keysDown.Contains(k);

        public InputHandler()
        {
            //instance = this;
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
                keysDown.Add(new Keybind(Keybind.MouseKey(button)));
            }
            else
            {
                keysDown.Remove(new Keybind(Keybind.MouseKey(button)));
            }
        }

        internal void ProcessKeyboard(WindowHandle* window, Silk.NET.GLFW.Keys key, int _scanCode, InputAction action, KeyModifiers mods)
        {
            if (action == InputAction.Press || action == InputAction.Repeat)
            {
                keysDown.Add(new Keybind(Keybind.MapKey(key)));
            }
            else
            {
                keysDown.Remove(new Keybind(Keybind.MapKey(key)));
            }
        }

        public void ActivateKeybinds()
        {
            foreach (Keybind keybind in keysDown)
            {
                Console.WriteLine($"Keybind {keybind.keyboardKey} is down. There are {instance.keybindActions.Count} keybinds registered");
                if (instance.keybindActions.Any(k => k.keyboardKey == keybind.keyboardKey))
                    keybindActions.First(k => k.keyboardKey == keybind.keyboardKey).action?.Invoke();
            }
        }

        public static object ParseXML(string xml)
        {
            string path = Paths.XMLDOCUMENTS + "\\" + xml;
            instance = new InputHandler();
            XElement root = XElement.Load(path);
            XNamespace ns = root.GetDefaultNamespace();
            foreach (XElement keybindElement in root.Elements())
            {
                foreach (var attribute in keybindElement.Attributes())
                {
                    if (attribute.Name == "Button")
                    {
                        Keybind keybind = new Keybind((Keys)Enum.Parse(typeof(Keys), attribute.Value));
                        instance.keybindActions.Add(keybind);
                    }
                    else if (attribute.Name == "Action")
                    {
                        MethodInfo? methodInfo = AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(a => a.GetTypes())
                            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                            .FirstOrDefault(m =>
                                    m.GetCustomAttributes(typeof(A_XSDActionDependencyAttribute), false).Any() &&
                                    string.Equals(m.Name, attribute.Value, StringComparison.OrdinalIgnoreCase));

                        if (methodInfo == null)
                            throw new Exception($"Action method '{attribute.Value}' not found in A_XSDActionDependency.");

                        Action actionDelegate = (Action)Delegate.CreateDelegate(typeof(Action), methodInfo);
                        instance.keybindActions.Last().AddAction(actionDelegate);
                    }
                }
            }
            Console.WriteLine($"Loaded {instance.keybindActions.Count} keybinds from {xml}");
            return instance;
        }
    }
}
