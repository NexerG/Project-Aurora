namespace ArctisAurora.Core.Editing
{
    // One user action's worth of records. Undo runs them in reverse, so the caret restore that
    // stands is the one belonging to the first record made.
    public sealed class EditStep
    {
        public readonly List<IEditRecord> records = new List<IEditRecord>();
        public readonly string label;

        public EditStep(string label) => this.label = label;

        public void Undo()
        {
            for (int i = records.Count - 1; i >= 0; i--)
                records[i].Undo();
        }

        public void Redo()
        {
            for (int i = 0; i < records.Count; i++)
                records[i].Redo();
        }
    }
}
