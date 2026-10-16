using Silk.NET.GLFW;
using System.Numerics;

namespace ArctisAurora.EngineWork.Rendering
{
    // The desktop input backend: GLFW's callbacks, posted to InputHandler as engine events.
    internal static unsafe class GlfwInput
    {
        internal static void Wire(RenderWindow window)
        {
            window.os.SetCursorPosCallback(MouseMove);
            window.os.SetMouseButtonCallback(MouseClick);
            window.os.SetKeyCallback(Keyboard);
            window.os.SetCharCallback(CharInput);
            window.os.SetMouseOnWindowCallback(window.MouseCrossedBorder);
            window.os.SetScrollCallback(ScrollWheel);
        }

        private static void CharInput(WindowHandle* window, uint codepoint) =>
            InputHandler.Post(new RawInputEvent { kind = InputEventKind.Text, codepoint = codepoint, window = Engine.WindowFor(window), timestamp = Engine.totalTime });

        private static void MouseMove(WindowHandle* window, double xPos, double yPos)
        {
            RenderWindow target = Engine.WindowFor(window);
            if (target == null) return;

            InputHandler.Post(new RawInputEvent { kind = InputEventKind.PointerMove, position = new Vector2((float)xPos, (float)yPos), window = target, timestamp = Engine.totalTime });
        }

        private static void MouseClick(WindowHandle* window, MouseButton button, InputAction action, KeyModifiers mods)
        {
            if (action != InputAction.Press && action != InputAction.Release) return;

            InputHandler.Post(new RawInputEvent
            {
                kind = InputEventKind.Button,
                key = MouseKey(button),
                action = action == InputAction.Press ? RawAction.Down : RawAction.Up,
                window = Engine.WindowFor(window),
                timestamp = Engine.totalTime
            });
        }

        private static void Keyboard(WindowHandle* window, Silk.NET.GLFW.Keys key, int _scanCode, InputAction action, KeyModifiers mods)
        {
            if (action != InputAction.Press && action != InputAction.Release) return;

            InputHandler.Post(new RawInputEvent
            {
                kind = InputEventKind.Key,
                key = MapKey(key),
                action = action == InputAction.Press ? RawAction.Down : RawAction.Up,
                window = Engine.WindowFor(window),
                timestamp = Engine.totalTime
            });
        }

        private static void ScrollWheel(WindowHandle* window, double offsetX, double offsetY)
        {
            RenderWindow target = Engine.WindowFor(window);
            if (target == null) return;

            InputHandler.Post(new RawInputEvent { kind = InputEventKind.Scroll, delta = new Vector2((float)offsetX, (float)offsetY), window = target, timestamp = Engine.totalTime });
        }

        private static Keys MapKey(Silk.NET.GLFW.Keys glfwkey) => glfwkey switch
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
            Silk.NET.GLFW.Keys.SuperLeft => Keys.LeftSuper,
            Silk.NET.GLFW.Keys.SuperRight => Keys.RightSuper,

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

        private static Keys MouseKey(MouseButton button) => button switch
        {
            MouseButton.Left => Keys.MouseLeft,
            MouseButton.Right => Keys.MouseRight,
            MouseButton.Middle => Keys.MouseMiddle,
            MouseButton.Button4 => Keys.MouseButton4,
            MouseButton.Button5 => Keys.MouseButton5,
            MouseButton.Button6 => Keys.MouseButton6,
            MouseButton.Button7 => Keys.MouseButton7,
            MouseButton.Button8 => Keys.MouseButton8,
            _ => Keys.unknown
        };
    }
}
