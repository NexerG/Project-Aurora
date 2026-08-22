namespace ArctisAurora.Core.UISystem.Controls.Text.Document.Edits
{
    // The content a range delete removed, and nothing else — the surviving context around it is
    // never copied. Also the shape a cut or a paste wants.
    //
    // The first snapshot run is always the text cut out of the head run and the last is always the
    // text cut out of the tail run; everything between them is a run that was destroyed whole. More
    // than one block means the range crossed a block boundary.
    public sealed class DocumentFragment
    {
        public readonly List<BlockSnapshot> blocks = new List<BlockSnapshot>();

        // Whether the tail run kept anything. Recorded from what the delete actually did rather
        // than re-derived, so the rule can move without stranding old records.
        public bool tailRunDestroyed;
    }
}
