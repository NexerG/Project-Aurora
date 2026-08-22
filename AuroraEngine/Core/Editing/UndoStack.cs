namespace ArctisAurora.Core.Editing
{
    // Undo history for one editable thing. An open EditScope collects records into a step, so one
    // user action is one undo however many primitives it ran.
    public sealed class UndoStack
    {
        private const int maxSteps = 500;

        private readonly List<EditStep> _undo = new List<EditStep>();
        private readonly List<EditStep> _redo = new List<EditStep>();

        // open step
        private EditStep _open;
        private int _depth;

        // set while a step is being reversed or replayed
        private bool _applying;

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        public EditScope Begin(string label)
        {
            if (_depth++ == 0) _open = new EditStep(label);
            return new EditScope(this);
        }

        // Redo replays the forward operation, which records again; outside a scope there is no step
        // to join.
        public void Push(IEditRecord record)
        {
            if (_applying || _open == null) return;
            _open.records.Add(record);
        }

        internal void End()
        {
            if (--_depth > 0) return;

            EditStep step = _open;
            _open = null;

            if (step == null || step.records.Count == 0) return;

            _undo.Add(step);
            _redo.Clear();

            if (_undo.Count > maxSteps) _undo.RemoveAt(0);
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;

            EditStep step = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);

            _applying = true;
            step.Undo();
            _applying = false;

            _redo.Add(step);
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;

            EditStep step = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);

            _applying = true;
            step.Redo();
            _applying = false;

            _undo.Add(step);
            return true;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _open = null;
            _depth = 0;
        }
    }

    // Everything recorded while one of these is open becomes a single step. A default instance
    // holds no stack and discards, which is what an editor with no session hands back.
    public readonly struct EditScope : IDisposable
    {
        private readonly UndoStack _stack;

        internal EditScope(UndoStack stack) => _stack = stack;

        public void Dispose() => _stack?.End();
    }
}
