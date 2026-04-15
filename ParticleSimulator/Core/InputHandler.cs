using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Filing.Serialization;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Reflection;
using System.Xml.Linq;
using static ArctisAurora.EngineWork.KeyStateTracker;

namespace ArctisAurora.EngineWork
{
    public enum RawAction
    {
        Down,
        Up
    }

    public struct RawInputEvent
    {
        public Keys key;
        public RawAction action;
        public double timestamp;

        public RawInputEvent(Keys key, RawAction action, double timestamp)
        {
            this.key = key;
            this.action = action;
            this.timestamp = timestamp;
        }
    }

    public class KeyStateEntry
    {
        public Keys key;
        public bool isDown;
        public double downTimestamp;
        public double upTimestamp;
        public double holdDuration;
        public int tapCount;
        public double lastTapTimestamp;
        public bool consumed;

        // Per-frame flags
        public bool justPressed;
        public bool justReleased;

        public void ResetFrame()
        {
            justPressed = false;
            justReleased = false;
            consumed = false;
        }
    }

    public class KeyStateTracker
    {
        private Dictionary<Keys, KeyStateEntry> _states = new Dictionary<Keys, KeyStateEntry>();
        private List<RawInputEvent> _writeQueue = new List<RawInputEvent>();
        private List<RawInputEvent> _readQueue = new List<RawInputEvent>();
        private readonly object _lock = new object();

        public double tapWindow = 0.3;

        public KeyStateEntry GetState(Keys key)
        {
            _states.TryGetValue(key, out KeyStateEntry entry);
            return entry;
        }

        public bool IsDown(Keys key)
        {
            if (_states.TryGetValue(key, out KeyStateEntry entry))
                return entry.isDown;
            return false;
        }

        public double HoldDuration(Keys key)
        {
            if (_states.TryGetValue(key, out KeyStateEntry entry) && entry.isDown)
                return entry.holdDuration;
            return 0;
        }

        // Called from GLFW callback thread
        public void EnqueueEvent(Keys key, RawAction action, double timestamp)
        {
            lock (_lock)
            {
                _writeQueue.Add(new RawInputEvent(key, action, timestamp));
            }
        }

        // Called once per tick on main thread
        public void Update(double currentTime, double deltaTime)
        {
            lock (_lock)
            {
                (_writeQueue, _readQueue) = (_readQueue, _writeQueue);
            }

            foreach (KeyStateEntry entry in _states.Values)
                entry.ResetFrame();

            for (int i = 0; i < _readQueue.Count; i++)
            {
                RawInputEvent evt = _readQueue[i];
                KeyStateEntry entry = GetOrCreate(evt.key);

                if (evt.action == RawAction.Down)
                {
                    if (!entry.isDown)
                    {
                        entry.justPressed = true;
                        entry.isDown = true;
                        entry.downTimestamp = evt.timestamp;
                        entry.holdDuration = 0;

                        if (evt.timestamp - entry.lastTapTimestamp < tapWindow)
                            entry.tapCount++;
                        else
                            entry.tapCount = 1;

                        entry.lastTapTimestamp = evt.timestamp;

                        // Mirror to AnySymbol
                        if (IsCharacterKey(evt.key))
                            MirrorToAnySymbol(evt.timestamp);
                    }
                }
                else
                {
                    if (entry.isDown)
                    {
                        entry.justReleased = true;
                        entry.isDown = false;
                        entry.upTimestamp = evt.timestamp;

                        // Release AnySymbol only if no other character keys are held
                        if (IsCharacterKey(evt.key) && !AnyCharacterKeyHeld())
                            ReleaseAnySymbol(evt.timestamp);
                    }
                }
            }
            _readQueue.Clear();

            foreach (KeyStateEntry entry in _states.Values)
            {
                if (entry.isDown)
                    entry.holdDuration += deltaTime;

                if (!entry.isDown && currentTime - entry.lastTapTimestamp > tapWindow)
                    entry.tapCount = 0;
            }
        }

        private void MirrorToAnySymbol(double timestamp)
        {
            KeyStateEntry any = GetOrCreate(Keys.AnySymbol);
            any.justPressed = true;
            any.isDown = true;
            any.downTimestamp = timestamp;
            any.holdDuration = 0;

            if (timestamp - any.lastTapTimestamp < tapWindow)
                any.tapCount++;
            else
                any.tapCount = 1;

            any.lastTapTimestamp = timestamp;
        }

        private void ReleaseAnySymbol(double timestamp)
        {
            KeyStateEntry any = GetOrCreate(Keys.AnySymbol);
            any.justReleased = true;
            any.isDown = false;
            any.upTimestamp = timestamp;
        }

        private bool AnyCharacterKeyHeld()
        {
            foreach (KeyStateEntry entry in _states.Values)
            {
                if (entry.key != Keys.AnySymbol && entry.isDown && IsCharacterKey(entry.key))
                    return true;
            }
            return false;
        }

        private static bool IsCharacterKey(Keys key)
        {
            return key >= Keys.A && key <= Keys.Z ||
                   key >= Keys.Num0 && key <= Keys.Num9 ||
                   key >= Keys.Numpad0 && key <= Keys.NumpadEqual ||
                   key == Keys.Space || key == Keys.Enter || key == Keys.Tab ||
                   key == Keys.Apostrophe || key == Keys.Comma || key == Keys.Minus ||
                   key == Keys.Period || key == Keys.Slash || key == Keys.Semicolon ||
                   key == Keys.Equal || key == Keys.LeftBracket || key == Keys.Backslash ||
                   key == Keys.RightBracket || key == Keys.GraveAccent;
        }

        public enum ConditionResult
        {
            Idle,       // not relevant yet
            Ongoing,    // in progress, don't fire yet
            Triggered,  // fire now
            Canceled    // was ongoing, now failed
        }

        [A_XSDType("Condition", "Input", description: "Base input condition type")]
        public abstract class InputCondition : IKeybindChild
        {
            // Called every tick. Returns the current state of this condition.
            public abstract ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime);

            // Called when the keybind fires or resets
            public abstract void Reset();
        }

        // ──────────────────────────────────────────────
        // PRESS — fires the frame the key goes down
        // ──────────────────────────────────────────────
        [A_XSDType("Press", "Input", description: "Fires the frame the key goes down")]
        public class PressCondition : InputCondition
        {
            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (trigger.justPressed)
                    return ConditionResult.Triggered;
                return ConditionResult.Idle;
            }

            public override void Reset() { }
        }

        // ──────────────────────────────────────────────
        // RELEASE — fires the frame the key goes up
        // ──────────────────────────────────────────────
        [A_XSDType("Release", "Input", description: "Fires the frame the key goes up")]
        public class ReleaseCondition : InputCondition
        {
            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (trigger.justReleased)
                    return ConditionResult.Triggered;
                return ConditionResult.Idle;
            }

            public override void Reset() { }
        }

        // ──────────────────────────────────────────────
        // HOLD — fires once after holding for threshold
        // ──────────────────────────────────────────────
        [A_XSDType("Hold", "Input", description: "Fires once after holding for threshold seconds")]
        public class HoldCondition : InputCondition
        {
            [A_XSDElementProperty("Threshold", "Input")]
            public float threshold { get; set; } = 0.3f;

            private bool _fired;

            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (!trigger.isDown)
                {
                    if (_fired)
                    {
                        _fired = false;
                        return ConditionResult.Idle;
                    }
                    return ConditionResult.Idle;
                }

                if (_fired)
                    return ConditionResult.Idle;

                if (trigger.holdDuration >= threshold)
                {
                    _fired = true;
                    return ConditionResult.Triggered;
                }

                return ConditionResult.Ongoing;
            }

            public override void Reset() { _fired = false; }
        }

        // ──────────────────────────────────────────────
        // HOLD RELEASE — fires on release, only if held
        // longer than threshold
        // ──────────────────────────────────────────────
        [A_XSDType("HoldRelease", "Input", description: "Fires on release only if held longer than threshold")]
        public class HoldReleaseCondition : InputCondition
        {
            [A_XSDElementProperty("Threshold", "Input")]
            public float threshold { get; set; } = 0.3f;

            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (trigger.justReleased)
                {
                    double heldFor = trigger.upTimestamp - trigger.downTimestamp;
                    if (heldFor >= threshold)
                        return ConditionResult.Triggered;
                    return ConditionResult.Canceled;
                }

                if (trigger.isDown)
                    return ConditionResult.Ongoing;

                return ConditionResult.Idle;
            }

            public override void Reset() { }
        }

        // ──────────────────────────────────────────────
        // MAX HOLD TIME — passes only if key was held
        // LESS than threshold (combine with Release)
        // ──────────────────────────────────────────────
        [A_XSDType("MaxHoldTime", "Input", description: "Passes only if key was held less than threshold. Combine with Release")]
        public class MaxHoldTimeCondition : InputCondition
        {
            [A_XSDElementProperty("Threshold", "Input")]
            public float threshold { get; set; } = 0.5f;

            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (trigger.justReleased)
                {
                    double heldFor = trigger.upTimestamp - trigger.downTimestamp;
                    if (heldFor < threshold)
                        return ConditionResult.Triggered;
                    return ConditionResult.Canceled;
                }

                if (trigger.isDown)
                {
                    if (trigger.holdDuration >= threshold)
                        return ConditionResult.Canceled;
                    return ConditionResult.Ongoing;
                }

                return ConditionResult.Idle;
            }

            public override void Reset() { }
        }

        // ──────────────────────────────────────────────
        // MULTI TAP — fires on Nth consecutive press
        // within tap window
        // ──────────────────────────────────────────────
        [A_XSDType("MultiTap", "Input", description: "Fires on Nth consecutive press within tap window")]
        public class MultiTapCondition : InputCondition
        {
            [A_XSDElementProperty("Count", "Input")]
            public int count { get; set; } = 2;

            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (trigger.justPressed)
                {
                    if (trigger.tapCount >= count)
                    {
                        trigger.tapCount = 0;
                        return ConditionResult.Triggered;
                    }
                    return ConditionResult.Ongoing;
                }

                if (trigger.tapCount > 0 && trigger.tapCount < count)
                    return ConditionResult.Ongoing;

                return ConditionResult.Idle;
            }

            public override void Reset() { }
        }

        // ──────────────────────────────────────────────
        // CONTINUOUS — fires every tick while held
        // ──────────────────────────────────────────────
        [A_XSDType("Continuous", "Input", description: "Fires every tick while held")]
        public class ContinuousCondition : InputCondition
        {
            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (trigger.isDown)
                    return ConditionResult.Triggered;
                return ConditionResult.Idle;
            }

            public override void Reset() { }
        }

        // ──────────────────────────────────────────────
        // REPEAT — fires on press, then repeats after
        // delay at rate
        // ──────────────────────────────────────────────
        [A_XSDType("Repeat", "Input", description: "Fires on press then repeats after delay at rate")]
        public class RepeatCondition : InputCondition
        {
            [A_XSDElementProperty("Delay", "Input")]
            public float delay { get; set; } = 0.35f;

            [A_XSDElementProperty("Rate", "Input")]
            public float rate { get; set; } = 0.03f;

            private double _accumulator;
            private bool _delayPassed;

            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (trigger.justPressed)
                {
                    _accumulator = 0;
                    _delayPassed = false;
                    return ConditionResult.Triggered;
                }

                if (!trigger.isDown)
                {
                    _accumulator = 0;
                    _delayPassed = false;
                    return ConditionResult.Idle;
                }

                _accumulator += deltaTime;

                if (!_delayPassed)
                {
                    if (_accumulator >= delay)
                    {
                        _delayPassed = true;
                        _accumulator = 0;
                        return ConditionResult.Triggered;
                    }
                    return ConditionResult.Ongoing;
                }
                else
                {
                    if (_accumulator >= rate)
                    {
                        _accumulator = 0;
                        return ConditionResult.Triggered;
                    }
                    return ConditionResult.Ongoing;
                }
            }

            public override void Reset()
            {
                _accumulator = 0;
                _delayPassed = false;
            }
        }

        // ──────────────────────────────────────────────
        // TOGGLE — alternates active/inactive on press
        // ──────────────────────────────────────────────
        [A_XSDType("Toggle", "Input", description: "Alternates active/inactive on each press")]
        public class ToggleCondition : InputCondition
        {
            public bool isActive;

            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (trigger.justPressed)
                {
                    isActive = !isActive;
                    return ConditionResult.Triggered;
                }
                return ConditionResult.Idle;
            }

            public override void Reset() { isActive = false; }
        }

        // ──────────────────────────────────────────────
        // HOLD CONTINUOUS — fires every tick, but only
        // after holding for threshold
        // ──────────────────────────────────────────────
        [A_XSDType("HoldContinuous", "Input", description: "Fires every tick but only after holding for threshold")]
        public class HoldContinuousCondition : InputCondition
        {
            [A_XSDElementProperty("Threshold", "Input")]
            public float threshold { get; set; } = 0.3f;

            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (!trigger.isDown)
                    return ConditionResult.Idle;

                if (trigger.holdDuration >= threshold)
                    return ConditionResult.Triggered;

                return ConditionResult.Ongoing;
            }

            public override void Reset() { }
        }

        // ──────────────────────────────────────────────
        // CHORD — requires another key to be held
        // (alternative to modifier list, usable as condition)
        // ──────────────────────────────────────────────
        [A_XSDType("Chord", "Input", description: "Requires another key to be held simultaneously")]
        public class ChordCondition : InputCondition
        {
            [A_XSDElementProperty("Key", "Input")]
            public Keys key { get; set; }

            public override ConditionResult Evaluate(KeyStateEntry trigger, KeyStateTracker tracker, double deltaTime)
            {
                if (tracker.IsDown(key))
                    return ConditionResult.Triggered;
                return ConditionResult.Canceled;
            }

            public override void Reset() { }
        }

        private KeyStateEntry GetOrCreate(Keys key)
        {
            if (!_states.TryGetValue(key, out KeyStateEntry entry))
            {
                entry = new KeyStateEntry();
                entry.key = key;
                _states.Add(key, entry);
            }
            return entry;
        }
    }

    [A_XSDType("KeybindModifier", "Input", description: "Key that must be held for the keybind to activate")]
    public class KeybindModifier : IKeybindChild
    {
        [A_XSDElementProperty("Key", "Input")]
        public Keys key { get; set; }
    }

    [A_XSDType("Keybind", "Input", AllowedChildren = typeof(IKeybindChild), Description = "Maps a trigger key with optional modifiers and conditions to an action")]
    public class KeybindDefinition
    {
        [A_XSDElementProperty("Trigger", "Input")]
        public Keys trigger { get; set; }

        [A_XSDElementProperty("Action", "Input")]
        public Action action { get; set; }

        [A_XSDElementProperty("Modifier", "Input")]
        public List<KeybindModifier> modifiers = new List<KeybindModifier>();

        // Conditions are child elements — parsed by RecursiveParse
        public List<InputCondition> conditions = new List<InputCondition>();

        // If no conditions specified, default to Press
        public bool hasConditions => conditions.Count > 0;

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

        public static Keys MouseKey(MouseButton button) => button switch
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

    public class GestureMatcher
    {
        private KeyStateTracker _tracker;
        private string _activeGroup = "default";
        private Dictionary<string, List<KeybindDefinition>> _groups = new Dictionary<string, List<KeybindDefinition>>();
        private List<KeybindDefinition> _activeBinds = new List<KeybindDefinition>();

        // Default condition when none specified
        private static PressCondition _defaultPress = new PressCondition();

        public GestureMatcher(KeyStateTracker tracker)
        {
            _tracker = tracker;
        }

        public void SetActiveGroup(string group)
        {
            _activeGroup = group;
            if (_groups.TryGetValue(group, out List<KeybindDefinition> binds))
                _activeBinds = binds;
            else
                _activeBinds = new List<KeybindDefinition>();
        }

        public void AddKeybind(string group, KeybindDefinition def)
        {
            if (!_groups.TryGetValue(group, out List<KeybindDefinition> list))
            {
                list = new List<KeybindDefinition>();
                _groups.Add(group, list);
            }
            list.Add(def);

            if (group == _activeGroup)
                _activeBinds = list;
        }

        public void Update(double deltaTime)
        {
            // Track which trigger keys have been consumed this frame
            // to prevent less-specific binds from also firing
            HashSet<Keys> consumedTriggers = null;

            // Process in order of specificity (most modifiers first)
            // Sort once when group changes, not every frame — but for correctness here:
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < _activeBinds.Count; i++)
                {
                    KeybindDefinition def = _activeBinds[i];

                    // Pass 0: only process binds with modifiers
                    // Pass 1: only process binds without modifiers
                    bool hasModifiers = def.modifiers.Count > 0;
                    if (pass == 0 && !hasModifiers) continue;
                    if (pass == 1 && hasModifiers) continue;

                    // Skip if a more specific bind already consumed this trigger
                    if (consumedTriggers != null && consumedTriggers.Contains(def.trigger))
                    {
                        // But only skip if this bind has fewer modifiers
                        // (same modifier count = different combo, allow it)
                        if (IsShadowed(def))
                            continue;
                    }

                    // Check trigger key exists in tracker
                    KeyStateEntry triggerState = _tracker.GetState(def.trigger);
                    if (triggerState == null) continue;

                    // Check all modifiers are held
                    bool modsHeld = true;
                    for (int m = 0; m < def.modifiers.Count; m++)
                    {
                        if (!_tracker.IsDown(def.modifiers[m].key))
                        {
                            modsHeld = false;
                            break;
                        }
                    }
                    if (!modsHeld) continue;

                    // Evaluate conditions
                    ConditionResult result = EvaluateConditions(def, triggerState, deltaTime);

                    if (result == ConditionResult.Triggered)
                    {
                        def.action?.Invoke();
                        triggerState.consumed = true;

                        if (consumedTriggers == null)
                            consumedTriggers = new HashSet<Keys>();
                        consumedTriggers.Add(def.trigger);
                    }
                }
            }
        }

        private ConditionResult EvaluateConditions(KeybindDefinition def, KeyStateEntry trigger, double deltaTime)
        {
            // No conditions = default Press behavior
            if (!def.hasConditions)
            {
                return _defaultPress.Evaluate(trigger, _tracker, deltaTime);
            }

            // ALL conditions must agree.
            // Triggered requires ALL to be Triggered.
            // If ANY is Canceled, result is Canceled.
            // If ANY is Ongoing (and none Canceled), result is Ongoing.
            // Otherwise Idle.

            bool allTriggered = true;
            bool anyCanceled = false;
            bool anyOngoing = false;

            for (int c = 0; c < def.conditions.Count; c++)
            {
                ConditionResult cr = def.conditions[c].Evaluate(trigger, _tracker, deltaTime);

                if (cr == ConditionResult.Canceled)
                {
                    anyCanceled = true;
                    allTriggered = false;
                }
                else if (cr == ConditionResult.Ongoing)
                {
                    anyOngoing = true;
                    allTriggered = false;
                }
                else if (cr == ConditionResult.Idle)
                {
                    allTriggered = false;
                }
            }

            if (anyCanceled)
            {
                // Reset all conditions when canceled
                for (int c = 0; c < def.conditions.Count; c++)
                    def.conditions[c].Reset();
                return ConditionResult.Canceled;
            }

            if (allTriggered)
            {
                // Reset after firing
                for (int c = 0; c < def.conditions.Count; c++)
                    def.conditions[c].Reset();
                return ConditionResult.Triggered;
            }

            if (anyOngoing)
                return ConditionResult.Ongoing;

            return ConditionResult.Idle;
        }

        private bool IsShadowed(KeybindDefinition def)
        {
            for (int i = 0; i < _activeBinds.Count; i++)
            {
                KeybindDefinition other = _activeBinds[i];
                if (other == def) continue;
                if (other.trigger != def.trigger) continue;
                if (other.modifiers.Count <= def.modifiers.Count) continue;

                // Check other's modifiers are a superset
                bool isSuperset = true;
                for (int m = 0; m < def.modifiers.Count; m++)
                {
                    bool found = false;
                    for (int n = 0; n < other.modifiers.Count; n++)
                    {
                        if (other.modifiers[n].key == def.modifiers[m].key)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found) { isSuperset = false; break; }
                }
                if (!isSuperset) continue;

                // Check all of other's modifiers are currently held
                bool allHeld = true;
                for (int m = 0; m < other.modifiers.Count; m++)
                {
                    if (!_tracker.IsDown(other.modifiers[m].key))
                    {
                        allHeld = false;
                        break;
                    }
                }
                if (allHeld) return true;
            }
            return false;
        }

        // Suppress char input when a modified keybind fired this frame
        public bool ShouldSuppressCharInput()
        {
            for (int i = 0; i < _activeBinds.Count; i++)
            {
                KeybindDefinition def = _activeBinds[i];
                if (def.modifiers.Count == 0) continue;

                KeyStateEntry state = _tracker.GetState(def.trigger);
                if (state != null && state.consumed)
                    return true;
            }
            return false;
        }
    }
    // -------------------------------

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

    public interface IKeybindChild { }


    [A_XSDType("KeybindMap", "Input", AllowedChildren = typeof(KeybindDefinition), Description = "Root container for keybind definitions")]
    public unsafe class InputHandler : IXMLParser<InputHandler>
    {
        public static InputHandler instance { get; set; }

        public KeyStateTracker keyTracker = new KeyStateTracker();
        public GestureMatcher gestureMatcher;

        public static char lastCharInput = '\0';
        public static Queue<char> charInputWriteQueue = new Queue<char>();
        public static Queue<char> charInputReadQueue = new Queue<char>();
        public static Vector2D<float> mousePos = new Vector2D<float>(0, 0);
        public static Vector2D<float> scrollDelta = new Vector2D<float>(0, 0);
        private static Vector2D<float> scrollDeltaWrite = new Vector2D<float>(0, 0);

        [A_XSDElementProperty("Keybind", "Input")]
        public List<KeybindDefinition> keybindDefinitions = new List<KeybindDefinition>();

        public bool IsKeyDown(Keys k) => keyTracker.IsDown(k);

        public InputHandler()
        {
            gestureMatcher = new GestureMatcher(keyTracker);
        }

        #region ---- Input callbacks ----
        internal void ProcessCharInput(WindowHandle* window, uint codepoint)
        {
            char c = (char)codepoint;
            if (c != '\0')
                charInputWriteQueue.Enqueue(c);
        }

        internal void ProcessMouseMove(WindowHandle* window, double xPos, double yPos)
        {
            mousePos.X = (float)xPos;
            mousePos.Y = (float)yPos;
        }

        internal void ProcessMouseClick(WindowHandle* window, MouseButton button, InputAction action, KeyModifiers mods)
        {
            Keys key = KeybindDefinition.MouseKey(button);

            if (action == InputAction.Press)
                keyTracker.EnqueueEvent(key, RawAction.Down, Engine.totalTime);
            else if (action == InputAction.Release)
                keyTracker.EnqueueEvent(key, RawAction.Up, Engine.totalTime);
        }

        internal void ProcessKeyboard(WindowHandle* window, Silk.NET.GLFW.Keys key, int _scanCode, InputAction action, KeyModifiers mods)
        {
            Keys mapped = KeybindDefinition.MapKey(key);
            if (action == InputAction.Press)
                keyTracker.EnqueueEvent(mapped, RawAction.Down, Engine.totalTime);
            else if (action == InputAction.Release)
                keyTracker.EnqueueEvent(mapped, RawAction.Up, Engine.totalTime);
        }

        internal void ProcessScrollWheel(WindowHandle* window, double offsetX, double offsetY)
        {
            scrollDeltaWrite.X += (float)offsetX;
            scrollDeltaWrite.Y += (float)offsetY;
        }
        #endregion

        public void ActivateKeybinds()
        {
            lock (charInputWriteQueue)
            {
                (charInputWriteQueue, charInputReadQueue) = (charInputReadQueue, charInputWriteQueue);
            }
            scrollDelta = scrollDeltaWrite;
            scrollDeltaWrite = new Vector2D<float>(0, 0);

            // Update key states from raw GLFW events
            keyTracker.Update(Engine.totalTime, Engine.deltaTime.TotalSeconds);

            // Evaluate all gesture keybinds
            gestureMatcher.Update(Engine.deltaTime.TotalSeconds);

            // Suppress char input if a modifier combo fired
            if (gestureMatcher.ShouldSuppressCharInput())
                charInputReadQueue.Clear();
        }

        public static InputHandler ParseXML(string xmlName)
        {
            InputHandler handler = new InputHandler();

            foreach (string path in Directory.GetFiles(Paths.XMLDOCUMENTS_INPUTS, "*.xml"))
            {
                XElement root = XElement.Load(path);
                XNamespace ns = root.GetDefaultNamespace();
                string groupName = Path.GetFileNameWithoutExtension(path);

                foreach (XElement keybindElement in root.Elements())
                {
                    KeybindDefinition def = new KeybindDefinition();

                    // Trigger key
                    XAttribute triggerAttr = keybindElement.Attribute("Trigger");
                    if (triggerAttr != null)
                        def.trigger = (Keys)Enum.Parse(typeof(Keys), triggerAttr.Value);

                    // Action — resolve via A_XSDActionDependency
                    XAttribute actionAttr = keybindElement.Attribute("Action");
                    if (actionAttr != null)
                    {
                        MethodInfo methodInfo = AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(a => a.GetTypes())
                            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                            .FirstOrDefault(m =>
                                m.GetCustomAttributes(typeof(A_XSDActionDependencyAttribute), false).Length > 0 &&
                                string.Equals(m.Name, actionAttr.Value, StringComparison.OrdinalIgnoreCase));

                        if (methodInfo == null)
                            throw new Exception($"Action method '{actionAttr.Value}' not found in A_XSDActionDependency.");

                        def.action = (Action)Delegate.CreateDelegate(typeof(Action), methodInfo);
                    }

                    // Child elements: Modifiers and Conditions
                    foreach (XElement child in keybindElement.Elements())
                    {
                        string localName = child.Name.LocalName;

                        if (localName == "Modifier")
                        {
                            KeybindModifier mod = new KeybindModifier();
                            XAttribute keyAttr = child.Attribute("Key");
                            if (keyAttr != null)
                                mod.key = (Keys)Enum.Parse(typeof(Keys), keyAttr.Value);
                            def.modifiers.Add(mod);
                        }
                        else
                        {
                            // It's a condition — resolve type via XSD
                            InputCondition condition = CreateCondition(localName, child);
                            if (condition != null)
                                def.conditions.Add(condition);
                        }
                    }

                    handler.gestureMatcher.AddKeybind(groupName, def);
                }
            }
            return handler;
        }

        private static InputCondition CreateCondition(string typeName, XElement element)
        {
            InputCondition condition = typeName switch
            {
                "Press" => new KeyStateTracker.PressCondition(),
                "Release" => new KeyStateTracker.ReleaseCondition(),
                "Hold" => new KeyStateTracker.HoldCondition(),
                "HoldRelease" => new KeyStateTracker.HoldReleaseCondition(),
                "MaxHoldTime" => new KeyStateTracker.MaxHoldTimeCondition(),
                "MultiTap" => new KeyStateTracker.MultiTapCondition(),
                "Continuous" => new KeyStateTracker.ContinuousCondition(),
                "Repeat" => new KeyStateTracker.RepeatCondition(),
                "Toggle" => new KeyStateTracker.ToggleCondition(),
                "HoldContinuous" => new KeyStateTracker.HoldContinuousCondition(),
                "Chord" => new KeyStateTracker.ChordCondition(),
                _ => null
            };

            if (condition == null)
            {
                Console.WriteLine($"Unknown condition type: {typeName}");
                return null;
            }

            // Parse attributes onto the condition object
            foreach (XAttribute attr in element.Attributes())
            {
                // Try property first (most condition params are properties)
                PropertyInfo prop = condition.GetType().GetProperty(
                    attr.Name.LocalName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop != null && prop.CanWrite)
                {
                    if (prop.PropertyType == typeof(float))
                        prop.SetValue(condition, float.Parse(attr.Value,
                            System.Globalization.CultureInfo.InvariantCulture));
                    else if (prop.PropertyType == typeof(int))
                        prop.SetValue(condition, int.Parse(attr.Value));
                    else if (prop.PropertyType == typeof(Keys))
                        prop.SetValue(condition, Enum.Parse(typeof(Keys), attr.Value));
                    else if (prop.PropertyType == typeof(double))
                        prop.SetValue(condition, double.Parse(attr.Value,
                            System.Globalization.CultureInfo.InvariantCulture));
                    continue;
                }

                // Fall back to field
                FieldInfo field = condition.GetType().GetField(
                    attr.Name.LocalName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (field != null)
                {
                    if (field.FieldType == typeof(float))
                        field.SetValue(condition, float.Parse(attr.Value,
                            System.Globalization.CultureInfo.InvariantCulture));
                    else if (field.FieldType == typeof(int))
                        field.SetValue(condition, int.Parse(attr.Value));
                    else if (field.FieldType == typeof(Keys))
                        field.SetValue(condition, Enum.Parse(typeof(Keys), attr.Value));
                    else if (field.FieldType == typeof(double))
                        field.SetValue(condition, double.Parse(attr.Value,
                            System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            return condition;
        }

        public static void SetActiveKeybindGroup(string groupName)
        {
            instance.gestureMatcher.SetActiveGroup(groupName);
        }

        [A_XSDActionDependency("InputHandler.LoadInputs", "Bootstrap")]
        public static void LoadInputs()
        {
            instance = ParseXML("InputMap.xml");
            Engine.inputHandler = instance;
            // Activate the default group (or first available)
            instance.gestureMatcher.SetActiveGroup("default");
        }
    }
}