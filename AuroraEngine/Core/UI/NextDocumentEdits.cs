using ArctisAurora.Core.Editing;
using ArctisAurora.Core.UISystem.Controls.Text.Document;

namespace ArctisAurora.Core.UI
{
    // Where an edit happened, by index rather than by reference. Undo rebuilds blocks, so a control
    // captured before the edit is already destroyed by the time it is reversed.
    public readonly struct NextDocumentAddress
    {
        public readonly int block;
        public readonly int offset;

        public NextDocumentAddress(int block, int offset)
        {
            this.block = block;
            this.offset = offset;
        }

        public bool Equals(NextDocumentAddress other) => block == other.block && offset == other.offset;
    }

    // A block, or a slice of one, as plain data.
    public sealed class NextBlockSnapshot
    {
        public TextStyleType stylingType;
        public string text = string.Empty;
        public readonly List<StyleSpan> spans = new List<StyleSpan>();
    }

    // The content a range delete removed, and nothing else — the surviving context around it is
    // never copied. More than one block means the range crossed a block boundary, and then the first
    // snapshot is the head block's cut suffix and the last the tail block's cut prefix.
    public sealed class NextDocumentFragment
    {
        public readonly List<NextBlockSnapshot> blocks = new List<NextBlockSnapshot>();
    }

    // Text written into or cut out of a single block, changing no structure — typing.
    public sealed class NextTextEdit : IEditRecord
    {
        private readonly NextDocumentControl document;
        private readonly NextDocumentAddress at;
        private readonly string text;
        private readonly bool inserted;

        public NextTextEdit(NextDocumentControl document, NextDocumentAddress at, string text, bool inserted)
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
    public sealed class NextSplitEdit : IEditRecord
    {
        private readonly NextDocumentControl document;
        private readonly NextDocumentAddress at;

        public NextSplitEdit(NextDocumentControl document, NextDocumentAddress at)
        {
            this.document = document;
            this.at = at;
        }

        public void Undo() => document.JoinBlockWithNext(at);

        public void Redo() => document.SplitBlockAt(at);
    }

    // A style change over a range, span styling or block styling alike. The text is untouched, so
    // the inverse is the spans that were on it — no fragment, no structural surgery.
    public sealed class NextStyleRangeEdit : IEditRecord
    {
        private readonly NextDocumentControl document;
        private readonly int firstBlock;
        private readonly List<NextBlockSnapshot> before;

        // span styling
        private readonly NextDocumentAddress from;
        private readonly NextDocumentAddress to;
        private readonly NextStyleDelta delta;

        // block styling; absent means this record is a span restyle
        private readonly TextStyleType? blockStyling;

        public NextStyleRangeEdit(NextDocumentControl document, int firstBlock,
            List<NextBlockSnapshot> before, NextDocumentAddress from, NextDocumentAddress to,
            NextStyleDelta delta)
        {
            this.document = document;
            this.firstBlock = firstBlock;
            this.before = before;
            this.from = from;
            this.to = to;
            this.delta = delta;
        }

        public NextStyleRangeEdit(NextDocumentControl document, int firstBlock,
            List<NextBlockSnapshot> before, TextStyleType blockStyling)
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
    public sealed class NextDeleteRangeEdit : IEditRecord
    {
        private readonly NextDocumentControl document;
        private readonly NextDocumentAddress from;
        private readonly NextDocumentAddress to;
        private readonly NextDocumentFragment fragment;

        public NextDeleteRangeEdit(NextDocumentControl document, NextDocumentAddress from,
            NextDocumentAddress to, NextDocumentFragment fragment)
        {
            this.document = document;
            this.from = from;
            this.to = to;
            this.fragment = fragment;
        }

        public void Undo() => document.InsertFragment(from, fragment);

        public void Redo() => document.DeleteBetween(from, to);
    }
}
