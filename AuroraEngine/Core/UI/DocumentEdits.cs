using ArctisAurora.Core.Editing;

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
        public ListKind listKind;
        public int listLevel;
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
        private readonly DocumentControl document;
        private readonly DocumentAddress at;
        private readonly string text;
        private readonly bool inserted;

        public TextEdit(DocumentControl document, DocumentAddress at, string text, bool inserted)
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
        private readonly DocumentControl document;
        private readonly DocumentAddress at;

        public SplitEdit(DocumentControl document, DocumentAddress at)
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
        private readonly DocumentControl document;
        private readonly int firstBlock;
        private readonly List<BlockSnapshot> before;

        // span styling
        private readonly DocumentAddress from;
        private readonly DocumentAddress to;
        private readonly StyleDelta delta;

        // block styling; absent means this record is a span restyle
        private readonly TextStyleType? blockStyling;

        public StyleRangeEdit(DocumentControl document, int firstBlock,
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

        public StyleRangeEdit(DocumentControl document, int firstBlock,
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
        private readonly DocumentControl document;
        private readonly DocumentAddress from;
        private readonly DocumentAddress to;
        private readonly DocumentFragment fragment;

        public DeleteRangeEdit(DocumentControl document, DocumentAddress from,
            DocumentAddress to, DocumentFragment fragment)
        {
            this.document = document;
            this.from = from;
            this.to = to;
            this.fragment = fragment;
        }

        public void Undo() => document.InsertFragment(from, fragment);

        public void Redo() => document.DeleteBetween(from, to);
    }

    // Blocks rewritten in place — list kind, nesting, a tick. Both directions are snapshots.
    public sealed class BlockStateEdit : IEditRecord
    {
        private readonly DocumentControl document;
        private readonly int firstBlock;
        private readonly List<BlockSnapshot> before;
        private readonly List<BlockSnapshot> after;

        public BlockStateEdit(DocumentControl document, int firstBlock,
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
}
