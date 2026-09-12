using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls.Text.Document;

namespace ArctisAurora.Core.UI
{
    // A run as a note file writes it: one styled slice of a block's text. It exists at load and save
    // only — in the tree a run is a StyleSpan on the block that holds it.
    [A_XSDType("NextRun", "UI")]
    public class NextRun
    {
        [A_XSDElementProperty("Text", "UI", "The slice of the block's text this run covers.")]
        public string text { get; set; } = string.Empty;

        [A_XSDElementProperty("Bold", "UI", "Run is bold.")]
        public bool bold { get; set; }

        [A_XSDElementProperty("Italic", "UI", "Run is italic.")]
        public bool italic { get; set; }

        [A_XSDElementProperty("Strikethrough", "UI", "Run is struck through.")]
        public bool strikethrough { get; set; }

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

        [A_XSDElementProperty("FontName", "UI", "Font family; absent takes the block's.")]
        public string fontName { get; set; }

        [A_XSDElementProperty("FontSize", "UI", "Type size in pixels; only written when the run chose one.")]
        public int fontSize { get; set; }

        [A_XSDElementProperty("FontSizeAuthored", "UI", "The run's FontSize was chosen, not taken from the styling scheme.")]
        public bool fontSizeAuthored { get; set; }

        [A_XSDElementProperty("StylingType", "UI", "Style this run takes; Inherit follows the block.")]
        public TextStyleType stylingType { get; set; } = TextStyleType.Inherit;

        public FontStyle Style =>
            bold ? (italic ? FontStyle.BoldItalic : FontStyle.Bold)
                 : italic ? FontStyle.Italic : FontStyle.Regular;
    }

    // One block of a note — a paragraph or a heading — as a single control: the whole block's string
    // with its runs as spans over it. Headings are not a subclass; a block names a styling type and
    // the styles scheme says what that looks like.
    public class NextBlockControl : TextRunControl
    {
        public TextStyleType stylingType = TextStyleType.Text;

        public NextBlockControl()
        {
            colorHex = "#2C2B26";
        }

        // Pressing a block focuses the editor above it, never the block itself.
        public override Control? ActiveContextTarget() => (parent as Control)?.ActiveContextTarget();

        // Copies the scheme's sizes onto the block and its spans. A span carrying an authored size
        // keeps it — the scheme only fills in sizes nobody chose.
        public void ApplyLayout(DocumentLayout layout)
        {
            lineHeight = layout.lineHeight;
            fontSize = layout.FontSizeFor(stylingType);

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

        // Load: the run's text joins the block's string and its style becomes the next span.
        public void AppendRun(NextRun run)
        {
            string slice = run.text ?? string.Empty;

            spans.Add(new StyleSpan
            {
                count = slice.Length,
                style = run.Style,
                colorHex = run.colorHex,
                fontName = run.fontName,
                fontSize = run.fontSizeAuthored ? run.fontSize : 0,
                gradient = run.gradient,
                strikethrough = run.strikethrough,
                stylingType = run.stylingType,
                fontSizeAuthored = run.fontSizeAuthored
            });

            text += slice;
        }

        #region ---- text and spans ----
        // Inserts text at a character offset. A boundary belongs to the span after it, which is the
        // run the outgoing stack's caret normalization put a caret in.
        public void InsertText(int offset, string insert)
        {
            if (string.IsNullOrEmpty(insert)) return;

            int index = SpanForInsert(offset);
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
        public NextBlockControl SplitAt(int offset)
        {
            string whole = text ?? string.Empty;
            NextBlockControl tail = new NextBlockControl
            {
                stylingType = stylingType,
                colorHex = colorHex,
                fontName = fontName,
                fontSize = fontSize,
                lineHeight = lineHeight
            };

            int index = SplitSpanAt(offset);
            tail.spans.Clear();
            for (int i = index; i < spans.Count; i++)
                tail.spans.Add(spans[i]);
            spans.RemoveRange(index, spans.Count - index);

            tail.text = whole[offset..];
            text = whole[..offset];

            if (spans.Count == 0) spans.Add(new StyleSpan { count = 0, style = style });
            if (tail.spans.Count == 0) tail.spans.Add(new StyleSpan { count = 0, style = style });

            return tail;
        }

        // The inverse of a split: the other block's text and spans land on the end of this one.
        public void AppendBlock(NextBlockControl tail)
        {
            AppendSpans(tail.spans, tail.text ?? string.Empty);
        }

        // One block's content as data, for an edit record that has to put it back.
        public NextBlockSnapshot Snapshot() => SliceSnapshot(0, Length);

        // The part of this block a range covers, and nothing else.
        public NextBlockSnapshot SliceSnapshot(int from, int to)
        {
            NextBlockSnapshot snapshot = new NextBlockSnapshot
            {
                stylingType = stylingType,
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
        public void InsertSlice(int offset, NextBlockSnapshot slice)
        {
            int index = SplitSpanAt(offset);
            spans.InsertRange(index, slice.spans);
            text = (text ?? string.Empty).Insert(offset, slice.text);

            DropEmptySpans();
            MergeSpans();
        }

        public void AppendSlice(NextBlockSnapshot slice) => AppendSpans(slice.spans, slice.text);

        // Replaces everything this block holds.
        public void Restore(NextBlockSnapshot snapshot)
        {
            stylingType = snapshot.stylingType;
            spans.Clear();
            spans.AddRange(snapshot.spans);
            text = snapshot.text;
            InvalidateLayout();
        }

        public static NextBlockControl From(NextBlockSnapshot snapshot)
        {
            NextBlockControl block = new NextBlockControl();
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
                if (offset < spanEnd || spanEnd == Length) return span;
                start = spanEnd;
            }
            return spans[^1];
        }

        // Restyles a character range: a boundary is cut at each end, every span between takes the
        // delta, and what the change made identical folds back together.
        public void StyleRange(int start, int end, NextStyleDelta delta)
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
        }

        private static bool SameStyle(StyleSpan a, StyleSpan b) =>
            a.style == b.style
            && a.colorHex == b.colorHex
            && a.gradient == b.gradient
            && a.fontName == b.fontName
            && a.fontSize == b.fontSize
            && a.strikethrough == b.strikethrough
            && a.stylingType == b.stylingType
            && a.fontSizeAuthored == b.fontSizeAuthored;
        #endregion

        // Save: the spans cut back into runs. The last span absorbs whatever is left of the string,
        // so its count is read off the text rather than trusted.
        public List<NextRun> Runs()
        {
            string whole = text ?? string.Empty;
            List<NextRun> runs = new List<NextRun>(spans.Count);
            int start = 0;

            for (int i = 0; i < spans.Count; i++)
            {
                int count = i == spans.Count - 1
                    ? whole.Length - start
                    : Math.Clamp(spans[i].count, 0, whole.Length - start);
                if (count < 0) count = 0;

                StyleSpan span = spans[i];
                runs.Add(new NextRun
                {
                    text = whole.Substring(start, count),
                    bold = span.style == FontStyle.Bold || span.style == FontStyle.BoldItalic,
                    italic = span.style == FontStyle.Italic || span.style == FontStyle.BoldItalic,
                    strikethrough = span.strikethrough,
                    colorHex = span.colorHex,
                    gradient = span.gradient,
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
