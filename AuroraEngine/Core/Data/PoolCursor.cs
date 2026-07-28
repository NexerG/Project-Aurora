namespace ArctisAurora.Core.Data
{
    // One consumer's position in one pool's history. Poll at the top of a tick for what changed.
    //
    // This is the notification mechanism, and the reason there is no message bus: a bus costs a
    // routed message per change per subscriber and cannot coalesce; a cursor costs one comparison
    // per poll however much changed. Owned by one consumer and touched only on its thread, so
    // nothing here synchronises — the ordering lives in the pool's volatile published state. It is
    // also why the pool has no Clear: consumers advance their own cursor instead of racing to reset
    // a shared flag.
    //
    // "Consumer" is not "system" — the renderer needs one per swapchain image.
    public sealed class PoolCursor
    {
        private readonly DataPool _pool;

        private ulong _lastContent;
        private ulong _lastStructural;
        private ulong _lastOrder;

        // What this consumer currently holds resources for: slot version per stableId, 0 for none.
        // Mirrors the shape of DataPool.PublishedSlotVersion so the diff is a straight compare.
        private int[] _provisioned;

        private readonly List<int> _created = new();
        private readonly List<int> _destroyed = new();

        // A recycled slot appears in both — the old occupant's resources go, the new one's are built.
        public IReadOnlyList<int> Created => _created;
        public IReadOnlyList<int> Destroyed => _destroyed;

        // Set by the last TryConsumeStructural: dense indices moved, so anything keyed by dense
        // index is stale even when Created/Destroyed are empty. A permute is exactly that case.
        public bool OrderChanged { get; private set; }

        public DataPool Pool => _pool;

        // Non-consuming peek: three volatile reads, no state change, safe to call every frame.
        public bool HasPending =>
            _pool.ContentVersion != _lastContent
            || _pool.StructuralVersion != _lastStructural
            || _pool.OrderVersion != _lastOrder;

        public PoolCursor(DataPool pool)
        {
            _pool = pool;
            _provisioned = new int[pool.Capacity];
        }

        // Dense range with content changes since the last call. The range only means anything
        // against the generation that published it, which is safe because any frame edge that moved
        // dense indices also marked the whole pool dirty.
        public bool TryConsumeContent(out int min, out int max)
        {
            if (_pool.ContentVersion == _lastContent)
            {
                min = int.MaxValue;
                max = -1;
                return false;
            }

            // Advance to the version the scan covered, not the one the early-out saw — the pool can
            // close another generation in between, and consuming past it would drop its range.
            bool any = _pool.TryGetDirtyRange(_lastContent, out min, out max, out ulong scanned);
            _lastContent = scanned;
            return any;
        }

        // True when the pool changed shape since the last call; read Created/Destroyed/OrderChanged
        // for the detail. Returns true on a version move even when both lists come back empty — a
        // resequence permutes dense indices while the live set stays identical, so the diff finds
        // nothing but every dense-keyed resource is stale.
        public bool TryConsumeStructural()
        {
            ulong currentOrder = _pool.OrderVersion;
            OrderChanged = currentOrder != _lastOrder;
            _lastOrder = currentOrder;

            ulong current = _pool.StructuralVersion;
            if (current == _lastStructural)
            {
                _created.Clear();
                _destroyed.Clear();
                return OrderChanged;
            }

            // Version first, snapshot second. The pool stores the snapshot then publishes the
            // version with a release write, so a snapshot read after observing V is at least as new
            // as V. The other order pairs an old snapshot with a new version and marks unseen
            // changes consumed.
            int[] live = _pool.PublishedSlotVersion;

            if (_provisioned.Length < live.Length)
                Array.Resize(ref _provisioned, live.Length);

            _created.Clear();
            _destroyed.Clear();

            for (int sid = 0; sid < live.Length; sid++)
            {
                int theirs = live[sid];
                int mine = _provisioned[sid];
                if (mine == theirs) continue;

                if (mine != 0) _destroyed.Add(sid);
                if (theirs != 0) _created.Add(sid);
                _provisioned[sid] = theirs;
            }

            _lastStructural = current;
            return true;
        }

        // Forget everything and rebuild from scratch on the next poll. For a consumer whose
        // resources were torn down out of band — swapchain recreation, device loss.
        public void Reset()
        {
            Array.Clear(_provisioned);
            _lastStructural = 0;
            _lastContent = 0;
            _lastOrder = 0;
            OrderChanged = false;
            _created.Clear();
            _destroyed.Clear();
        }
    }
}
