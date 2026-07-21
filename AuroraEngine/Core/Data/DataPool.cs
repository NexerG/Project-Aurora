namespace ArctisAurora.Core.Data
{
    // A homogeneous block of entities: one dense array per component type, plus an
    // indirection table so packed storage survives removal and reordering.
    //
    // Two index spaces:
    //   stableId — [0,capacity), handed out from a free list, stable for an element's
    //              lifetime. _slots[stableId] -> dense index; _versions[stableId].
    //   dense    — [0,Count), tightly packed, what systems iterate. _backMap[dense] ->
    //              stableId; component columns and _owners are indexed by dense.
    //
    // All structural mutation (destroy drain, compaction, resequence) happens in FrameEdge,
    // between frames, never while a system iterates a span. Growth is the one exception —
    // it only runs from Allocate (control-lifecycle code, never mid-span-iteration).
    public sealed class DataPool
    {
        public ushort Id { get; }
        public string Name { get; }
        public bool Ordered { get; }

        private readonly PoolGrowthType _growthMode;
        private readonly int _growthValue;

        private readonly Dictionary<Type, IPoolColumn> _columns = new();
        private int[] _slots;      // stableId -> dense index (-1 if free)
        private int[] _backMap;    // dense index -> stableId
        private int[] _versions;   // stableId -> version (>= 1 for a live/recyclable slot)
        private object[] _owners;  // dense index -> proxy back-reference (managed sidecar)
        private readonly Stack<int> _freeIds = new();
        private int _highStableId; // next never-issued stableId
        private int _count;
        private int _capacity;

        private readonly HashSet<int> _pendingFree = new();
        private bool _orderDirty;

        public int Count => _count;
        public int Capacity => _capacity;
        public bool StructuralDirty { get; private set; }

        // Dirty range in dense-index space: the contiguous slice [DirtyMin, DirtyMax] (inclusive)
        // with unuploaded changes. DirtyMax < DirtyMin means nothing is dirty. The renderer
        // re-uploads only this slice — requires the GPU buffer to mirror pool dense order.
        private int _dirtyMin = int.MaxValue;
        private int _dirtyMax = -1;
        public int DirtyMin => _dirtyMin;
        public int DirtyMax => _dirtyMax;
        public bool ContentDirty => _dirtyMax >= _dirtyMin;

        // Supplies the desired dense order (as stableIds) when the pool is resequenced.
        // Resolved from the pool's SortAction, or set directly (tests / systems).
        public Func<DataPool, IReadOnlyList<int>> SortProvider { get; set; }

        public DataPool(ushort id, string name, int capacity, bool ordered, PoolGrowthType growthMode, int growthValue, IEnumerable<Type> componentTypes)
        {
            if (capacity < 1) capacity = 1;
            Id = id;
            Name = name;
            Ordered = ordered;
            _growthMode = growthMode;
            _growthValue = growthValue < 1 ? 1 : growthValue;
            _capacity = capacity;

            _slots = new int[capacity];
            _backMap = new int[capacity];
            _versions = new int[capacity];
            _owners = new object[capacity];
            Array.Fill(_versions, 1);

            foreach (Type t in componentTypes)
            {
                Type columnType = typeof(PoolColumn<>).MakeGenericType(t);
                _columns[t] = (IPoolColumn)Activator.CreateInstance(columnType, _capacity);
            }
        }

        public bool HasComponent(Type t) => _columns.ContainsKey(t);

        public Span<T> GetSpan<T>() where T : struct
            => ((PoolColumn<T>)_columns[typeof(T)]).data.AsSpan(0, _count);

        public ref T GetRef<T>(DataHandle h) where T : struct
        {
            int dense = _slots[h.StableId];
            return ref ((PoolColumn<T>)_columns[typeof(T)]).data[dense];
        }

        private T[] Column<T>() where T : struct => ((PoolColumn<T>)_columns[typeof(T)]).data;

        // ---- bulk data transfer (per component type) ----

        // Copy this pool's full live data for component T out into dest (dest.Length >= Count).
        public void CopyTo<T>(Span<T> dest) where T : struct
            => GetSpan<T>().CopyTo(dest);

        // Copy an external array into component T's dense storage from index 0, and dirty it.
        // A raw data refresh — Count is unchanged (lifecycle stays with Allocate/Free).
        public void CopyFrom<T>(ReadOnlySpan<T> src) where T : struct
        {
            src.CopyTo(Column<T>().AsSpan(0, src.Length));
            MarkRangeDirty(0, src.Length - 1);
        }

        // Copy a dense range [from, to] (inclusive) of component T out into dest.
        public void CopyRange<T>(int from, int to, Span<T> dest) where T : struct
            => Column<T>().AsSpan(from, to - from + 1).CopyTo(dest);

        // Overwrite a dense range [from, to] (inclusive) of component T from src, and dirty it.
        public void UpdateRange<T>(int from, int to, ReadOnlySpan<T> src) where T : struct
        {
            int len = to - from + 1;
            src.Slice(0, len).CopyTo(Column<T>().AsSpan(from, len));
            MarkRangeDirty(from, to);
        }

        public bool Alive(DataHandle h)
            => h.PoolId == Id
               && (uint)h.StableId < (uint)_capacity
               && _versions[h.StableId] == h.Version
               && _slots[h.StableId] >= 0;

        public object OwnerAt(int denseIndex) => _owners[denseIndex];

        public DataHandle Allocate(object owner = null)
        {
            if (_count >= _capacity)
                Grow();

            int stableId = _freeIds.Count > 0 ? _freeIds.Pop() : _highStableId++;
            int dense = _count++;
            _slots[stableId] = dense;
            _backMap[dense] = stableId;
            _owners[dense] = owner;

            StructuralDirty = true;                    // instance count changed
            if (dense < _dirtyMin) _dirtyMin = dense;  // the new element needs uploading
            if (dense > _dirtyMax) _dirtyMax = dense;
            return new DataHandle(Id, stableId, _versions[stableId]);
        }

        // Deferred: enqueue only. The slot stays alive (handle valid) until FrameEdge drains
        // it. A repeat or stale Free is a no-op.
        public void Free(DataHandle h)
        {
            if (!Alive(h)) return;
            _pendingFree.Add(h.StableId);
        }

        // Expand the dirty range to include this element (dense index resolved from the handle).
        public void MarkContentDirty(DataHandle h)
        {
            int dense = _slots[h.StableId];
            if (dense < 0) return;
            if (dense < _dirtyMin) _dirtyMin = dense;
            if (dense > _dirtyMax) _dirtyMax = dense;
        }

        // Whole buffer changed (reorder / realloc / count change) — dirty the entire live range.
        public void MarkAllDirty()
        {
            _dirtyMin = 0;
            _dirtyMax = _count - 1;
        }

        // Expand the dirty range to cover the dense range [from, to] (inclusive).
        public void MarkRangeDirty(int from, int to)
        {
            if (from < _dirtyMin) _dirtyMin = from;
            if (to > _dirtyMax) _dirtyMax = to;
        }

        public void MarkOrderDirty() => _orderDirty = true;

        public void ClearDirty()
        {
            _dirtyMin = int.MaxValue;
            _dirtyMax = -1;
            StructuralDirty = false;
        }

        // Runs between frames. Order matters: remove dead, then resequence survivors.
        public void FrameEdge()
        {
            if (_pendingFree.Count > 0)
            {
                if (Ordered) CompactOrdered();
                else SwapRemoveDead();
                _pendingFree.Clear();
                StructuralDirty = true;
                MarkAllDirty();
            }

            if (Ordered && _orderDirty && SortProvider != null)
            {
                Resequence();
                _orderDirty = false;
                StructuralDirty = true;
                MarkAllDirty();
            }
        }

        private void Recycle(int stableId)
        {
            _versions[stableId]++;   // invalidates every outstanding handle to this slot
            _slots[stableId] = -1;
            _freeIds.Push(stableId);
        }

        // Order-preserving batch compaction: one forward sweep, write cursor trails read.
        private void CompactOrdered()
        {
            int w = 0;
            for (int r = 0; r < _count; r++)
            {
                int sid = _backMap[r];
                if (_pendingFree.Contains(sid))
                {
                    Recycle(sid);
                    continue;
                }
                if (w != r)
                    MoveDense(r, w);
                w++;
            }
            _count = w;
        }

        // Unordered: fill each hole with the current last element. Process dead dense
        // indices high-to-low so a swapped-in survivor is never a not-yet-processed dead.
        private void SwapRemoveDead()
        {
            int[] deadDense = new int[_pendingFree.Count];
            int n = 0;
            foreach (int sid in _pendingFree)
                deadDense[n++] = _slots[sid];
            Array.Sort(deadDense);

            for (int i = n - 1; i >= 0; i--)
            {
                int hole = deadDense[i];
                int sid = _backMap[hole];
                int last = _count - 1;
                if (hole != last)
                    MoveDense(last, hole);
                _count--;
                Recycle(sid);
            }
        }

        private void Resequence()
        {
            IReadOnlyList<int> order = SortProvider(this);
            if (order.Count != _count)
            {
                Console.WriteLine($"[DataPool] '{Name}' resequence order count {order.Count} != live count {_count} — skipping.");
                return;
            }

            int[] destToSrc = new int[_count];
            for (int i = 0; i < _count; i++)
                destToSrc[i] = _slots[order[i]];

            foreach (IPoolColumn col in _columns.Values)
                col.Permute(destToSrc, _count);

            object[] ownersTmp = new object[_count];
            for (int i = 0; i < _count; i++)
                ownersTmp[i] = _owners[destToSrc[i]];
            Array.Copy(ownersTmp, _owners, _count);

            for (int i = 0; i < _count; i++)
            {
                int sid = order[i];
                _backMap[i] = sid;
                _slots[sid] = i;
            }
        }

        // Move one dense element (all columns + sidecar + back/forward maps) from -> to.
        private void MoveDense(int from, int to)
        {
            foreach (IPoolColumn col in _columns.Values)
                col.Move(from, to);
            _owners[to] = _owners[from];
            int sid = _backMap[from];
            _backMap[to] = sid;
            _slots[sid] = to;
        }

        private void Grow()
        {
            int newCap = _growthMode == PoolGrowthType.Multiplicative
                ? _capacity * _growthValue
                : _capacity + _growthValue;
            if (newCap <= _capacity) newCap = _capacity + 1;

            foreach (IPoolColumn col in _columns.Values)
                col.Grow(newCap);

            Array.Resize(ref _slots, newCap);
            Array.Resize(ref _backMap, newCap);
            Array.Resize(ref _owners, newCap);
            int old = _versions.Length;
            Array.Resize(ref _versions, newCap);
            for (int i = old; i < newCap; i++)
                _versions[i] = 1;

            _capacity = newCap;
            StructuralDirty = true;   // buffer reallocated — full re-upload + descriptor rebuild
            MarkAllDirty();
        }
    }
}
