using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Interactable;
using ArctisAurora.Core.UISystem.Controls.Text.Document.Edits;
using ArctisAurora.Core.UISystem.Controls.Text.Editing;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // The format bar for whichever note holds the caret. It owns no editor: every button resolves
    // the focused one when it is pressed, the same walk the keybinds use. That is why nothing in
    // here may take the active control — the walk starts at the caret's run, and a button that stole
    // it would leave the bar acting on itself.
    [A_XSDType("DocumentToolbar", "UI")]
    public class DocumentToolbarControl : StackPanelControl
    {
        // palette
        private const string barColorHex = "#1e1e1e";
        private const string hoverHex = "#2A2A2A";
        private const string pressHex = "#3A3A3A";
        private const string idleInkHex = "#9D9D9D";
        private const string activeInkHex = "#6C9BE0";
        private const string separatorHex = "#2F2F2F";
        private const string fieldHex = "#252525";

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

        // Default is the colour a fresh run carries, so picking it lets the note drop the attribute
        // again rather than pinning white into the file.
        private static readonly (string caption, string hex)[] colorOptions =
        {
            ("Default", "#FFFFFF"),
            ("Gray", "#9D9D9D"),
            ("Red", "#E06C75"),
            ("Orange", "#D19A66"),
            ("Yellow", "#E5C07B"),
            ("Green", "#98C379"),
            ("Blue", "#61AFEF"),
            ("Purple", "#C678DD")
        };

        private readonly IconControl boldInk;
        private readonly IconControl italicInk;
        private readonly LabelControl stylingCaption;
        private readonly LabelControl colorInk;
        private readonly PxBox pxField;

        // The last editor that held the caret. Only the px field uses it: a field has to take the
        // active control to be typed into, which is the one thing the rest of the bar avoids, so the
        // press that focuses it has already ended the walk that would find the note. Every other
        // control resolves live, and so cannot act on a note the caret has left.
        private DocumentEditorControl? remembered;

        // What the px field is pointed at, captured on the press that focuses it and held until it
        // commits or is abandoned. No range means the note had nothing selected, and a size with
        // nothing to put it on changes nothing.
        private DocumentEditorControl? pxTarget;
        private bool pxRanged;
        private DocumentAddress pxFrom;
        private DocumentAddress pxTo;

        // what the children were last told, so a tick that changes nothing writes nothing — the
        // colour setter has no equality guard and this runs every frame
        private bool? shownBold;
        private bool? shownItalic;
        private TextStyleType? shownStyling;
        private string? shownColor;
        private int? shownPx;

        public override bool takesActiveControl => false;

        public DocumentToolbarControl()
        {
            orientation = Orientation.Horizontal;
            Spacing = 2f;
            preferredHeight = barHeight;
            horizontalAlignment = HorizontalAlignment.Stretch;
            controlColorHex = barColorHex;

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

            pxField = new PxBox
            {
                preferredWidth = 40,
                preferredHeight = 20,
                fontSize = captionSize,
                textColorHex = idleInkHex,
                controlColorHex = fieldHex
            };
            pxField.onPress = CapturePx;
            pxField.onCommit = ApplyPx;
            pxField.onCancel = RevertPx;
            pxField.onBlur = AbandonPx;
            AddChild(pxField);
        }

        // Reflects the run the selection starts in. Cheap enough to poll: four comparisons, and a
        // write only when one of them moved.
        public override void OnTick()
        {
            base.OnTick();

            DocumentEditorControl live = TextInputActions.FocusedEditor();
            if (live != null) remembered = live;

            // Reflecting the remembered editor rather than the live one is what keeps the bar
            // showing the note while the px field holds the focus.
            DocumentEditorControl? editor = live ?? remembered;
            CaretStyle? source = editor?.StyleSource;

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
                colorInk.controlColorHex = color;
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
        // The press that focuses the field. The note is still the one the active control just left,
        // so this is the last moment its selection can be read.
        private void CapturePx()
        {
            pxTarget = remembered;
            pxRanged = pxTarget != null && pxTarget.SelectedRange(out pxFrom, out pxTo);
        }

        // Enter. A size out of range or unparseable changes nothing; either way the note gets the
        // caret back.
        private void ApplyPx(string value)
        {
            DocumentEditorControl? target = pxTarget;
            bool ranged = pxRanged;
            DocumentAddress from = pxFrom;
            DocumentAddress to = pxTo;
            EndPx();

            if (target != null && int.TryParse(value, out int px) && px >= minPx && px <= maxPx)
            {
                if (ranged) target.ApplyStyleTo(from, to, new StyleDelta(fontSize: px));
                else target.ArmStyle(new StyleDelta(fontSize: px));
            }

            target?.FocusCaret();
        }

        // Escape.
        private void RevertPx()
        {
            DocumentEditorControl? target = pxTarget;
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

        private static void Reflect(IconControl ink, bool on, ref bool? shown)
        {
            if (shown == on) return;

            shown = on;
            ink.controlColorHex = on ? activeInkHex : idleInkHex;
        }

        #region ---- menus ----
        // The editor is captured here rather than re-resolved inside the entry: a context menu item
        // is a plain button and does take the active control, so by the time an entry runs the walk
        // would start at the menu instead of the note.
        private static void OpenStyling(ToolButton owner)
        {
            DocumentEditorControl editor = TextInputActions.FocusedEditor();
            if (editor == null) return;

            List<ContextEntry> entries = new List<ContextEntry>();
            foreach ((string caption, TextStyleType type) in stylingOptions)
            {
                TextStyleType picked = type;
                entries.Add(new ContextEntry(caption, () => editor.SetBlockStyling(picked), true, false));
            }

            Drop(owner, entries);
        }

        private static void OpenColors(ToolButton owner)
        {
            DocumentEditorControl editor = TextInputActions.FocusedEditor();
            if (editor == null) return;

            List<ContextEntry> entries = new List<ContextEntry>();
            foreach ((string caption, string hex) in colorOptions)
            {
                string picked = hex;
                entries.Add(new ContextEntry(caption, () =>
                {
                    editor.ApplyStyle(new StyleDelta(colorHex: picked));
                    editor.FocusCaret();
                }, true, false));
            }

            Drop(owner, entries);
        }

        private static void Drop(ToolButton owner, List<ContextEntry> entries) =>
            ContextMenus.OpenList(owner, entries,
                new Vector2D<float>(owner.arrangedRect.x, owner.arrangedRect.Bottom));

        private static string CaptionFor(TextStyleType type)
        {
            foreach ((string caption, TextStyleType option) in stylingOptions)
                if (option == type) return caption;

            return stylingOptions[0].caption;
        }
        #endregion

        #region ---- parts ----
        private static ToolButton NewButton(int width, Action<ToolButton> onPress)
        {
            ToolButton button = new ToolButton
            {
                preferredWidth = width,
                preferredHeight = barHeight,
                controlColorHex = barColorHex,
                hoverColorHex = hoverHex,
                pressColorHex = pressHex
            };
            button.onPress = onPress;
            return button;
        }

        private static ToolButton IconButton(IconControl ink, Action<ToolButton> onPress)
        {
            ToolButton button = NewButton(iconButtonWidth, onPress);
            button.AddChild(ink);
            return button;
        }

        // A caption plus its chevron is two controls, and a button takes one — so they travel in a
        // row of their own. The row is masked out: a StackPanel paints, unlike the text containers,
        // and an opaque one here would sit over the button's hover tint.
        private static ToolButton CaptionButton(LabelControl caption, int width, Action<ToolButton> onPress)
        {
            StackPanelControl row = new StackPanelControl
            {
                orientation = Orientation.Horizontal,
                Spacing = 4f,
                maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible")
            };
            row.AddChild(caption);
            row.AddChild(Ink("chevron-down"));

            ToolButton button = NewButton(width, onPress);
            button.AddChild(row);
            return button;
        }

        // Colour before text: the text setter is what builds the glyphs, and it hands them whatever
        // colour is already standing.
        private static LabelControl Caption(string text, string hex) => new LabelControl
        {
            fontSize = captionSize,
            controlColorHex = hex,
            text = text
        };

        private static IconControl Ink(string icon) => new IconControl
        {
            setName = "default",
            iconName = icon,
            preferredWidth = iconSize,
            preferredHeight = iconSize,
            controlColorHex = idleInkHex
        };

        private static PanelControl Separator() => new PanelControl
        {
            preferredWidth = 1,
            preferredHeight = barHeight,
            controlColorHex = separatorHex
        };
        #endregion

        // The one control in the bar that does take the active control, because it cannot be typed
        // into otherwise. The press is the hook rather than the focus, since the collision handler
        // hands the context over before the click reaches the box.
        private class PxBox : TextBoxControl
        {
            public Action? onPress;

            public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
            {
                base.ResolveOnClick(oldPos, delta);
                onPress?.Invoke();
            }
        }

        // Acts on press, not release: with the active control left alone, SolveLMBRelease's
        // "the press target is what is active" gate never matches and no release arrives.
        private class ToolButton : ButtonControl
        {
            public Action<ToolButton>? onPress;

            public override bool takesActiveControl => false;

            public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
            {
                base.ResolveOnClick(oldPos, delta);
                onPress?.Invoke(this);
            }
        }
    }
}
