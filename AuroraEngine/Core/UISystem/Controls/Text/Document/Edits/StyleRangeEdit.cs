using ArctisAurora.Core.Editing;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document.Edits
{
    // A style change over a range, run styling or block styling alike. The text is untouched, so the
    // inverse is the run partition and the styles that were on it — no fragment, no structural
    // surgery. Redo replays the forward primitive, which resolves again because undo put the
    // partition back exactly as it was.
    public sealed class StyleRangeEdit : IEditRecord
    {
        private readonly DocumentControl document;
        private readonly int firstBlock;
        private readonly List<BlockSnapshot> before;

        // run styling
        private readonly DocumentAddress from;
        private readonly DocumentAddress to;
        private readonly StyleDelta delta;

        // block styling; absent means this record is a run restyle
        private readonly TextStyleType? blockStyling;

        public StyleRangeEdit(DocumentControl document, int firstBlock, List<BlockSnapshot> before,
            DocumentAddress from, DocumentAddress to, StyleDelta delta)
        {
            this.document = document;
            this.firstBlock = firstBlock;
            this.before = before;
            this.from = from;
            this.to = to;
            this.delta = delta;
        }

        public StyleRangeEdit(DocumentControl document, int firstBlock, List<BlockSnapshot> before,
            TextStyleType blockStyling)
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
}
