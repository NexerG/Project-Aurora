using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.UI
{
    // The insertion point — a narrow filled quad, positioned by whoever owns the text. Named for
    // the outgoing CaretControl, which registers the same serializable id: Serializable.GenerateID
    // hashes the type's simple name and [@Serializable] is inherited, so two Entity subclasses
    // sharing a name collide at bootstrap. Landing 6 renames this back.
    public class NextCaretControl : Control
    {
        public const float Width = 2f;

        private const double blinkInterval = 0.53;

        // blink state
        private double phase;
        private bool focused = true;

        public NextCaretControl()
        {
            colorHex = "#FFFFFF";
        }

        public override void OnTick()
        {
            base.OnTick();
            if (!focused) return;

            phase += Engine.deltaTime.TotalSeconds;
            SetAlpha(phase % (blinkInterval * 2.0) < blinkInterval ? 1f : 0f);
        }

        // Shows the caret solid, from the top of the cycle.
        internal void Focus()
        {
            focused = true;
            phase = 0.0;
            SetAlpha(1f);
        }

        internal void Blur()
        {
            focused = false;
            SetAlpha(0f);
        }

        private void SetAlpha(float value)
        {
            if (alpha != value) alpha = value;
        }
    }
}
