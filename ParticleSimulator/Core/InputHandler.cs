using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork.Serialization;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.EngineWork
{
    [A_XSDType("Keys", "Input")]
    public enum Keys
    {
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        //a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p, q, r, s, t, u, v, w, x, y, z,
        Apostrophe, Comma, Minus, Period, Slash, Semicolon, Equal, LeftBracket, Backslash, RightBracket, GraveAccent,
        Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9, NumpadDecimal, NumpadDivide, NumpadMultiply, NumpadSubtract, NumpadAdd, NumpadEnter, NumpadEqual,
        Num0, Num1, Num2, Num3, Num4, Num5, Num6, Num7, Num8, Num9,
        Space, Enter, Tab,
        AnySymbol,
        Backspace, Escape, LeftControl, RightControl, LeftShift, RightShift, LeftWin, RightWin, Menu, CapsLock, ScrollLock, NumLock, PrintScreen, Pause,
        LeftAlt, RightAlt, LeftSuper, RightSuper,
        Up, Down, Left, Right, Home, End, PageUp, PageDown, Insert, Delete,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24, F25,
        MouseLeft, MouseRight, MouseMiddle, MouseButton4, MouseButton5, MouseButton6, MouseButton7, MouseButton8,
        unknown,
    }

    [A_XSDType("Keystate", "Input")]
    public enum KeyState
    {
        Pressed,
        Released,
        Held,
        Unknown
    }

    [A_XSDType("Keybind", "Input")]
    public class Keybind
    {
        [A_XSDElementProperty("Button", "Input")]
        public Keys button;
        [A_XSDElementProperty("Action", "Input")]
        public Action action;
        [A_XSDElementProperty("State", "Input")]
        public KeyState state;
        [A_XSDElementProperty("OnTick", "Input")]
        public bool onTick = false;
        public double repeatWatch = 0;
        public bool isRepeating = false;

        [Obsolete("Currently legacy, here only for input solving error handling.")]
        public Keybind(Keys key)
        {
            button = key;
        }

        public Keybind(Keys key, KeyState state)
        {
            button = key;
            this.state = state;
        }

        public Keybind(Keys button, Action action, KeyState state)
        {
            this.button = button;
            this.action = action;
            this.state = state;
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

            // lowercase
            //silk.net.glfw.keys.

            //numbers
            Silk.NET.GLFW.Keys.Number0 => Keys.Num0,
            Silk.NET.GLFW.Keys.Number1 => Keys.Num1,
            Silk.NET.GLFW.Keys.Number2 => Keys.Num2,
            Silk.NET.GLFW.Keys.Number3 => Keys.Num3,
            Silk.NET.GLFW.Keys.Number4 => Keys.Num4,
            Silk.NET.GLFW.Keys.Number5 => Keys.Num5,
            Silk.NET.GLFW.Keys.Number6 => Keys.Num6,
            Silk.NET.GLFW.Keys.Number7 => Keys.Num7,
            Silk.NET.GLFW.Keys.Number8 => Keys.Num8,
            Silk.NET.GLFW.Keys.Number9 => Keys.Num9,

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

        public static KeyState MapState(InputAction action) => action switch
        {
            InputAction.Press => KeyState.Pressed,
            InputAction.Release => KeyState.Released,
            InputAction.Repeat => KeyState.Held,
            _ => KeyState.Unknown
        };

        public static bool IsCharacter(Keys key) => key switch
        {
            Keys.A or Keys.B or Keys.C or Keys.D or Keys.E or Keys.F or Keys.G or
            Keys.H or Keys.I or Keys.J or Keys.K or Keys.L or Keys.M or Keys.N or
            Keys.O or Keys.P or Keys.Q or Keys.R or Keys.S or Keys.T or Keys.U or
            Keys.V or Keys.W or Keys.X or Keys.Y or Keys.Z or
            Keys.Num0 or Keys.Num1 or Keys.Num2 or Keys.Num3 or Keys.Num4 or Keys.Num5 or
            Keys.Num6 or Keys.Num7 or Keys.Num8 or Keys.Num9 or
            Keys.Space or Keys.Enter or Keys.Tab or
            Keys.Apostrophe or Keys.Comma or Keys.Minus or Keys.Period or Keys.Slash or
            Keys.Semicolon or Keys.Equal or Keys.LeftBracket or Keys.Backslash or Keys.RightBracket or Keys.GraveAccent or
            Keys.Numpad0 or Keys.Numpad1 or Keys.Numpad2 or Keys.Numpad3 or Keys.Numpad4 or
            Keys.Numpad5 or Keys.Numpad6 or Keys.Numpad7 or Keys.Numpad8 or Keys.Numpad9 or
            Keys.NumpadDecimal or Keys.NumpadDivide or Keys.NumpadMultiply or Keys.NumpadSubtract or Keys.NumpadAdd or Keys.NumpadEnter or Keys.NumpadEqual
            => true,
            _ => false
        };
    }

    public class KeybindComparer : IEqualityComparer<Keybind>
    {
        bool IEqualityComparer<Keybind>.Equals(Keybind? x, Keybind? y)
        {
            if(x == null || y == null) return false;

            //if (x.keyboardKey != null && y.keyboardKey != null)
            //{
            if (x.button == y.button && x.action == y.action) return true;
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
            if (obj.button != null) return obj.button.GetHashCode();
            return base.GetHashCode();
        }
    }

    public interface ICharacterInput
    {
        public void HandleInput(char character);
    }

    [A_XSDType("KeybindMap", "Input")]
    public unsafe class InputHandler : IXMLParser<InputHandler>, IBootstrap
    {
        public static InputHandler instance { get; set; }

        public static char lastCharInput = '\0';
        public static Queue<char> charInputWriteQueue = new Queue<char>();
        private static Queue<char> charInputReadQueue = new Queue<char>();

        public static Queue<Keybind> inputWriteQueue = new Queue<Keybind>();
        private static Queue<Keybind> inputReadQueue = new Queue<Keybind>();
        public static float repeatDelay = 0.35f; // seconds before a held key starts repeating
        public static float repeatRate = 0.01f; // seconds between repeats after the initial delay

        private static HashSet<Keybind> keysDownRead = new HashSet<Keybind>(new KeybindComparer());
        public static HashSet<Keybind> keysDownWrite = new HashSet<Keybind>(new KeybindComparer());
        public static Vector2D<float> mousePos = new Vector2D<float>(0, 0);

        [A_XSDElementProperty("Keybind", "Input")]
        public List<Keybind> activeKeybindActions = new List<Keybind>();

        public static Dictionary<string, List<Keybind>> keybindGroups = new Dictionary<string, List<Keybind>>();


        public bool IsKeyDown(Keybind k) => keysDownRead.Contains(k);

        public InputHandler()
        {}

        internal void ProcessCharInput(WindowHandle* window, uint codepoint)
        {
            lastCharInput = (char)codepoint;
            charInputWriteQueue.Enqueue((char)codepoint);
        }

        internal void ProcessMouseMove(WindowHandle* window, double xPos, double yPos)
        {
            mousePos.X = (float)xPos;
            mousePos.Y = (float)yPos;
        }

        internal void ProcessMouseClick(WindowHandle* window, MouseButton button, InputAction action, KeyModifiers mods)
        {
            if (keysDownWrite.Add(new Keybind(Keybind.MouseKey(button), Keybind.MapState(action))))
            {
                inputWriteQueue.Enqueue(new Keybind(Keybind.MouseKey(button), Keybind.MapState(action)));
            }
            else
            {
                inputWriteQueue.Enqueue(new Keybind(Keybind.MouseKey(button), Keybind.MapState(action)));
            }
        }

        internal void ProcessKeyboard(WindowHandle* window, Silk.NET.GLFW.Keys key, int _scanCode, InputAction state, KeyModifiers mods)
        {
            if(keysDownWrite.Add(new Keybind(Keybind.MapKey(key), Keybind.MapState(state))))
            {
                inputWriteQueue.Enqueue(new Keybind(Keybind.MapKey(key), Keybind.MapState(state)));
            }
            else
            {
                inputWriteQueue.Enqueue(new Keybind(Keybind.MapKey(key), Keybind.MapState(state)));
            }
        }

        public void ActivateKeybinds()
        {
            lock (keysDownRead)
            {
                lock (inputWriteQueue)
                {
                    (inputWriteQueue, inputReadQueue) = (inputReadQueue, inputWriteQueue);
                    (keysDownRead, keysDownWrite) = (keysDownWrite, keysDownRead);
                }
                lock (charInputWriteQueue)
                {
                    (charInputWriteQueue, charInputReadQueue) = (charInputReadQueue, charInputWriteQueue);
                }
            }

            foreach (Keybind keybind in inputReadQueue)
            {
                if(Keybind.IsCharacter(keybind.button))
                {
                    foreach (Keybind any in activeKeybindActions.Where(k => k.button == Keys.AnySymbol && k.state == keybind.state))
                    {
                        ActivateKeyByState(any);
                    }
                }
                ActivateKeyByState(keybind);
            }
            inputReadQueue.Clear();
            keysDownRead.Clear();
            charInputReadQueue.Clear();
        }

        private void ActivateKeyByState(Keybind keybind)
        {
            switch (keybind.state)
            {
                case KeyState.Pressed:
                    {
                        Keybind k = activeKeybindActions.FirstOrDefault(k => k.button == keybind.button && k.state == KeyState.Pressed);
                        if (k != null)
                        {
                            k.action?.Invoke();
                        }
                        break;
                    }
                case KeyState.Released:
                    {
                        Keybind k = activeKeybindActions.FirstOrDefault(k => k.button == keybind.button && k.state == KeyState.Released);
                        if (k != null)
                        {
                            k.action?.Invoke();
                            k.repeatWatch = 0;
                            k.isRepeating = false;
                        }
                        break;
                    }
                case KeyState.Held:
                    {
                        Keybind k = activeKeybindActions.FirstOrDefault(k => k.button == keybind.button && k.state == KeyState.Held);
                        if (k != null)
                        {
                            if (!k.isRepeating)
                            {
                                k.repeatWatch += Engine.deltaTime.TotalSeconds;
                                if (k.repeatWatch >= repeatDelay)
                                {
                                    k.isRepeating = true;
                                    k.repeatWatch = 0;
                                    k.action?.Invoke();
                                }
                            }
                            else
                            {
                                k.repeatWatch += Engine.deltaTime.TotalSeconds;
                                if (k.repeatWatch >= repeatRate)
                                {
                                    k.repeatWatch = 0;
                                    k.action?.Invoke();
                                }
                            }
                        }
                    }
                    break;
            }
        }

        public static InputHandler ParseXML(string xmlName)
        {
            InputHandler handler = new InputHandler();
            foreach (string path in Directory.GetFiles(Paths.XMLDOCUMENTS_INPUTS, "*.xml"))
            {
                XElement root = XElement.Load(path);
                XNamespace ns = root.GetDefaultNamespace();
                List<Keybind> keybinds = new List<Keybind>();

                foreach (XElement keybindElement in root.Elements())
                {
                    var attributes = keybindElement.Attributes().ToList();

                    var buttonAttr = attributes.FirstOrDefault(a => a.Name == "Button");
                    var actionAttr = attributes.FirstOrDefault(a => a.Name == "Action");
                    var stateAttr = attributes.FirstOrDefault(a => a.Name == "State");

                    KeyState state = stateAttr.Value switch
                    {
                        "Pressed" => KeyState.Pressed,
                        "Released" => KeyState.Released,
                        "Held" => KeyState.Held,
                        _ => KeyState.Unknown
                    };

                    Console.WriteLine($"Found Attributes of Button: {buttonAttr}, action: {actionAttr}, state: {stateAttr}");

                    MethodInfo? methodInfo = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                        .FirstOrDefault(m =>
                                m.GetCustomAttributes(typeof(A_XSDActionDependencyAttribute), false).Any() &&
                                string.Equals(m.Name,
                                actionAttr.Value, StringComparison.OrdinalIgnoreCase));

                    if (methodInfo == null)
                        throw new Exception($"Action method '{actionAttr.Value}' not found in A_XSDActionDependency.");

                    Action actionDelegate = (Action)Delegate.CreateDelegate(typeof(Action), methodInfo);

                    Keybind keybind = new Keybind((Keys)Enum.Parse(typeof(Keys), buttonAttr.Value), actionDelegate, state);
                    keybinds.Add(keybind);
                    Console.WriteLine($"Created keybind of: {keybind.button}, action: {keybind.action}, state: {keybind.state}");
                }
                keybindGroups.Add(Path.GetFileNameWithoutExtension(path), keybinds);
            }
            return handler;
        }

        public static void SetActiveKeybindGroup(string groupName)
        {
            if (keybindGroups.ContainsKey(groupName))
            {
                instance.activeKeybindActions = keybindGroups[groupName];
            }
            else
            {
                Console.WriteLine($"Keybind group '{groupName}' not found.");
            }
        }

        [A_BootstrapStage(BootstrapStage.PreGPUAPI)]
        public static void Bootstrap(BootstrapStage? stage)
        {
            instance = ParseXML("InputMap.xml");
            Engine.inputHandler = instance;
        }
    }
}