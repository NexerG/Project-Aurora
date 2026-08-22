namespace ArctisAurora.Core.UISystem.Controls.Text.Document.Edits
{
    // Where an edit happened, by index rather than by reference. Undo rebuilds runs and blocks, so
    // a control captured before the edit is already destroyed by the time it is reversed.
    public readonly struct DocumentAddress
    {
        public readonly int block;
        public readonly int run;
        public readonly int offset;

        public DocumentAddress(int block, int run, int offset)
        {
            this.block = block;
            this.run = run;
            this.offset = offset;
        }
    }
}
