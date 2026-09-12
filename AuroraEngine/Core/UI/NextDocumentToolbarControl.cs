using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // The format bar for whichever note holds the caret. It owns no editor: every button resolves
    // the focused one when it is pressed, the same walk the keybinds use. That is why nothing in
    // here may take the active control — the walk starts at the caret's block, and a button that
    // stole it would leave the bar acting on itself.
    [A_XSDType("NextDocumentToolbar", "UI")]
    public class NextDocumentToolbarControl : NextStackPanelControl
    {
        // palette
        [A_XSDElementProperty("HoverColorHex", "UI", "Ground of a bar button while hovered.")]
        public string hoverHex { get => field; set { field = value; ApplyPalette(); } } = "#2A2A2A";

        [A_XSDElementProperty("PressColorHex", "UI", "Ground of a bar button while held.")]
        public string pressHex { get => field; set { field = value; ApplyPalette(); } } = "#3A3A3A";

        [A_XSDElementProperty("IdleInkColorHex", "UI", "Color of a bar glyph that is not lit.")]
        public string idleInkHex { get => field; set { field = value; ApplyPalette(); } } = "#9D9D9D";

        [A_XSDElementProperty("ActiveInkColorHex", "UI", "Color of a bar glyph the caret's span carries.")]
        public string activeInkHex { get => field; set { field = value; ApplyPalette(); } } = "#6C9BE0";

        [A_XSDElementProperty("SeparatorColorHex", "UI", "Color of the rules between bar groups.")]
        public string separatorHex { get => field; set { field = value; ApplyPalette(); } } = "#2F2F2F";

        [A_XSDElementProperty("FieldColorHex", "UI", "Ground of the px field.")]
        public string fieldHex { get => field; set { field = value; ApplyPalette(); } } = "#252525";

        // The bar's own ground is what a button rests at, so it repaints the parts the way the rest
        // of the palette does.
        public override string colorHex
        {
            get => base.colorHex;
            set { base.colorHex = value; ApplyPalette(); }
        }

        // metrics
        private const int barHeight = 30;
        private const int iconButtonWidth = 34;
        private const int iconSize = 14;
        private const int captionSize = 13;

        // what the px field will accept
        private const int minPx = 6;
        private const int maxPx = 200;

        private static readonly (string caption, TextStyleType type)[] stylingOptions =
        {
            ("Text", TextStyleType.Text),
            ("Heading 1", TextStyleType.Heading1),
            ("Heading 2", TextStyleType.Heading2),
            ("Heading 3", TextStyleType.Heading3),
            ("Heading 4", TextStyleType.Heading4),
            ("Heading 5", TextStyleType.Heading5),
            ("Heading 6", TextStyleType.Heading6),
            ("Quote", TextStyleType.Quote),
            ("Code", TextStyleType.Code),
            ("Comment", TextStyleType.Comment)
        };

        // Default is the colour a fresh span carries, so picking it lets the note drop the attribute
        // again rather than pinning a hex into the file.
        private static readonly (string caption, string hex)[] colorOptions =
        {
            ("Default", "#2C2B26"),
            ("Gray", "#808080"),
            ("Red", "#E06C75"),
            ("Orange", "#D19A66"),
            ("Yellow", "#E5C07B"),
            ("Green", "#98C379"),
            ("Blue", "#61AFEF"),
            ("Purple", "#C678DD")
        };

        private readonly NextIconControl boldInk;
        private readonly NextIconControl italicInk;
        private readonly NextLabelControl stylingCaption;
        private readonly NextLabelControl colorInk;
        private readonly NextPxBox pxField;

        // The last editor that held the caret. Only the px field uses it: a field has to take the
        // active control to be typed into, which is the one thing the rest of the bar avoids, so the
        // press that focuses it has already ended the walk that would find the note. Every other
        // control resolves live, and so cannot act on a note the caret has left.
        private NextDocumentEditorControl remembered;

        // What the px field is pointed at, captured on the press that focuses it and held until it
        // commits or is abandoned. No range means the note had nothing selected, and a size with
        // nothing to put it on changes nothing.
        private NextDocumentEditorControl pxTarget;
        private bool pxRanged;
        private NextDocumentAddress pxFrom;
        private NextDocumentAddress pxTo;

        // what the children were last told, so a tick that changes nothing writes nothing
        private bool? shownBold;
        private bool? shownItalic;
        private TextStyleType? shownStyling;
        private string shownColor;
        private int? shownPx;

        public override bool takesActiveControl => false;

        public NextDocumentToolbarControl()
        {
            orientation = Orientation.Horizontal;
            Spacing = 2f;
            preferredHeight = barHeight;
            horizontalAlignment = HorizontalAlignment.Stretch;

            boldInk = Ink("bold");
            italicInk = Ink("italic");
            AddChild(IconButton(boldInk, _ => TextInputActions.Bold()));
            AddChild(IconButton(italicInk, _ => TextInputActions.Italic()));
            AddChild(Separator());

            stylingCaption = Caption(stylingOptions[0].caption, idleInkHex);
            AddChild(CaptionButton(stylingCaption, 124, OpenStyling));
            AddChild(Separator());

            colorInk = Caption("A", idleInkHex);
            AddChild(CaptionButton(colorInk, 52, OpenColors));
            AddChild(Separator());

            pxField = new NextPxBox
            {
                preferredWidth = 40,
                preferredHeight = 20,
                fontSize = captionSize,
                textColorHex = idleInkHex,
                colorHex = fieldHex
            };
            pxField.pressed = CapturePx;
            pxField.onCommit = ApplyPx;
            pxField.onCancel = RevertPx;
            pxField.onBlur = AbandonPx;
            AddChild(pxField);
        }

        // Reflects the span the selection starts in. Cheap enough to poll: four comparisons, and a
        // write only when one of them moved.
        public override void OnTick()
        {
            base.OnTick();

            NextDocumentEditorControl live = TextInputActions.NextEditor();
            if (live != null) remembered = live;

            // Reflecting the remembered editor rather than the live one is what keeps the bar
            // showing the note while the px field holds the focus.
            NextDocumentEditorControl editor = live ?? remembered;
            NextCaretStyle? source = editor?.StyleSource;

            Reflect(boldInk, source?.bold == true, ref shownBold);
            Reflect(italicInk, source?.italic == true, ref shownItalic);

            TextStyleType styling = editor?.CaretBlockStyling ?? TextStyleType.Text;
            if (shownStyling != styling)
            {
                shownStyling = styling;
                stylingCaption.text = CaptionFor(styling);
            }

            string color = source?.colorHex ?? idleInkHex;
            if (shownColor != color)
            {
                shownColor = color;
                Paint(colorInk, color);
            }

            // never while it is being typed into, or the reflection would fight the keystrokes
            int px = source?.fontSize ?? 0;
            if (!pxField.isEditing && shownPx != px)
            {
                shownPx = px;
                pxField.text = px > 0 ? px.ToString() : string.Empty;
            }
        }

        #region ---- px field ----
        // The press that focuses the field. The note is still the one the active control is about to
        // leave, so this is the last moment its selection can be read.
        private void CapturePx()
        {
            pxTarget = remembered;
            pxRanged = pxTarget != null && pxTarget.SelectedRange(out pxFrom, out pxTo);
        }

        // Enter. A size out of range or unparseable changes nothing; either way the note gets the
        // caret back.
        private void ApplyPx(string value)
        {
            NextDocumentEditorControl target = pxTarget;
            bool ranged = pxRanged;
            NextDocumentAddress from = pxFrom;
            NextDocumentAddress to = pxTo;
            EndPx();

            if (target != null && int.TryParse(value, out int px) && px >= minPx && px <= maxPx)
            {
                if (ranged) target.ApplyStyleTo(from, to, new NextStyleDelta(fontSize: px));
                else target.ArmStyle(new NextStyleDelta(fontSize: px));
            }

            target?.FocusCaret();
        }

        // Escape.
        private void RevertPx()
        {
            NextDocumentEditorControl target = pxTarget;
            EndPx();
            target?.FocusCaret();
        }

        // The focus went somewhere the user pointed it. The active control is theirs to place, so
        // this ends the session and touches nothing else.
        private void AbandonPx() => EndPx();

        // Ending the session first is what keeps the refocus that follows from re-entering here
        // through the field's own blur; clearing the cache is what makes the next tick rewrite the
        // field from the note.
        private void EndPx()
        {
            pxTarget = null;
            pxRanged = false;
            shownPx = null;
        }
        #endregion

        private void Reflect(NextIconControl ink, bool on, ref bool? shown)
        {
            if (shown == on) return;

            shown = on;
            ink.colorHex = on ? activeInkHex : idleInkHex;
        }

        #region ---- menus ----
        // The editor is captured here rather than re-resolved inside the entry: a menu row is a
        // plain button and does take the active control, so by the time an entry runs the walk would
        // start at the menu instead of the note.
        private static void OpenStyling(NextToolButton owner)
        {
            NextDocumentEditorControl editor = TextInputActions.NextEditor();
            if (editor == null) return;

            List<ContextMenuEntry> entries = new List<ContextMenuEntry>();
            foreach ((string caption, TextStyleType type) in stylingOptions)
            {
                TextStyleType picked = type;
                entries.Add(new ContextMenuButton(caption, () => editor.SetBlockStyling(picked)));
            }

            Drop(owner, entries);
        }

        private static void OpenColors(NextToolButton owner)
        {
            NextDocumentEditorControl editor = TextInputActions.NextEditor();
            if (editor == null) return;

            List<ContextMenuEntry> entries = new List<ContextMenuEntry>();
            foreach ((string caption, string hex) in colorOptions)
            {
                string picked = hex;
                entries.Add(new ContextMenuButton(caption, () =>
                {
                    editor.ApplyStyle(new NextStyleDelta(colorHex: picked));
                    editor.FocusCaret();
                }));
            }

            Drop(owner, entries);
        }

        private static void Drop(NextToolButton owner, List<ContextMenuEntry> entries) =>
            NextContextMenus.Open(entries, owner,
                new Vector2D<float>(owner.arrangedRect.x, owner.arrangedRect.Bottom));

        private static string CaptionFor(TextStyleType type)
        {
            foreach ((string caption, TextStyleType option) in stylingOptions)
                if (option == type) return caption;

            return stylingOptions[0].caption;
        }
        #endregion

        #region ---- parts ----
        private NextToolButton NewButton(int width, Action<NextToolButton> onToolPress)
        {
            NextToolButton button = new NextToolButton
            {
                preferredWidth = width,
                preferredHeight = barHeight,
                colorHex = colorHex,
                hoverColorHex = hoverHex,
                pressColorHex = pressHex
            };
            button.pressed = onToolPress;
            return button;
        }

        private NextToolButton IconButton(NextIconControl ink, Action<NextToolButton> onToolPress)
        {
            NextToolButton button = NewButton(iconButtonWidth, onToolPress);
            button.AddChild(ink);
            return button;
        }

        // A caption plus its chevron is two controls, and a button places one — so they travel in a
        // row of their own. The row is transparent: every control paints, and an opaque one here
        // would sit over the button's hover tint.
        private NextToolButton CaptionButton(NextLabelControl caption, int width, Action<NextToolButton> onToolPress)
        {
            NextStackPanelControl row = new NextStackPanelControl
            {
                orientation = Orientation.Horizontal,
                Spacing = 4f,
                alpha = 0f,
                hitTestable = false,
                horizontalPosition = 0.5f,
                verticalPosition = 0.5f
            };
            row.AddChild(caption);
            row.AddChild(Ink("chevron-down"));

            NextToolButton button = NewButton(width, onToolPress);
            button.AddChild(row);
            return button;
        }

        // The parts inside a button are decoration: hit-testable they would answer the press, and a
        // row resolves the active context to itself rather than to the button that owns it.
        private static NextLabelControl Caption(string text, string hex) => new NextLabelControl
        {
            fontSize = captionSize,
            colorHex = hex,
            hitTestable = false,
            text = text
        };

        private NextIconControl Ink(string icon) => new NextIconControl
        {
            setName = "default",
            iconName = icon,
            preferredWidth = iconSize,
            preferredHeight = iconSize,
            hitTestable = false,
            horizontalPosition = 0.5f,
            verticalPosition = 0.5f,
            colorHex = idleInkHex
        };

        private NextPanelControl Separator() => new NextPanelControl
        {
            preferredWidth = 1,
            preferredHeight = barHeight,
            colorHex = separatorHex
        };

        // A label's colour reaches its glyphs when its runs are built, which is a measure away.
        private static void Paint(NextLabelControl label, string hex)
        {
            label.colorHex = hex;
            label.InvalidateLayout();
        }

        // Repaints what the constructor already built, because the host's attributes arrive after it.
        private void ApplyPalette()
        {
            foreach (Entity child in children)
            {
                switch (child)
                {
                    case NextPxBox box:
                        box.colorHex = fieldHex;
                        box.textColorHex = idleInkHex;
                        break;
                    case NextToolButton button:
                        button.colorHex = colorHex;
                        button.hoverColorHex = hoverHex;
                        button.pressColorHex = pressHex;
                        InkTree(button);
                        break;
                    case NextPanelControl separator:
                        separator.colorHex = separatorHex;
                        break;
                }
            }

            // The lit ones repaint from OnTick, which only writes when its cache moved.
            shownBold = null;
            shownItalic = null;
            shownColor = null;
        }

        private void InkTree(Control control)
        {
            foreach (Entity child in control.children)
            {
                if (child is not Control visual) continue;

                if (visual is NextIconControl) visual.colorHex = idleInkHex;
                else if (visual is NextLabelControl label) Paint(label, idleInkHex);

                InkTree(visual);
            }
        }
        #endregion

        // The one control in the bar that does take the active control, because it cannot be typed
        // into otherwise. The press is the hook rather than the focus, since the active context is
        // handed over in the same phase.
        private class NextPxBox : NextTextBoxControl
        {
            public Action pressed;

            public override bool OnPointerPress(PointerEvent e)
            {
                if (e.button != PointerEvent.leftButton) return false;

                bool handled = base.OnPointerPress(e);
                pressed?.Invoke();
                return handled;
            }
        }

        // Acts on press, not release: with the active control left alone, the release's "the press
        // target is what is active" gate never matches and no release arrives.
        private class NextToolButton : NextButtonControl
        {
            public Action<NextToolButton> pressed;

            public override bool takesActiveControl => false;

            public override bool OnPointerPress(PointerEvent e)
            {
                base.OnPointerPress(e);
                if (e.button == PointerEvent.leftButton) pressed?.Invoke(this);
                return true;
            }
        }
    }
}
