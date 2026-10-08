using ArctisAurora.Core.Filing;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // One entry of a note: a block or a table.
    public abstract class NoteNode
    {
        // the gap above, px; null takes the layout's block spacing
        public float? spaceBefore;

        // a break before the entry, and the footnote or float it is part of; null in the flow
        public PageBreak pageBreak;
        public PageInsert? insert;
    }

    // One block of a note as data: its string, spans and block properties.
    public sealed class NoteBlock : NoteNode
    {
        public readonly TextRunData run = new TextRunData();

        public TextStyleType stylingType = TextStyleType.Text;
        public TextAlignment alignment;

        // first-line indent, px
        public float firstIndent;

        // the page style its page takes, and the running-head marks it sets; null leaves a mark as it was
        public string? pageStyle;
        public string? markLeft;
        public string? markRight;

        // a code block's fence language; null when none was named
        public string? language;

        // a code block that wraps its lines; .xml only, so Markdown code never wraps
        public bool codeWrap;

        // list item state
        public ListKind listKind;
        public int listLevel;
        public bool isChecked;

        // the item's own marker; null takes the layout's for its level
        public ListMarker? listMarker;

        // the number this item restarts its list at; null counts on from the item above
        public int? listStart;

        // pre-palette block ink, dropped from runs at load
        private const string legacyInkHex = "#2C2B26";

        // Load: the run's text joins the block's string and its style becomes the next span.
        public void AppendRun(Run appended)
        {
            string slice = appended.image != null || appended.math != null || appended.sheet != null ? BlockControl.PictureChar : appended.text ?? string.Empty;

            run.spans.Add(new StyleSpan
            {
                count = slice.Length,
                style = appended.Style,
                colorHex = string.Equals(appended.colorHex, legacyInkHex, StringComparison.OrdinalIgnoreCase) ? null : appended.colorHex,
                fontName = appended.fontName,
                fontSize = appended.fontSizeAuthored ? appended.fontSize : 0,
                gradient = appended.gradient,
                effect = appended.effect,
                strikethrough = appended.strikethrough,
                underline = appended.underline,
                highlightHex = appended.highlightHex,
                stylingType = appended.stylingType,
                fontSizeAuthored = appended.fontSizeAuthored,
                imageSource = appended.image,
                imageWidth = appended.width,
                imageHeight = appended.height,
                wrap = appended.image != null ? appended.wrap : PictureWrap.Inline,
                imageX = appended.image != null ? appended.x : 0f,
                imageY = appended.image != null ? appended.y : 0f,
                imageRotation = appended.image != null ? appended.rotation : 0f,
                collision = appended.image != null ? appended.collision : PictureCollision.Box,
                mathSource = appended.image == null ? appended.math : null,
                mathDisplay = appended.image == null && appended.math != null && appended.display,
                sheetRef = appended.image == null && appended.math == null ? appended.sheet : null,
                spaceWidth = appended.image == null && appended.math == null && appended.sheet == null ? appended.space : 0f,
                note = appended.note
            });

            run.text += slice;
        }

        // Keeps [0..offset) and returns a block of the same styling holding the rest.
        public NoteBlock SplitAt(int offset)
        {
            string whole = run.text ?? string.Empty;
            NoteBlock tail = new NoteBlock
            {
                stylingType = stylingType,
                alignment = alignment,
                firstIndent = firstIndent,
                spaceBefore = spaceBefore,
                insert = insert,
                language = language,
                codeWrap = codeWrap,
                listKind = listKind,
                listLevel = listLevel,
                listMarker = listMarker
            };

            StyleSpan carried = run.StyleAt(offset);
            carried.count = 0;

            int index = run.SplitSpanAt(offset);
            for (int i = index; i < run.spans.Count; i++)
                tail.run.spans.Add(run.spans[i]);
            run.spans.RemoveRange(index, run.spans.Count - index);

            tail.run.text = whole[offset..];
            run.text = whole[..offset];

            if (run.spans.Count == 0) run.spans.Add(carried);
            if (tail.run.spans.Count == 0) tail.run.spans.Add(carried);

            return tail;
        }

        // One block's content as data, for an edit record that has to put it back.
        public BlockSnapshot Snapshot() => SliceSnapshot(0, run.Length);

        // The part of this block a range covers, and nothing else.
        public BlockSnapshot SliceSnapshot(int from, int to)
        {
            BlockSnapshot snapshot = new BlockSnapshot
            {
                stylingType = stylingType,
                alignment = alignment,
                firstIndent = firstIndent,
                spaceBefore = spaceBefore,
                pageBreak = pageBreak,
                pageStyle = pageStyle,
                markLeft = markLeft,
                markRight = markRight,
                insert = insert,
                language = language,
                codeWrap = codeWrap,
                listKind = listKind,
                listLevel = listLevel,
                listMarker = listMarker,
                listStart = listStart,
                isChecked = isChecked,
                text = (run.text ?? string.Empty)[from..to]
            };

            int start = 0;
            foreach (StyleSpan span in run.spans)
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
                snapshot.spans.Add(new StyleSpan { count = 0, style = FontStyle.Regular });

            return snapshot;
        }

        // Replaces everything this block holds.
        public void Restore(BlockSnapshot snapshot)
        {
            TakeKind(snapshot);
            run.spans.Clear();
            run.spans.AddRange(snapshot.spans);
            run.text = snapshot.text;
        }

        // Takes another block's kind and leaves the text alone.
        public void TakeKind(BlockSnapshot kind)
        {
            stylingType = kind.stylingType;
            alignment = kind.alignment;
            firstIndent = kind.firstIndent;
            spaceBefore = kind.spaceBefore;
            pageBreak = kind.pageBreak;
            pageStyle = kind.pageStyle;
            markLeft = kind.markLeft;
            markRight = kind.markRight;
            insert = kind.insert;
            language = kind.language;
            codeWrap = kind.codeWrap;
            listKind = kind.listKind;
            listLevel = kind.listLevel;
            listMarker = kind.listMarker;
            listStart = kind.listStart;
            isChecked = kind.isChecked;
        }

        public static NoteBlock From(BlockSnapshot snapshot)
        {
            NoteBlock block = new NoteBlock();
            block.Restore(snapshot);
            return block;
        }
    }

    // One cell of a table: the columns it spans, its rules and its blocks.
    public sealed class NoteCell
    {
        public int span = 1;
        public CellRules? rules;
        public NoteBlock[] blocks = Array.Empty<NoteBlock>();
    }

    // A table of a note as data: fixed-width columns, rows of cells.
    public sealed class NoteTable : NoteNode
    {
        // column widths in design pixels, before zoom, and the rules down each column's edges
        public readonly float[] widths;
        public readonly TableRule[] leftRules;
        public readonly TableRule[] rightRules;

        public NoteCell[][] rows = Array.Empty<NoteCell[]>();

        public bool showBorders = true;

        // where a table narrower than the note sits; Justify is Left
        public TextAlignment alignment;

        // the cell inset across and down, px; null = the view's own
        public Vector2? cellPadding;

        public NoteTable(float[] widths)
        {
            this.widths = widths;
            leftRules = new TableRule[widths.Length];
            rightRules = new TableRule[widths.Length];
        }
    }

    // What an edit to a note changed.
    public enum NoteChangeKind
    {
        Text,
        Spans,
        Kind,
        Inserted,
        Removed,
        Table,
        Page,
        Palette,
        Layout,
        Properties,
        ReadOnly
    }

    // One change the model made, raised after it is made.
    public readonly struct NoteChange
    {
        public readonly NoteChangeKind kind;

        // blocks by flat index; a note-level index for a table
        public readonly int first;
        public readonly int count;

        // where text went in or came out, and how much; negative is removed
        public readonly DocumentAddress at;
        public readonly int length;

        // where the edit leaves the caret, and the selection's other end; null leaves them
        public readonly DocumentAddress? anchor;
        public readonly DocumentAddress? caret;

        public NoteChange(NoteChangeKind kind, int first, int count, DocumentAddress at, int length,
            DocumentAddress? anchor, DocumentAddress? caret)
        {
            this.kind = kind;
            this.first = first;
            this.count = count;
            this.at = at;
            this.length = length;
            this.anchor = anchor;
            this.caret = caret;
        }

        // Where an address from before this change sits after it.
        public DocumentAddress Map(DocumentAddress a)
        {
            switch (kind)
            {
                case NoteChangeKind.Text:
                    if (a.block != at.block || a.offset <= at.offset) return a;
                    return new DocumentAddress(a.block, Math.Max(at.offset, a.offset + length));
                case NoteChangeKind.Inserted:
                    if (a.block >= first) return new DocumentAddress(a.block + count, a.offset);
                    if (a.block == at.block && a.offset > at.offset)
                        return new DocumentAddress(first + count - 1, a.offset - at.offset + length);
                    return a;
                case NoteChangeKind.Removed:
                    if (a.block >= first + count) return new DocumentAddress(a.block - count, a.offset);
                    if (a.block == first + count - 1) return new DocumentAddress(at.block, at.offset + Math.Max(0, a.offset - length));
                    if (a.block >= first || (a.block == at.block && a.offset > at.offset)) return at;
                    return a;
                default:
                    return a;
            }
        }
    }
}
