using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.UI
{
    // Asks a question with two answers. One window per process, built on the first ask and hidden
    // between them, the same shape as NoteNameWindow.
    public static class ConfirmWindow
    {
        private const string windowName = "confirm";
        private const uint windowWidth = 380;
        private const uint windowHeight = 100;

        private static RenderWindow _window = null!;
        private static RenderWindow? _source;
        private static LabelControl _message = null!;

        private static Action? _onConfirm;
        private static Action? _onCancel;

        public static bool isOpen { get; private set; }

        // A prompt at a time, as with the name prompt — a second ask would strand the first one's
        // callbacks.
        public static unsafe void Ask(RenderWindow source, string message, Action onConfirm, Action? onCancel)
        {
            if (isOpen || source == null) return;

            Build();
            _source = source;
            _onConfirm = onConfirm;
            _onCancel = onCancel;
            _message.text = message ?? string.Empty;

            AGlfwWindow._glfw.GetWindowPos(source.os.handle, out int sx, out int sy);
            AGlfwWindow._glfw.GetWindowSize(source.os.handle, out int sw, out int sh);
            _window.os.SetPosition(sx + (sw - (int)windowWidth) / 2, sy + (sh - (int)windowHeight) / 2);

            _window.os.Show();
            _window.os.Focus();
            _window.os.SeedIsInWindow();

            isOpen = true;
        }

        private static void Confirm()
        {
            Action? confirm = _onConfirm;
            Hide();
            confirm?.Invoke();
        }

        private static void Cancel()
        {
            Action? cancel = _onCancel;
            Hide();
            cancel?.Invoke();
        }

        // Cleared before the callbacks, so a handler that asks again is not refused by the ask it
        // was called from.
        private static void Hide()
        {
            if (!isOpen) return;

            _window.os.Hide();
            isOpen = false;
            _onConfirm = null;
            _onCancel = null;

            _source?.os.Focus();
            _source = null;
        }

        private static void Build()
        {
            if (_window != null) return;

            _window = Engine.OpenMenuWindow(windowName, windowWidth, windowHeight);

            WindowRoot root = new WindowRoot();
            root.AddChild(Content());
            _window.ui.uiRoot = root;

            _window.os.Resize(windowWidth, windowHeight);
            root.FitTo(new Extent2D(windowWidth, windowHeight));
        }

        private static Control Content()
        {
            PanelControl ground = new PanelControl
            {
                role = PaletteRole.Surface,
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch
            };

            StackPanelControl column = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Vertical,
                alpha = 0f,
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch,
                padding = new Thickness(14),
                Spacing = 10
            };

            _message = new LabelControl
            {
                fontSize = 15,
                preferredHeight = 20,
                horizontalPosition = 0f
            };
            column.AddChild(_message);

            StackPanelControl buttons = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Horizontal,
                alpha = 0f,
                preferredHeight = 30,
                Spacing = 8
            };

            // The row fills the column, so the buttons are pushed right by a star child rather than
            // by aligning the row itself.
            buttons.AddChild(new PanelControl { widthStar = 1, alpha = 0f });
            buttons.AddChild(Button("Cancel", Cancel));
            buttons.AddChild(Button("Confirm", Confirm));
            column.AddChild(buttons);

            ground.AddChild(column);
            return ground;
        }

        private static ButtonControl Button(string caption, Action action)
        {
            ButtonControl button = new ButtonControl
            {
                preferredWidth = 90,
                preferredHeight = 30,
                role = PaletteRole.Chrome,
                cornerRadius = new CornerRadii(4)
            };
            button.AddChild(new LabelControl { text = caption, fontSize = 14, role = PaletteRole.MutedInk });
            button.RegisterOnRelease(_ => { action(); return true; });
            return button;
        }
    }
}
