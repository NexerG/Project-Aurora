using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UI
{
    // Asks for a note's name. One window per process, built on the first ask and hidden between them
    // — a swapchain per prompt would put the build cost inside the gesture.
    public static class NextNoteNameWindow
    {
        private const string windowName = "next-note-name";
        private const uint windowWidth = 380;
        private const uint windowHeight = 140;
        private const int discardWidth = 100;

        // palette, matching the app chrome
        private const string groundHex = "#252525";
        private const string promptHex = "#CCCCCC";
        private const string fieldGroundHex = "#1B1B1B";
        private const string buttonHex = "#3A3A3A";
        private const string buttonHoverHex = "#4A4A4A";
        private const string buttonPressHex = "#2A2A2A";

        private static RenderWindow _window = null!;
        private static RenderWindow? _source;
        private static Control? _restoreActive;
        private static NextTextBoxControl _field = null!;
        private static NextButtonControl _discard = null!;

        private static Action<string>? _onConfirm;
        private static Action? _onDiscard;
        private static Action? _onCancel;

        public static bool isOpen { get; private set; }

        // A prompt at a time. A second ask while one is up would strand the first one's callbacks.
        // Without an onDiscard there is nothing to discard — an explicit save has no such answer —
        // so the button is left out rather than duplicating Cancel.
        public static unsafe void Ask(RenderWindow source, string suggestion, Action<string> onConfirm,
            Action? onDiscard, Action? onCancel)
        {
            if (isOpen || source == null) return;

            Build();
            _source = source;
            _onConfirm = onConfirm;
            _onDiscard = onDiscard;
            _onCancel = onCancel;

            // Hiding collapses what a control draws but still reserves its slot, so the width goes
            // with it or the row keeps a gap where the button was.
            _discard.preferredWidth = onDiscard != null ? discardWidth : 1;
            if (onDiscard != null) _discard.Show(); else _discard.Hide();

            _field.text = suggestion ?? string.Empty;

            AGlfwWindow._glfw.GetWindowPos(source.os.handle, out int sx, out int sy);
            AGlfwWindow._glfw.GetWindowSize(source.os.handle, out int sw, out int sh);
            _window.os.SetPosition(sx + (sw - (int)windowWidth) / 2, sy + (sh - (int)windowHeight) / 2);

            _window.os.Show();
            _window.os.Focus();
            _window.os.SeedIsInWindow();

            // The field has to hold the active context before a click reaches it, or the keystrokes
            // meant for the prompt land in whatever was last clicked — the note being closed.
            _restoreActive = UIEngine.activeControl;
            UIEngine.SetActiveControl(_field);
            _field.Focus();

            isOpen = true;
        }

        private static void Confirm()
        {
            string name = _field.text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            Action<string>? confirm = _onConfirm;
            Hide();
            confirm?.Invoke(name);
        }

        private static void Discard()
        {
            Action? discard = _onDiscard;
            Hide();
            discard?.Invoke();
        }

        private static void Cancel()
        {
            Action? cancel = _onCancel;
            Hide();
            cancel?.Invoke();
        }

        // Cleared before the callbacks, so a handler that opens another prompt is not refused by the
        // one it was called from.
        private static void Hide()
        {
            if (!isOpen) return;

            _window.os.Hide();
            isOpen = false;
            _onConfirm = null;
            _onDiscard = null;
            _onCancel = null;

            UIEngine.SetActiveControl(_restoreActive);
            _restoreActive = null;

            _source?.os.Focus();
            _source = null;
        }

        private static void Build()
        {
            if (_window != null) return;

            _window = Engine.OpenMenuWindow(windowName, windowWidth, windowHeight);
            _window.isActivable = true;

            WindowRoot root = new WindowRoot();
            root.AddChild(Content());
            _window.uiNext.uiRoot = root;

            _window.os.Resize(windowWidth, windowHeight);
            root.FitTo(new Extent2D(windowWidth, windowHeight));
        }

        private static Control Content()
        {
            NextPanelControl ground = new NextPanelControl
            {
                colorHex = groundHex,
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };

            NextStackPanelControl column = new NextStackPanelControl
            {
                orientation = NextStackPanelControl.Orientation.Vertical,
                alpha = 0f,
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch,
                padding = new Thickness(14),
                Spacing = 10
            };

            column.AddChild(new NextLabelControl
            {
                text = "Name this note",
                fontSize = 15,
                colorHex = promptHex,
                preferredHeight = 20,
                horizontalPosition = 0f
            });

            _field = new NextTextBoxControl
            {
                preferredHeight = 30,
                horizontalAlignment = HorizontalAlignment.Stretch,
                colorHex = fieldGroundHex,
                textColorHex = "#EAEAEA",
                fontSize = 15,
                padding = new Thickness(0, 6, 0, 8)
            };
            _field.onCommit = _ => Confirm();
            _field.onCancel = Cancel;
            column.AddChild(_field);

            NextStackPanelControl buttons = new NextStackPanelControl
            {
                orientation = NextStackPanelControl.Orientation.Horizontal,
                alpha = 0f,
                preferredHeight = 30,
                Spacing = 8
            };

            // The row fills the column, so the buttons are pushed right by a star child rather than
            // by aligning the row itself.
            buttons.AddChild(new NextPanelControl { widthStar = 1, alpha = 0f });
            _discard = Button("Don't save", Discard, discardWidth);
            buttons.AddChild(_discard);
            buttons.AddChild(Button("Cancel", Cancel));
            buttons.AddChild(Button("Save", Confirm));
            column.AddChild(buttons);

            ground.AddChild(column);
            return ground;
        }

        private static NextButtonControl Button(string caption, Action action, int width = 90)
        {
            NextButtonControl button = new NextButtonControl
            {
                preferredWidth = width,
                preferredHeight = 30,
                colorHex = buttonHex,
                hoverColorHex = buttonHoverHex,
                pressColorHex = buttonPressHex,
                cornerRadius = new CornerRadii(4)
            };
            button.AddChild(new NextLabelControl { text = caption, fontSize = 14, colorHex = promptHex });
            button.RegisterOnRelease(_ => { action(); return true; });
            return button;
        }
    }
}
