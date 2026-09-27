using ArctisAurora.Core.ECS.EngineEntity;
using System.Globalization;

namespace ArctisAurora.Core.UI
{
    // A note's properties as rows: dates, palette, layout, and a Markdown note's other frontmatter keys.
    public class NotePropertiesControl : StackPanelControl
    {
        // metrics
        private const int captionSize = 13;
        private const float keyWidth = 110f;
        private const float paletteWidth = 180f;
        private const float rowHeight = 22f;

        private const string appPalette = "App default";

        private readonly DocumentEditorControl editor;
        private readonly bool markdown;

        public override bool takesActiveControl => false;

        public NotePropertiesControl(DocumentEditorControl editor, bool markdown)
        {
            this.editor = editor;
            this.markdown = markdown;
            orientation = Orientation.Vertical;
            Spacing = 4f;
            padding = new Thickness(0f, 8f);
            horizontalAlignment = HorizontalAlignment.Stretch;
            alpha = 0f;
            Refresh();
        }

        // Rebuilds every row from the note.
        public void Refresh()
        {
            foreach (Entity child in children.ToList()) child.Destroy();

            RichTextDocument document = editor.activeDocument;
            Row("Created", Caption(Date(document.created)));
            Row("Modified", Caption(Date(document.modified)));

            DropdownControl palette = new DropdownControl
            {
                options = new[] { appPalette }.Concat(Palettes.Names).ToArray(),
                selected = document.palette ?? appPalette,
                preferredWidth = paletteWidth,
                preferredHeight = rowHeight,
                role = PaletteRole.Field
            };
            palette.onPicked = picked => editor.SetPalette(picked == appPalette ? null : picked);
            Row("Palette", palette);

            DocumentLayout layout = document.layout;
            Row("Line height", Number(layout.lineHeight, 0.5f, (l, v) => l.lineHeight = v));
            Row("Block spacing", Number(layout.blockSpacing, 0f, (l, v) => l.blockSpacing = v));
            Row("List indent", Number(layout.listIndent, 0f, (l, v) => l.listIndent = v));

            if (!markdown) return;

            foreach (Frontmatter.Entry entry in Frontmatter.Entries(document.frontmatter))
            {
                if (MarkdownFormat.IsProperty(entry.key)) continue;

                string key = entry.key;
                Row(key, entry.editable
                    ? Field(entry.value, value => { editor.SetFrontmatterValue(key, value); return true; })
                    : Caption(entry.value));
            }
        }

        public override bool OnPointerPress(PointerEvent e) => true;

        public override bool OnPointerTap(PointerEvent e) => true;

        #region ---- parts ----
        private void Row(string key, Control value)
        {
            PropertyRow row = new PropertyRow
            {
                orientation = Orientation.Horizontal,
                Spacing = 8f,
                preferredHeight = rowHeight,
                horizontalAlignment = HorizontalAlignment.Stretch,
                alpha = 0f
            };

            LabelControl caption = Caption(key);
            caption.preferredWidth = keyWidth;
            caption.horizontalPosition = 0f;
            caption.role = PaletteRole.MutedInk;
            row.AddChild(caption);

            if (value.preferredWidth <= 0f) value.widthStar = 1f;
            value.horizontalPosition = 0f;
            row.AddChild(value);
            AddChild(row);
        }

        private static LabelControl Caption(string text) => new LabelControl
        {
            fontSize = captionSize,
            role = PaletteRole.Ink,
            hitTestable = false,
            text = text
        };

        // A field that applies on Enter or when the focus leaves it; a value apply refuses is put back.
        private TextBoxControl Field(string text, Func<string, bool> apply)
        {
            TextBoxControl box = new TextBoxControl
            {
                text = text,
                fontSize = captionSize,
                preferredHeight = rowHeight,
                role = PaletteRole.Field
            };

            string shown = text;
            void Apply(string value)
            {
                if (value == shown) return;
                if (apply(value)) shown = value;
                else box.text = shown;
            }

            box.onCommit = value =>
            {
                Apply(value);
                editor.FocusCaret();
            };
            box.onCancel = editor.FocusCaret;
            box.onBlur = () => Apply(box.text);
            return box;
        }

        private TextBoxControl Number(float current, float min, Action<DocumentLayout, float> change) =>
            Field(current.ToString(CultureInfo.InvariantCulture), value =>
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || parsed < min)
                    return false;

                DocumentLayout layout = editor.activeDocument.layout.Clone();
                change(layout, parsed);
                editor.SetLayout(layout);
                return true;
            });

        private static string Date(string? stamp) =>
            DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset time)
                ? time.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : stamp ?? "-";

        // A press on a row's gap leaves the caret's note active.
        private class PropertyRow : StackPanelControl
        {
            public override bool takesActiveControl => false;
        }
        #endregion
    }
}
