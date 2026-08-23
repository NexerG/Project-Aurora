using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    // Shows a bind's combo, and on press hands the next key press to whoever is listening instead of
    // to the keybinds.
    [A_XSDType("KeyCapture", "UI")]
    public class KeyCaptureControl : ButtonControl
    {
        // caption
        private const int captionSize = 13;
        private const string captionColorHex = "#E6E6E6";
        private const string listeningCaption = "press a key...";

        private readonly LabelControl label = new LabelControl
        {
            fontSize = captionSize,
            controlColorHex = captionColorHex
        };

        public Action<Keys, List<Keys>>? onCaptured;

        public KeyCaptureControl()
        {
            AddChild(label);
        }

        public void SetCombo(Keys trigger, IEnumerable<Keys> modifiers) =>
            label.text = Describe(trigger, modifiers);

        public static string Describe(Keys trigger, IEnumerable<Keys> modifiers)
        {
            string mods = string.Join(" + ", modifiers);
            return mods.Length == 0 ? trigger.ToString() : $"{mods} + {trigger}";
        }

        // Closing the screen mid-capture would otherwise leave every keybind swallowed.
        public override void OnDestroy()
        {
            if (InputHandler.isCapturing) InputHandler.CancelCapture();
            base.OnDestroy();
        }

        public override void ResolveOnRelease()
        {
            base.ResolveOnRelease();
            if (InputHandler.isCapturing) return;

            label.text = listeningCaption;
            InputHandler.Capture((trigger, modifiers) =>
            {
                SetCombo(trigger, modifiers);
                onCaptured?.Invoke(trigger, modifiers);
            });
        }
    }
}
