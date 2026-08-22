namespace ArctisAurora.Core.Editing
{
    // One reversible change. A record carries its own target and the data to reverse itself, so the
    // stack never learns what kind of thing it is holding history for.
    public interface IEditRecord
    {
        void Undo();

        void Redo();
    }
}
