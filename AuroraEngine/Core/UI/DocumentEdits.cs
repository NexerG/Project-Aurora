using ArctisAurora.Core.Editing;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // Where an edit happened, by index rather than by reference. Undo rebuilds blocks, so a control
    // captured before the edit is already destroyed by the time it is reversed.
    public readonly struct DocumentAddress
    {
        public readonly int block;
        public readonly int offset;

        public DocumentAddress(int block, int offset)
        {
            this.block = block;
            this.offset = offset;
        }

        public bool Equals(DocumentAddress other) => block == other.block && offset == other.offset;
    }

    // A block, or a slice of one, as plain data.
    public sealed class BlockSnapshot
    {
        public TextStyleType stylingType;
        public TextAlignment alignment;
        public float firstIndent;
        public float? spaceBefore;
        public PageBreak pageBreak;
        public string? pageStyle;
        public string? markLeft;
        public string? markRight;
        public PageInsert? insert;
        public string? language;
        public bool codeWrap;
        public ListKind listKind;
        public int listLevel;
        public ListMarker? listMarker;
        public int? listStart;
        public bool isChecked;
        public string text = string.Empty;
        public readonly List<StyleSpan> spans = new List<StyleSpan>();
    }

    // The content a range delete removed, and nothing else — the surviving context around it is
    // never copied. More than one block means the range crossed a block boundary, and then the first
    // snapshot is the head block's cut suffix and the last the tail block's cut prefix.
    public sealed class DocumentFragment
    {
        public readonly List<BlockSnapshot> blocks = new List<BlockSnapshot>();
    }

    // Text written into or cut out of a single block, changing no structure — typing.
    public sealed class TextEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly DocumentAddress at;
        private readonly string text;
        private readonly bool inserted;

        public TextEdit(RichTextDocument document, DocumentAddress at, string text, bool inserted)
        {
            this.document = document;
            this.at = at;
            this.text = text;
            this.inserted = inserted;
        }

        public void Undo()
        {
            if (inserted) document.RemoveText(at, text.Length);
            else document.InsertText(at, text);
        }

        public void Redo()
        {
            if (inserted) document.InsertText(at, text);
            else document.RemoveText(at, text.Length);
        }
    }

    // A block split in two. The spans fold back together on the join, so a redo splits the same
    // partition the undo restored.
    public sealed class SplitEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly DocumentAddress at;

        public SplitEdit(RichTextDocument document, DocumentAddress at)
        {
            this.document = document;
            this.at = at;
        }

        public void Undo() => document.JoinBlockWithNext(at);

        public void Redo() => document.SplitBlockAt(at);
    }

    // A style change over a range, span styling or block styling alike. The text is untouched, so
    // the inverse is the spans that were on it — no fragment, no structural surgery.
    public sealed class StyleRangeEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly int firstBlock;
        private readonly List<BlockSnapshot> before;

        // span styling
        private readonly DocumentAddress from;
        private readonly DocumentAddress to;
        private readonly StyleDelta delta;

        // block styling; absent means this record is a span restyle
        private readonly TextStyleType? blockStyling;

        public StyleRangeEdit(RichTextDocument document, int firstBlock,
            List<BlockSnapshot> before, DocumentAddress from, DocumentAddress to,
            StyleDelta delta)
        {
            this.document = document;
            this.firstBlock = firstBlock;
            this.before = before;
            this.from = from;
            this.to = to;
            this.delta = delta;
        }

        public StyleRangeEdit(RichTextDocument document, int firstBlock,
            List<BlockSnapshot> before, TextStyleType blockStyling)
        {
            this.document = document;
            this.firstBlock = firstBlock;
            this.before = before;
            this.blockStyling = blockStyling;
        }

        public void Undo() => document.RestoreBlocks(firstBlock, before);

        public void Redo()
        {
            if (blockStyling.HasValue)
                document.SetBlockStylingBetween(firstBlock, firstBlock + before.Count - 1, blockStyling.Value);
            else
                document.ApplyStyleBetween(from, to, delta);
        }
    }

    // A delete of a range, inside one block or across several. Redo replays the forward primitive
    // rather than inverting the inverse, so only one direction is hand-written.
    public sealed class DeleteRangeEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly DocumentAddress from;
        private readonly DocumentAddress to;
        private readonly DocumentFragment fragment;

        // the selection before the delete, put back by undo
        private readonly DocumentAddress anchor;
        private readonly DocumentAddress caret;

        public DeleteRangeEdit(RichTextDocument document, DocumentAddress from,
            DocumentAddress to, DocumentFragment fragment, DocumentAddress anchor, DocumentAddress caret)
        {
            this.document = document;
            this.from = from;
            this.to = to;
            this.fragment = fragment;
            this.anchor = anchor;
            this.caret = caret;
        }

        public void Undo()
        {
            document.InsertFragment(from, fragment, anchor, caret);
            if (fragment.blocks.Count > 1) document.RestoreKind(from, fragment.blocks[0], anchor, caret);
        }

        public void Redo() => document.DeleteBetween(from, to);
    }

    // Content put into the document at one place — a paste or a drop. The mirror of a range delete.
    public sealed class InsertRangeEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly DocumentAddress from;
        private readonly DocumentAddress to;
        private readonly DocumentFragment fragment;

        public InsertRangeEdit(RichTextDocument document, DocumentAddress from,
            DocumentAddress to, DocumentFragment fragment)
        {
            this.document = document;
            this.from = from;
            this.to = to;
            this.fragment = fragment;
        }

        public void Undo() => document.DeleteBetween(from, to);

        public void Redo() => document.InsertBetween(from, to, fragment);
    }

    // A picture resized, rewrapped or moved; both directions leave it selected.
    public sealed class PictureEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly DocumentAddress at;
        private readonly StyleSpan before;
        private readonly StyleSpan after;

        public PictureEdit(RichTextDocument document, DocumentAddress at, StyleSpan before, StyleSpan after)
        {
            this.document = document;
            this.at = at;
            this.before = before;
            this.after = after;
        }

        public void Undo() => document.SetPicture(at, before);

        public void Redo() => document.SetPicture(at, after);
    }

    // A formula's source rewritten; both directions leave it selected.
    public sealed class MathEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly DocumentAddress at;
        private readonly string before;
        private readonly string after;

        public MathEdit(RichTextDocument document, DocumentAddress at, string before, string after)
        {
            this.document = document;
            this.at = at;
            this.before = before;
            this.after = after;
        }

        public void Undo() => document.SetMath(at, before);

        public void Redo() => document.SetMath(at, after);
    }

    // Blocks rewritten in place — list kind, nesting, a tick. Both directions are snapshots.
    public sealed class BlockStateEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly int firstBlock;
        private readonly List<BlockSnapshot> before;
        private readonly List<BlockSnapshot> after;

        public BlockStateEdit(RichTextDocument document, int firstBlock,
            List<BlockSnapshot> before, List<BlockSnapshot> after)
        {
            this.document = document;
            this.firstBlock = firstBlock;
            this.before = before;
            this.after = after;
        }

        public void Undo() => document.RestoreBlocks(firstBlock, before);

        public void Redo() => document.RestoreBlocks(firstBlock, after);
    }

    // A table inserted, rebuilt or deleted whole, as its XML before and after; null is no table there.
    public sealed class TableEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly int index;
        private readonly XElement? before;
        private readonly XElement? after;
        private readonly DocumentAddress caretBefore;
        private readonly DocumentAddress caretAfter;

        public TableEdit(RichTextDocument document, int index, XElement? before, XElement? after,
            DocumentAddress caretBefore, DocumentAddress caretAfter)
        {
            this.document = document;
            this.index = index;
            this.before = before;
            this.after = after;
            this.caretBefore = caretBefore;
            this.caretAfter = caretAfter;
        }

        public void Undo() => document.PutTable(index, after != null, before, caretBefore);

        public void Redo() => document.PutTable(index, before != null, after, caretAfter);
    }

    // A note's page format before and after a change; null is the editor's own.
    public sealed class PageEdit : IEditRecord
    {
        private readonly RichTextDocument document;
        private readonly PageLayout? before;
        private readonly PageLayout? after;

        public PageEdit(RichTextDocument document, PageLayout? before, PageLayout? after)
        {
            this.document = document;
            this.before = before;
            this.after = after;
        }

        public void Undo() => document.SetPage(before);

        public void Redo() => document.SetPage(after);
    }
}
