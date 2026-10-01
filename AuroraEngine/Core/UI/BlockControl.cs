using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Filing;
using System.Numerics;
using System.Text;

namespace ArctisAurora.Core.UI
{
    public enum ListKind
    {
        None,
        Bullet,
        Task
    }

    // What a list item shows in its indent: a shape, or its number written some way.
    [A_XSDType("ListMarker", "UI")]
    public enum ListMarker
    {
        Disc,
        Circle,
        Triangle,
        TriangleOutline,
        Square,
        SquareOutline,
        Decimal,
        UpperAlpha,
        LowerAlpha,
        LowerRoman,
        UpperRoman
    }

    public static class ListMarkers
    {
        public static bool IsNumbered(ListMarker marker) => marker >= ListMarker.Decimal;

        // the default icon set's glyph for a shape marker
        public static string ShapeIcon(ListMarker marker) => marker switch
        {
            ListMarker.Circle => "bullet-circle",
            ListMarker.Triangle => "bullet-triangle",
            ListMarker.TriangleOutline => "bullet-triangle-outline",
            ListMarker.Square => "bullet-square",
            ListMarker.SquareOutline => "bullet-square-outline",
            _ => "bullet-disc"
        };

        // An item's number as its marker writes it, dot included.
        public static string Format(int number, ListMarker marker) => marker switch
        {
            ListMarker.UpperAlpha => Letters(number).ToUpperInvariant() + ".",
            ListMarker.LowerAlpha => Letters(number) + ".",
            ListMarker.UpperRoman => Roman(number).ToUpperInvariant() + ".",
            ListMarker.LowerRoman => Roman(number) + ".",
            _ => number + "."
        };

        // a..z, then aa, ab — bijective, so there is no zero letter
        private static string Letters(int number)
        {
            string letters = string.Empty;
            for (int n = Math.Max(1, number); n > 0; n = (n - 1) / 26)
                letters = (char)('a' + (n - 1) % 26) + letters;
            return letters;
        }

        private static readonly (int value, string numeral)[] numerals =
        {
            (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"),
            (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")
        };

        private static string Roman(int number)
        {
            if (number < 1 || number > 3999) return number.ToString();

            StringBuilder roman = new StringBuilder();
            foreach ((int value, string numeral) in numerals)
                for (; number >= value; number -= value) roman.Append(numeral);
            return roman.ToString();
        }
    }

    // A run as a note file writes it: one styled slice of a block's text. It exists at load and save
    // only — in the tree a run is a StyleSpan on the block that holds it.
    [A_XSDType("Run", "UI")]
    public class Run
    {
        [A_XSDElementProperty("Text", "UI", "The slice of the block's text this run covers.")]
        public string text { get; set; } = string.Empty;

        [A_XSDElementProperty("Bold", "UI", "Run is bold.")]
        public bool bold { get; set; }

        [A_XSDElementProperty("Italic", "UI", "Run is italic.")]
        public bool italic { get; set; }

        [A_XSDElementProperty("Strikethrough", "UI", "Run is struck through.")]
        public bool strikethrough { get; set; }

        [A_XSDElementProperty("Underline", "UI", "Run is underlined.")]
        public bool underline { get; set; }

        [A_XSDElementProperty("HighlightHex", "UI", "Colour drawn behind the run's text; absent draws none.")]
        public string highlightHex { get; set; }

        [A_XSDElementProperty("ColorHex", "UI", "Run's text color; absent takes the block's.")]
        public string colorHex { get; set; }

        // Resolves into ColorHex the way VulkanControl's does; a save writes the hex.
        [A_XSDElementProperty("ControlColor", "UI", "Run's text color by name.")]
        public ControlColor controlColor
        {
            get => field;
            set
            {
                field = value;
                colorHex = Control.EnumColorToHex(value);
            }
        }

        [A_XSDElementProperty("Gradient", "UI", "Name of a gradient ramped across the run in place of its color.")]
        public string gradient { get; set; }

        [A_XSDElementProperty("Effect", "UI", "Name of an effect played across the run's letters.")]
        public string effect { get; set; }

        [A_XSDElementProperty("FontName", "UI", "Font family; absent takes the block's.")]
        public string fontName { get; set; }

        [A_XSDElementProperty("FontSize", "UI", "Type size in pixels; only written when the run chose one.")]
        public int fontSize { get; set; }

        [A_XSDElementProperty("FontSizeAuthored", "UI", "The run's FontSize was chosen, not taken from the styling scheme.")]
        public bool fontSizeAuthored { get; set; }

        [A_XSDElementProperty("StylingType", "UI", "Style this run takes; Inherit follows the block.")]
        public TextStyleType stylingType { get; set; } = TextStyleType.Inherit;

        [A_XSDElementProperty("Image", "UI", "Picture file, relative to the note; the run is the picture and carries no text.")]
        public string image { get; set; }

        [A_XSDElementProperty("Width", "UI", "Picture width in pixels; absent takes the picture's own, fitted to the column.")]
        public float width { get; set; }

        [A_XSDElementProperty("Height", "UI", "Picture height in pixels; absent follows the width at the picture's aspect.")]
        public float height { get; set; }

        [A_XSDElementProperty("Wrap", "UI", "How text flows around the picture; absent keeps it in line with the text.")]
        public PictureWrap wrap { get; set; }

        [A_XSDElementProperty("X", "UI", "A floating picture's offset from the column's left, in pixels.")]
        public float x { get; set; }

        [A_XSDElementProperty("Y", "UI", "A floating picture's offset from its paragraph's top, in pixels.")]
        public float y { get; set; }

        [A_XSDElementProperty("Rotation", "UI", "Picture turn in clockwise degrees; absent is upright.")]
        public float rotation { get; set; }

        [A_XSDElementProperty("Collision", "UI", "What a turned Square picture wraps: its bounding Box or its Shape; absent is Box.")]
        public PictureCollision collision { get; set; }

        public FontStyle Style =>
            bold ? (italic ? FontStyle.BoldItalic : FontStyle.Bold)
                 : italic ? FontStyle.Italic : FontStyle.Regular;
    }

    // One block of a note — a paragraph or a heading — as a single control: the whole block's string
    // with its runs as spans over it. Headings are not a subclass; a block names a styling type and
    // the styles scheme says what that looks like.
    public class BlockControl : TextRunControl
    {
        public TextStyleType stylingType = TextStyleType.Text;
        public TextAlignment alignment;

        // a code block's fence language; null when none was named
        public string? language;

        // list item state
        public ListKind listKind;
        public int listLevel;
        public bool isChecked;

        // the item's own marker; null takes the layout's for its level
        public ListMarker? listMarker;

        // what the document resolved for this item: the marker and its number in its list
        public ListMarker shownMarker { get; internal set; } = ListMarker.Disc;
        public int listNumber { get; internal set; } = 1;

        // shape, number or checkbox, in the indent
        private Control? marker;
        private float listIndent;
        private const float bulletSize = 6f;
        private const float numberGap = 6f;

        // code block and rule geometry
        private const float codeInset = 10f;
        private const float ruleWeight = 1f;
        private DocumentLayout? layout;

        // pre-palette block ink, dropped from runs at load
        private const string legacyInkHex = "#2C2B26";

        // the character a picture span covers
        public const string PictureChar = "￼";

        // Pressing a block focuses the editor above it, never the block itself.
        public override Control? ActiveContextTarget() => (parent as Control)?.ActiveContextTarget();

        // Copies the scheme's sizes onto the block and its spans. A span carrying an authored size
        // keeps it — the scheme only fills in sizes nobody chose.
        public void ApplyLayout(DocumentLayout layout)
        {
            this.layout = layout;
            lineHeight = layout.lineHeight;
            fontSize = layout.FontSizeFor(stylingType);
            fontName = layout.FontNameFor(stylingType) ?? "default";

            listIndent = layout.listIndent;
            ApplyInset();
            SyncMarker();
            SizeMarker();

            for (int i = 0; i < spans.Count; i++)
            {
                StyleSpan span = spans[i];
                if (span.fontSizeAuthored) continue;

                span.fontSize = span.stylingType == TextStyleType.Inherit
                    ? 0 : layout.FontSizeFor(span.stylingType);
                spans[i] = span;
            }

            InvalidateLayout();
        }

        // Scales the type, the list indent and the marker for a document zoom.
        internal void SetZoom(float zoom)
        {
            if (textZoom == zoom) return;

            textZoom = zoom;
            ApplyInset();
            SizeMarker();
            InvalidateLayout();
        }

        private void ApplyInset()
        {
            Thickness inset = padding;
            bool code = stylingType == TextStyleType.Code;
            inset.left = listKind != ListKind.None ? (listLevel + 1) * listIndent * textZoom
                       : code ? codeInset * textZoom : 0f;
            inset.right = code ? codeInset * textZoom : 0f;
            padding = inset;
        }

        private void SizeMarker()
        {
            if (marker is CheckBoxControl box)
            {
                box.SetScale(textZoom);
                return;
            }
            if (marker is LabelControl number)
            {
                number.fontSize = fontSize;
                number.textZoom = textZoom;
                number.InvalidateLayout();
                return;
            }
            if (marker == null) return;

            marker.preferredWidth = bulletSize * textZoom;
            marker.preferredHeight = bulletSize * textZoom;
        }

        // Wraps inside the indent.
        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            Vector2 desired = base.MeasureCore(new Vector2(MathF.Max(0f, availableSize.X - padding.left - padding.right), availableSize.Y));
            marker?.Measure(availableSize);
            return desired;
        }

        // A span's font: its own, else its styling type's, else the block's.
        protected override string FontFor(in StyleSpan span) =>
            span.fontName ?? (span.stylingType == TextStyleType.Inherit ? null : layout?.FontNameFor(span.stylingType)) ?? fontName;

        protected override TextAlignment Alignment => alignment;

        // A code block's ground behind each line, and a rule's line in place of text.
        internal override void Emit(float z)
        {
            LayoutRect a = arrange.arranged;
            LayoutRect box = arrange.clip;
            Vector4 clip = new Vector4(box.x, box.y, box.Right, box.Bottom);
            Vector4 bounds = new Vector4(a.x, a.y, a.Right, a.Bottom);

            if (stylingType == TextStyleType.Rule)
            {
                float weight = MathF.Max(1f, ruleWeight * textZoom);
                float y = Lines is { Count: > 0 } lines ? TextOrigin.Y + lines[0].top + (lines[0].height - weight) * 0.5f : a.y;
                WriteRect(UIEngine.Quads, a.x, y, a.width, weight, Palettes.Surface(palette ?? Palettes.Default, PaletteRole.Line),
                          alpha, z, clip, bounds);
                return;
            }

            if (stylingType == TextStyleType.Code && Lines != null)
            {
                uint ground = Palettes.Surface(palette ?? Palettes.Default, PaletteRole.SubField);
                foreach (TextLine line in Lines)
                    WriteRect(UIEngine.Quads, a.x, TextOrigin.Y + line.top, a.width, line.height, ground, alpha, z, clip, bounds);
            }

            base.Emit(z);
        }

        // Places the marker in the indent, centred on the first line.
        protected override void ArrangeCore(LayoutRect finalRect)
        {
            base.ArrangeCore(finalRect);
            if (marker == null || Lines == null || Lines.Count == 0) return;

            TextLine first = Lines[0];
            Vector2 size = marker.DesiredSize;
            float indent = listIndent * textZoom;
            float x = marker is LabelControl
                ? TextOrigin.X - numberGap * textZoom - size.X
                : TextOrigin.X - indent + (indent - size.X) * 0.5f;
            float y = TextOrigin.Y + first.top + (first.height - size.Y) * 0.5f;
            marker.Arrange(new LayoutRect(x, y, size.X, size.Y));
        }

        // The document's resolved marker and number for this item.
        internal void ShowMarker(ListMarker resolved, int number)
        {
            if (shownMarker == resolved && listNumber == number) return;

            shownMarker = resolved;
            listNumber = number;
            SyncMarker();
        }

        // Builds, swaps or drops the marker to match the list kind and resolved marker.
        private void SyncMarker()
        {
            bool numbered = ListMarkers.IsNumbered(shownMarker);
            bool fits = listKind switch
            {
                ListKind.Task => marker is CheckBoxControl,
                ListKind.Bullet => numbered
                    ? marker is LabelControl
                    : marker is IconControl icon && icon.iconName == ListMarkers.ShapeIcon(shownMarker),
                _ => marker == null
            };

            if (!fits)
            {
                marker?.Destroy();
                marker = listKind switch
                {
                    ListKind.Task => new CheckBoxControl
                    {
                        role = PaletteRole.SubField,
                        onChanged = value => (parent?.parent as DocumentEditorControl)?.SetChecked(this, value)
                    },
                    ListKind.Bullet when numbered => new LabelControl
                    {
                        role = PaletteRole.Ink,
                        hitTestable = false
                    },
                    ListKind.Bullet => new IconControl
                    {
                        iconName = ListMarkers.ShapeIcon(shownMarker),
                        role = PaletteRole.Ink,
                        hitTestable = false
                    },
                    _ => null
                };
                SizeMarker();
                if (marker != null) AddChild(marker);
                InvalidateLayout();
            }

            if (marker is CheckBoxControl box) box.isChecked = isChecked;
            if (marker is LabelControl label)
            {
                string written = ListMarkers.Format(listNumber, shownMarker);
                if (label.text != written)
                {
                    label.text = written;
                    InvalidateLayout();
                }
            }
        }

        // Load: the run's text joins the block's string and its style becomes the next span.
        public void AppendRun(Run run)
        {
            string slice = run.image != null ? PictureChar : run.text ?? string.Empty;

            spans.Add(new StyleSpan
            {
                count = slice.Length,
                style = run.Style,
                colorHex = string.Equals(run.colorHex, legacyInkHex, StringComparison.OrdinalIgnoreCase) ? null : run.colorHex,
                fontName = run.fontName,
                fontSize = run.fontSizeAuthored ? run.fontSize : 0,
                gradient = run.gradient,
                effect = run.effect,
                strikethrough = run.strikethrough,
                underline = run.underline,
                highlightHex = run.highlightHex,
                stylingType = run.stylingType,
                fontSizeAuthored = run.fontSizeAuthored,
                imageSource = run.image,
                imageWidth = run.width,
                imageHeight = run.height,
                wrap = run.image != null ? run.wrap : PictureWrap.Inline,
                imageX = run.image != null ? run.x : 0f,
                imageY = run.image != null ? run.y : 0f,
                imageRotation = run.image != null ? run.rotation : 0f,
                collision = run.image != null ? run.collision : PictureCollision.Box
            });
            if (run.effect != null) RestartEffect();

            text += slice;
        }

        #region ---- text and spans ----
        // Inserts text at a character offset. A boundary belongs to the span after it, which is the
        // run the outgoing stack's caret normalization put a caret in.
        public void InsertText(int offset, string insert)
        {
            if (string.IsNullOrEmpty(insert)) return;

            int index = SpanForInsert(offset);
            if (spans[index].IsPicture) index = TextSpanBeside(index, offset);

            StyleSpan span = spans[index];
            span.count += insert.Length;
            spans[index] = span;

            text = (text ?? string.Empty).Insert(offset, insert);
        }

        public void RemoveText(int offset, int count)
        {
            if (count <= 0) return;

            int end = offset + count;
            int start = 0;

            for (int i = 0; i < spans.Count; i++)
            {
                StyleSpan span = spans[i];
                int spanEnd = start + span.count;

                int cut = Math.Min(spanEnd, end) - Math.Max(start, offset);
                if (cut > 0)
                {
                    span.count -= cut;
                    spans[i] = span;
                }
                start = spanEnd;
            }

            text = (text ?? string.Empty).Remove(offset, count);
            DropEmptySpans();
        }

        // Keeps [0..offset) and returns a detached block of the same styling holding the rest.
        public BlockControl SplitAt(int offset)
        {
            string whole = text ?? string.Empty;
            BlockControl tail = new BlockControl
            {
                stylingType = stylingType,
                alignment = alignment,
                language = language,
                listKind = listKind,
                listLevel = listLevel,
                listMarker = listMarker,
                fontName = fontName,
                fontSize = fontSize,
                lineHeight = lineHeight
            };
            tail.CopyPaint(this);

            StyleSpan carried = StyleAt(offset);
            carried.count = 0;

            int index = SplitSpanAt(offset);
            tail.spans.Clear();
            for (int i = index; i < spans.Count; i++)
                tail.spans.Add(spans[i]);
            spans.RemoveRange(index, spans.Count - index);

            tail.text = whole[offset..];
            text = whole[..offset];

            if (spans.Count == 0) spans.Add(carried);
            if (tail.spans.Count == 0) tail.spans.Add(carried);

            return tail;
        }

        // The inverse of a split: the other block's text and spans land on the end of this one.
        public void AppendBlock(BlockControl tail)
        {
            AppendSpans(tail.spans, tail.text ?? string.Empty);
        }

        // One block's content as data, for an edit record that has to put it back.
        public BlockSnapshot Snapshot() => SliceSnapshot(0, Length);

        // The part of this block a range covers, and nothing else.
        public BlockSnapshot SliceSnapshot(int from, int to)
        {
            BlockSnapshot snapshot = new BlockSnapshot
            {
                stylingType = stylingType,
                alignment = alignment,
                language = language,
                listKind = listKind,
                listLevel = listLevel,
                listMarker = listMarker,
                isChecked = isChecked,
                text = (text ?? string.Empty)[from..to]
            };

            int start = 0;
            foreach (StyleSpan span in spans)
            {
                int spanEnd = start + span.count;
                int covered = Math.Min(spanEnd, to) - Math.Max(start, from);
                if (covered > 0)
                {
                    StyleSpan cut = span;
                    cut.count = covered;
                    snapshot.spans.Add(cut);
                }
                start = spanEnd;
            }

            if (snapshot.spans.Count == 0)
                snapshot.spans.Add(new StyleSpan { count = 0, style = style });

            return snapshot;
        }

        // Puts a captured slice back, styles and all.
        public void InsertSlice(int offset, BlockSnapshot slice)
        {
            int index = SplitSpanAt(offset);
            spans.InsertRange(index, slice.spans);
            text = (text ?? string.Empty).Insert(offset, slice.text);

            DropEmptySpans();
            MergeSpans();
        }

        public void AppendSlice(BlockSnapshot slice) => AppendSpans(slice.spans, slice.text);

        // Replaces everything this block holds.
        public void Restore(BlockSnapshot snapshot)
        {
            stylingType = snapshot.stylingType;
            alignment = snapshot.alignment;
            language = snapshot.language;
            listKind = snapshot.listKind;
            listLevel = snapshot.listLevel;
            listMarker = snapshot.listMarker;
            isChecked = snapshot.isChecked;
            spans.Clear();
            spans.AddRange(snapshot.spans);
            text = snapshot.text;
            InvalidateLayout();
        }

        // Takes another block's kind and leaves the text alone.
        internal void TakeKind(BlockSnapshot kind)
        {
            stylingType = kind.stylingType;
            alignment = kind.alignment;
            language = kind.language;
            listKind = kind.listKind;
            listLevel = kind.listLevel;
            listMarker = kind.listMarker;
            isChecked = kind.isChecked;
            InvalidateLayout();
        }

        public static BlockControl From(BlockSnapshot snapshot)
        {
            BlockControl block = new BlockControl();
            block.Restore(snapshot);
            return block;
        }

        // The style the character before an offset carries, which is what a caret there takes.
        public StyleSpan StyleAt(int offset)
        {
            int start = 0;
            foreach (StyleSpan span in spans)
            {
                int spanEnd = start + span.count;
                if (offset < spanEnd || spanEnd == Length) return span.AsText();
                start = spanEnd;
            }
            return spans[^1].AsText();
        }

        // Restyles a character range: a boundary is cut at each end, every span between takes the
        // delta, and what the change made identical folds back together.
        public void StyleRange(int start, int end, StyleDelta delta)
        {
            if (end <= start) return;

            SplitSpanAt(end);
            int first = SplitSpanAt(start);

            int at = start;
            for (int i = first; i < spans.Count && at < end; i++)
            {
                StyleSpan span = spans[i];
                int spanEnd = i == spans.Count - 1 ? Length : at + span.count;

                if (spanEnd <= end)
                {
                    delta.Apply(ref span);
                    spans[i] = span;
                }
                at = spanEnd;
            }

            MergeSpans();
            InvalidateLayout();
        }

        private void AppendSpans(List<StyleSpan> add, string slice)
        {
            spans.AddRange(add);
            text = (text ?? string.Empty) + slice;

            DropEmptySpans();
            MergeSpans();
        }

        private int SpanForInsert(int offset)
        {
            int start = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                int spanEnd = start + spans[i].count;
                if (offset == start && i > 0) return i;
                if (offset < spanEnd) return i;
                start = spanEnd;
            }
            return spans.Count - 1;
        }

        // Gives the picture at an offset another's size, wrap and offset.
        public void SetPicture(int offset, StyleSpan picture)
        {
            int start = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                if (offset == start && spans[i].IsPicture && spans[i].count > 0)
                {
                    StyleSpan span = spans[i];
                    span.imageWidth = picture.imageWidth;
                    span.imageHeight = picture.imageHeight;
                    span.wrap = picture.wrap;
                    span.imageX = picture.imageX;
                    span.imageY = picture.imageY;
                    span.imageRotation = picture.imageRotation;
                    span.collision = picture.collision;
                    spans[i] = span;
                    InvalidateLayout();
                    return;
                }
                start += spans[i].count;
            }
        }

        // The text span on the picture's side the offset touches, made empty in its style when there is none.
        private int TextSpanBeside(int picture, int offset)
        {
            int start = 0;
            for (int i = 0; i < picture; i++) start += spans[i].count;

            if (offset <= start)
            {
                if (picture > 0 && !spans[picture - 1].IsPicture) return picture - 1;
                spans.Insert(picture, ZeroText(spans[picture]));
                return picture;
            }

            if (picture + 1 < spans.Count && !spans[picture + 1].IsPicture) return picture + 1;
            spans.Insert(picture + 1, ZeroText(spans[picture]));
            return picture + 1;
        }

        private static StyleSpan ZeroText(StyleSpan picture)
        {
            StyleSpan text = picture.AsText();
            text.count = 0;
            return text;
        }

        // Makes a span boundary fall exactly on an offset and returns the index that starts there.
        // An offset already on one splits nothing, which is what keeps a repeated edit from shredding
        // a block into one span per character.
        public int SplitSpanAt(int offset)
        {
            int start = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                StyleSpan span = spans[i];
                int spanEnd = start + span.count;

                if (offset == start) return i;
                if (offset < spanEnd)
                {
                    StyleSpan left = span;
                    left.count = offset - start;
                    StyleSpan right = span;
                    right.count = spanEnd - offset;

                    spans[i] = left;
                    spans.Insert(i + 1, right);
                    return i + 1;
                }
                start = spanEnd;
            }
            return spans.Count;
        }

        // Folds neighbours nothing distinguishes back into one, so a document does not accumulate a
        // span boundary per edit.
        public void MergeSpans()
        {
            for (int i = spans.Count - 1; i > 0; i--)
                if (SameStyle(spans[i - 1], spans[i]))
                {
                    StyleSpan merged = spans[i - 1];
                    merged.count += spans[i].count;
                    spans[i - 1] = merged;
                    spans.RemoveAt(i);
                }
        }

        private void DropEmptySpans()
        {
            for (int i = spans.Count - 1; i >= 0; i--)
                if (spans[i].count == 0 && spans.Count > 1) spans.RemoveAt(i);

            if (spans.Count == 1 && spans[0].count == 0 && spans[0].IsPicture) spans[0] = spans[0].AsText();
        }

        private static bool SameStyle(StyleSpan a, StyleSpan b) =>
            !a.IsPicture && !b.IsPicture
            && a.style == b.style
            && a.colorHex == b.colorHex
            && a.gradient == b.gradient
            && a.effect == b.effect
            && a.fontName == b.fontName
            && a.fontSize == b.fontSize
            && a.strikethrough == b.strikethrough
            && a.underline == b.underline
            && a.highlightHex == b.highlightHex
            && a.stylingType == b.stylingType
            && a.fontSizeAuthored == b.fontSizeAuthored;
        #endregion

        // Save: the spans cut back into runs. The last span absorbs whatever is left of the string,
        // so its count is read off the text rather than trusted.
        public List<Run> Runs()
        {
            string whole = text ?? string.Empty;
            List<Run> runs = new List<Run>(spans.Count);
            int start = 0;

            for (int i = 0; i < spans.Count; i++)
            {
                int count = i == spans.Count - 1
                    ? whole.Length - start
                    : Math.Clamp(spans[i].count, 0, whole.Length - start);
                if (count < 0) count = 0;

                StyleSpan span = spans[i];
                runs.Add(new Run
                {
                    text = span.IsPicture ? string.Empty : whole.Substring(start, count),
                    image = span.imageSource,
                    width = span.imageWidth,
                    height = span.imageHeight,
                    wrap = span.wrap,
                    x = span.imageX,
                    y = span.imageY,
                    rotation = span.imageRotation,
                    collision = span.collision,
                    bold = span.style == FontStyle.Bold || span.style == FontStyle.BoldItalic,
                    italic = span.style == FontStyle.Italic || span.style == FontStyle.BoldItalic,
                    strikethrough = span.strikethrough,
                    underline = span.underline,
                    highlightHex = span.highlightHex,
                    colorHex = span.colorHex,
                    gradient = span.gradient,
                    effect = span.effect,
                    fontName = span.fontName,
                    fontSize = span.fontSizeAuthored ? span.fontSize : 0,
                    fontSizeAuthored = span.fontSizeAuthored,
                    stylingType = span.stylingType
                });
                start += count;
            }

            return runs;
        }
    }
}
