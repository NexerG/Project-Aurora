using System.Diagnostics;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Threading;

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
        private static readonly LogChannel Log = LogChannel.For("Data");

        public ushort Id { get; }
        public string Name { get; }
        public bool Ordered { get; }

        private readonly PoolGrowthType _growthMode;
        private readonly int _growthValue;

        private readonly Dictionary<Type, IPoolColumn> _columns = new();
        private readonly Dictionary<Type, ushort> _columnIds = new();
        private readonly IPoolColumn[] _columnsByIndex;
        // columns and column ids by ColumnKey<T>.index
        private readonly IPoolColumn[] _byKey;
        private readonly ushort[] _idByKey;
        private int[] _slots;     // stableId -> dense index (-1 if free)
        private int[] _backMap;    // dense index -> stableId
        private int[] _versions;   // stableId -> version (>= 1 for a live/recyclable slot)
        private object[] _owners;  // dense index -> proxy back-reference (managed sidecar)
        private int[] _permuteScratch;    // resequence scratch
        private object[] _ownersScratch;
        private readonly PriorityQueue<int, int> _freeIds = new(); // lowest stableId first
        private int _highStableId; // next never-issued stableId
        private int _count;
        private int _capacity;
        private readonly int _initialCapacity;
        private int _versionFloor = 1;

        // shrink window
        public const double ShrinkAfterSeconds = 2.0;
        private long _lowSince;
        private int _lowPeak;
        private readonly int _slotBytes;

        private readonly HashSet<int> _pendingFree = new();
        private bool _orderDirty;

        public int Count => _count;
        public int Capacity => _capacity;

        // Bytes of every capacity-sized array the pool holds.
        public long ReservedBytes => (long)_capacity * _slotBytes;
        public bool StructuralDirty { get; private set; }

        // Dirty range in dense-index space: the contiguous slice [DirtyMin, DirtyMax] (inclusive)
        // with unuploaded changes. DirtyMax < DirtyMin means nothing is dirty. The renderer
        // re-uploads only this slice — requires the GPU buffer to mirror pool dense order.
        private int _dirtyMin = int.MaxValue;
        private int _dirtyMax = -1;
        public int DirtyMin => _dirtyMin;
        public int DirtyMax => _dirtyMax;
        public bool ContentDirty => _dirtyMax >= _dirtyMin;

        // ---- generation versioning (what consumers poll) ----
        //
        // Versions count GENERATIONS, not writes: a write widens the open generation's dirty range,
        // FrameEdge closes it and bumps the counter. Per-write versions would burn one per dirtied
        // control — 1000 in a tick — blowing past any bounded history, so every consumer would fall
        // back to full-range and the mechanism would buy nothing. Publishing at the frame edge also
        // hands consumers a settled batch and gives one place for the release write.
        private const int DirtyLogSize = 16;   // power of two: indexed with & (DirtyLogSize - 1)

        private readonly (ulong version, int min, int max)[] _dirtyLog = new (ulong, int, int)[DirtyLogSize];
        private ulong _contentVersion;
        private ulong _structuralVersion;
        private ulong _orderVersion;
        private bool _structuralPending;
        private bool _orderPending;

        // Snapshot of the live set in stableId space as of StructuralVersion: slot version if live,
        // 0 if free. Immutable once published, so a consumer can hold it across a scan with no
        // tearing and no locks.
        //
        // A bitset would be a quarter the size but cannot see ABA: free slot 5 and reallocate it
        // before the next frame edge and the bit reads 1 both times while the occupant changed. Not
        // a corner case — _freeIds hands out the lowest id, so a freed low slot is reissued next. The
        // version makes that show up as a destroy plus a create, which is what it is.
        private int[] _publishedSlotVersion;

        // Latest closed generation. Read these first, then read the data they describe.
        public ulong ContentVersion => Volatile.Read(ref _contentVersion);
        public ulong StructuralVersion => Volatile.Read(ref _structuralVersion);

        // Bumped only when dense indices MOVE — compaction and resequence. Separate from
        // StructuralVersion (the live set changing) so a plain Allocate can still take the cheap
        // append path; folded together, every added control would force a full descriptor rebuild.
        public ulong OrderVersion => Volatile.Read(ref _orderVersion);

        // Immutable snapshot of slot occupancy as of StructuralVersion. Index is stableId.
        public int[] PublishedSlotVersion => Volatile.Read(ref _publishedSlotVersion);

        // Union of the dirty ranges published in generations (since, current]. False when nothing
        // changed. A consumer more than DirtyLogSize generations behind has had its history
        // overwritten and gets the whole live range instead — correct, just not minimal.
        public bool TryGetDirtyRange(ulong since, out int min, out int max, out ulong current)
        {
            current = Volatile.Read(ref _contentVersion);
            min = int.MaxValue;
            max = -1;
            if (since >= current) return false;

            if (current - since > DirtyLogSize)
            {
                min = 0;
                max = _count - 1;
                return max >= min;
            }

            for (ulong v = since + 1; v <= current; v++)
            {
                ref (ulong version, int min, int max) rec = ref _dirtyLog[v & (DirtyLogSize - 1)];
                if (rec.version != v) continue;   // never written (no dirt that generation)
                if (rec.min < min) min = rec.min;
                if (rec.max > max) max = rec.max;
            }
            return max >= min;
        }

        // Supplies the desired dense order (as stableIds) when the pool is resequenced.
        // Resolved from the pool's SortAction, or set directly (tests / systems).
        public Func<DataPool, IReadOnlyList<int>>? SortProvider { get; set; }

        public DataPool(ushort id, string name, int capacity, bool ordered, PoolGrowthType growthMode, int growthValue, IEnumerable<Type> componentTypes)
        {
            if (capacity < 1) capacity = 1;
            Id = id;
            Name = name;
            Ordered = ordered;
            _growthMode = growthMode;
            _growthValue = growthValue < 1 ? 1 : growthValue;
            _capacity = capacity;
            _initialCapacity = capacity;

            _slots = new int[capacity];
            _backMap = new int[capacity];
            _versions = new int[capacity];
            _owners = new object[capacity];
            _permuteScratch = new int[capacity];
            _ownersScratch = new object[capacity];
            Array.Fill(_versions, 1);

            _publishedSlotVersion = new int[capacity];

            // Column ids come from Pools.pools.xml declaration order, like pool ids from parse order.
            // Nothing in C# declares the mapping — reorder the <Component> elements and every
            // in-flight command's ColumnId means something else.
            List<IPoolColumn> columnOrder = new();
            _slotBytes = 4 * sizeof(int) + IntPtr.Size;
            foreach (Type t in componentTypes)
            {
                Type columnType = typeof(PoolColumn<>).MakeGenericType(t);
                IPoolColumn column = (IPoolColumn)Activator.CreateInstance(columnType, _capacity);
                _columnIds[t] = (ushort)columnOrder.Count;
                _columns[t] = column;
                columnOrder.Add(column);
                _slotBytes += column.ElementSize;
            }
            _columnsByIndex = columnOrder.ToArray();

            int keyCount = 0;
            foreach (IPoolColumn col in _columnsByIndex)
                keyCount = Math.Max(keyCount, ColumnKeys.Of(col.ElementType) + 1);
            _byKey = new IPoolColumn[keyCount];
            _idByKey = new ushort[keyCount];
            for (ushort column = 0; column < _columnsByIndex.Length; column++)
            {
                int key = ColumnKeys.Of(_columnsByIndex[column].ElementType);
                _byKey[key] = _columnsByIndex[column];
                _idByKey[key] = column;
            }
        }

        // DEBUG only: a step touching columns it did not declare in Frame.frame.xml throws. need is the
        // column bits the call touches, 0 for any column. Outside a step nothing is checked, except
        // that a dedicated thread never writes.
        [Conditional("DEBUG")]
        private void AssertAccess(ulong need, bool write, string op)
        {
            FrameStep? step = FrameStep.Current;
            if (step == null)
            {
                ThreadedSystem? current = ThreadedSystem.Current;
                if (write && current != null && current.Dedicated)
                    throw new Exception($"[DataPool] {op} writes '{Name}' from dedicated '{current.Name}' — a dedicated thread only reads pools.");
                return;
            }

            ulong have = write ? step.writes[Id] : step.reads[Id] | step.writes[Id];
            if (need == 0 ? have != 0 : (have & need) == need) return;

            throw new Exception($"[DataPool] {op} on '{Name}' from step '{step.Name}', which does not list it in Frame.frame.xml {(write ? "Writes" : "Reads")}.");
        }

        // DEBUG only: a structural write, checked as a write of every column and refused inside a Jobs.For chunk.
        [Conditional("DEBUG")]
        private void AssertStructural(string op)
        {
            if (Jobs.InChunk)
                throw new Exception($"[DataPool] {op} on '{Name}' inside a Jobs.For chunk — structural writes are not thread-safe.");
            AssertAccess(FrameStep.AllColumns(this), true, op);
        }

        private ulong Bit<T>() where T : struct => 1UL << _idByKey[ColumnKey<T>.index];

        public bool HasComponent(Type t) => _columns.ContainsKey(t);

        public int ColumnCount => _columnsByIndex.Length;

        public IPoolColumn ColumnAt(ushort columnId) => _columnsByIndex[columnId];

        public ushort ColumnId(Type t) => _columnIds[t];

        public ushort ColumnId<T>() where T : struct => _idByKey[ColumnKey<T>.index];

        // Dense index for a handle, or -1 if stale.
        public int DenseOf(DataHandle h) => Alive(h) ? _slots[h.StableId] : -1;

        public Span<T> GetSpan<T>() where T : struct
        {
            AssertAccess(Bit<T>(), true, nameof(GetSpan));
            return ((PoolColumn<T>)_byKey[ColumnKey<T>.index]).data.AsSpan(0, _count);
        }


        public ref T GetRef<T>(DataHandle h) where T : struct
        {
            AssertAccess(Bit<T>(), true, nameof(GetRef));
            int dense = _slots[h.StableId];
            return ref ((PoolColumn<T>)_byKey[ColumnKey<T>.index]).data[dense];
        }

        // One row of a column as raw bytes, empty when the handle is stale.
        public Span<byte> ElementBytes(ushort column, DataHandle h)
        {
            AssertAccess(1UL << column, true, nameof(ElementBytes));
            int dense = DenseOf(h);
            return dense < 0 ? Span<byte>.Empty : _columnsByIndex[column].ElementBytes(dense);
        }

        private T[] Column<T>() where T : struct => ((PoolColumn<T>)_byKey[ColumnKey<T>.index]).data;

        // The full backing array for component T (length Capacity), for a bulk GPU upload sized
        // to the pool's capacity. Only dense [0,Count) is live; the tail is unused slack. The
        // renderer mirrors this straight to a GPU buffer, so it must stay in dense order — read
        // only, never structurally mutated by the caller.
        //
        // Checked as a read: this and CopyTo/CopyRange/OwnerAt are the reads a dedicated thread may
        // make, which is how the render thread gets at the pools at all. C# cannot hand out a
        // read-only T[], so "read only" here is still convention.
        public T[] Backing<T>() where T : struct
        {
            AssertAccess(Bit<T>(), false, nameof(Backing));
            return Column<T>();
        }

        // ---- bulk data transfer (per component type) ----

        // Copy this pool's full live data for component T out into dest (dest.Length >= Count).
        public void CopyTo<T>(Span<T> dest) where T : struct
        {
            AssertAccess(Bit<T>(), false, nameof(CopyTo));
            Column<T>().AsSpan(0, _count).CopyTo(dest);
        }

        // Copy an external array into component T's dense storage from index 0, and dirty it.
        // A raw data refresh — Count is unchanged (lifecycle stays with Allocate/Free).
        public void CopyFrom<T>(ReadOnlySpan<T> src) where T : struct
        {
            AssertAccess(Bit<T>(), true, nameof(CopyFrom));
            src.CopyTo(Column<T>().AsSpan(0, src.Length));
            MarkRangeDirty(0, src.Length - 1);
        }

        // Copy a dense range [from, to] (inclusive) of component T out into dest.
        public void CopyRange<T>(int from, int to, Span<T> dest) where T : struct
        {
            AssertAccess(Bit<T>(), false, nameof(CopyRange));
            Column<T>().AsSpan(from, to - from + 1).CopyTo(dest);
        }

        // Overwrite a dense range [from, to] (inclusive) of component T from src, and dirty it.
        public void UpdateRange<T>(int from, int to, ReadOnlySpan<T> src) where T : struct
        {
            AssertAccess(Bit<T>(), true, nameof(UpdateRange));
            int len = to - from + 1;
            src.Slice(0, len).CopyTo(Column<T>().AsSpan(from, len));
            MarkRangeDirty(from, to);
        }

        public bool Alive(DataHandle h)
            => h.PoolId == Id
               && (uint)h.StableId < (uint)_capacity
               && _versions[h.StableId] == h.Version
               && _slots[h.StableId] >= 0;

        public object OwnerAt(int denseIndex)
        {
            AssertAccess(0, false, nameof(OwnerAt));
            return _owners[denseIndex];
        }

        public DataHandle Allocate(object owner = null)
        {
            AssertStructural(nameof(Allocate));
            if (_count >= _capacity)
                Grow();

            int stableId = _freeIds.Count > 0 ? _freeIds.Dequeue() : _highStableId++;
            int dense = _count++;
            foreach (IPoolColumn col in _columnsByIndex)
                col.Clear(dense);
            _slots[stableId] = dense;
            _backMap[dense] = stableId;
            _owners[dense] = owner;

            _structuralPending = true;

            StructuralDirty = true;                    // instance count changed
            if (dense < _dirtyMin) _dirtyMin = dense;  // the new element needs uploading
            if (dense > _dirtyMax) _dirtyMax = dense;
            return new DataHandle(Id, stableId, _versions[stableId]);
        }

        // Empties a handle-less pool.
        public void Rewind()
        {
            AssertStructural(nameof(Rewind));
            _count = 0;
        }

        // Appends an uncleared row to a handle-less pool and returns its dense index.
        public int Append()
        {
            AssertStructural(nameof(Append));
            if (_count >= _capacity)
                Grow();

            int dense = _count++;
            if (dense < _dirtyMin) _dirtyMin = dense;
            if (dense > _dirtyMax) _dirtyMax = dense;
            return dense;
        }

        // Appends count uncleared rows to a handle-less pool and returns the first one's dense index.
        public int Append(int count)
        {
            AssertStructural(nameof(Append));
            while (_count + count > _capacity)
                Grow();

            int first = _count;
            _count += count;
            if (first < _dirtyMin) _dirtyMin = first;
            if (_count - 1 > _dirtyMax) _dirtyMax = _count - 1;
            return first;
        }

        // Deferred: enqueue only. The slot stays alive (handle valid) until FrameEdge drains
        // it. A repeat or stale Free is a no-op.
        public void Free(DataHandle h)
        {
            AssertStructural(nameof(Free));
            if (!Alive(h)) return;
            _pendingFree.Add(h.StableId);
        }

        // Expand the dirty range to include this element (dense index resolved from the handle).
        public void MarkContentDirty(DataHandle h)
        {
            AssertAccess(0, true, nameof(MarkContentDirty));
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
            AssertAccess(0, true, nameof(MarkRangeDirty));
            if (from < _dirtyMin) _dirtyMin = from;
            if (to > _dirtyMax) _dirtyMax = to;
        }

        public void MarkOrderDirty()
        {
            AssertAccess(0, true, nameof(MarkOrderDirty));
            _orderDirty = true;
        }

        public void ClearDirty()
        {
            _dirtyMin = int.MaxValue;
            _dirtyMax = -1;
            StructuralDirty = false;
        }

        // Runs between frames. Order matters: remove dead, then resequence survivors.
        //
        // Guarded like a write of every column because it is one — compaction moves pool memory.
        public void FrameEdge()
        {
            AssertStructural(nameof(FrameEdge));
            if (_pendingFree.Count > 0)
            {
                if (Ordered) CompactOrdered();
                else SwapRemoveDead();
                _pendingFree.Clear();
                StructuralDirty = true;
                MarkAllDirty();
                _orderPending = true;   // survivors shifted down into the holes
            }

            // A refused resequence keeps the flag, so the reorder is retried at the next edge
            // rather than dropped — clearing it unconditionally would leave dense order
            // permanently disagreeing with the tree over one bad frame.
            if (Ordered && _orderDirty && SortProvider != null && Resequence())
            {
                _orderDirty = false;
                StructuralDirty = true;
                MarkAllDirty();

                // A permute leaves the live SET identical, so the slot-version snapshot shows no
                // difference. OrderVersion is what says "your dense-keyed resources are wrong now"
                // when nothing was born or died; structural is bumped too so a consumer watching
                // only that still reacts.
                _structuralPending = true;
                _orderPending = true;
            }

            TryShrink();
            PublishGeneration();
        }

        // Close the open generation and hand it to consumers. Runs at the end of FrameEdge, after
        // compaction and resequence, so what it publishes describes the settled layout.
        //
        // Order is load-bearing: record and snapshot stored first, version last with a release
        // write, so a consumer that has observed V observes everything V describes.
        private void PublishGeneration()
        {
            if (_dirtyMax >= _dirtyMin)
            {
                ulong next = _contentVersion + 1;
                _dirtyLog[next & (DirtyLogSize - 1)] = (next, _dirtyMin, _dirtyMax);
                Volatile.Write(ref _contentVersion, next);
                _dirtyMin = int.MaxValue;
                _dirtyMax = -1;
            }

            if (_structuralPending)
            {
                int[] snapshot = new int[_capacity];
                for (int sid = 0; sid < _highStableId; sid++)
                    snapshot[sid] = _slots[sid] >= 0 ? _versions[sid] : 0;

                Volatile.Write(ref _publishedSlotVersion, snapshot);
                Volatile.Write(ref _structuralVersion, _structuralVersion + 1);
                _structuralPending = false;
            }

            if (_orderPending)
            {
                Volatile.Write(ref _orderVersion, _orderVersion + 1);
                _orderPending = false;
            }
        }

        private void Recycle(int stableId)
        {
            _versions[stableId]++;   // invalidates every outstanding handle to this slot
            _slots[stableId] = -1;
            _structuralPending = true;
            _freeIds.Enqueue(stableId, stableId);
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
            ReleaseOwnerSlack(w, _count);
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

            int oldCount = _count;
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
            ReleaseOwnerSlack(_count, oldCount);
        }

        // Drop the managed back-references in the vacated range [newCount, oldCount). MoveDense
        // copies _owners down without clearing the source, so a removal pass leaves a duplicate for
        // every survivor it shifted and the entity itself for every row that died. Only _owners
        // needs this — the component columns are unmanaged, so their slack is just numbers nobody
        // reads.
        //
        // Without it a destroyed entity stays reachable from the pool, and so uncollectable, until
        // some later Allocate happens to reuse that exact dense index.
        private void ReleaseOwnerSlack(int newCount, int oldCount)
        {
            if (oldCount > newCount)
                Array.Clear(_owners, newCount, oldCount - newCount);
        }

        // False when the sort provider did not account for every live element — a live row it
        // cannot reach would be dropped out of the permutation entirely, so refusing is the only
        // safe answer. For UIControls that means a control the DFS walk never reached: its parent
        // is a control that does not list it as a child.
        private bool Resequence()
        {
            IReadOnlyList<int> order = SortProvider!(this);
            if (order.Count != _count)
            {
                Log.Warn($"'{Name}' resequence order count {order.Count} != live count {_count} — skipping.");
                return false;
            }

            int[] destToSrc = _permuteScratch;
            for (int i = 0; i < _count; i++)
                destToSrc[i] = _slots[order[i]];

            foreach (IPoolColumn col in _columns.Values)
                col.Permute(destToSrc, _count);

            for (int i = 0; i < _count; i++)
                _ownersScratch[i] = _owners[destToSrc[i]];
            Array.Copy(_ownersScratch, _owners, _count);
            Array.Clear(_ownersScratch, 0, _count);

            for (int i = 0; i < _count; i++)
            {
                int sid = order[i];
                _backMap[i] = sid;
                _slots[sid] = i;
            }
            return true;
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
            Resize(newCap);
        }

        // Halves capacity once the pool has stayed at most a quarter full for ShrinkAfterSeconds.
        private void TryShrink()
        {
            if (_capacity <= _initialCapacity || _count * 4 > _capacity)
            {
                _lowSince = 0;
                return;
            }

            long now = Stopwatch.GetTimestamp();
            if (_lowSince == 0)
            {
                _lowSince = now;
                _lowPeak = _count;
                return;
            }
            _lowPeak = Math.Max(_lowPeak, _count);
            if (Stopwatch.GetElapsedTime(_lowSince, now).TotalSeconds < ShrinkAfterSeconds) return;
            _lowSince = 0;

            int highestLive = -1;
            for (int i = 0; i < _count; i++)
                highestLive = Math.Max(highestLive, _backMap[i]);

            int bound = Math.Max(_initialCapacity, Math.Max(2 * _lowPeak, highestLive + 1));
            int newCap = _capacity;
            while (newCap / 2 >= bound)
                newCap /= 2;
            if (newCap == _capacity) return;

            for (int i = newCap; i < _capacity; i++)
                _versionFloor = Math.Max(_versionFloor, _versions[i] + 1);

            List<int> kept = new List<int>();
            foreach ((int id, int _) in _freeIds.UnorderedItems)
                if (id < newCap) kept.Add(id);
            _freeIds.Clear();
            foreach (int id in kept)
                _freeIds.Enqueue(id, id);
            _highStableId = Math.Min(_highStableId, newCap);

            Resize(newCap);
        }

        // Reallocates every capacity-sized array; a slot issued past the old capacity starts at the version floor.
        private void Resize(int newCap)
        {
            foreach (IPoolColumn col in _columns.Values)
                col.Grow(newCap);

            Array.Resize(ref _slots, newCap);
            Array.Resize(ref _backMap, newCap);
            Array.Resize(ref _owners, newCap);
            _permuteScratch = new int[newCap];
            _ownersScratch = new object[newCap];
            int old = _versions.Length;
            Array.Resize(ref _versions, newCap);
            for (int i = old; i < newCap; i++)
                _versions[i] = _versionFloor;

            _structuralPending = true;   // consumers size their own tables off the published one

            _capacity = newCap;
            StructuralDirty = true;   // buffer reallocated — full re-upload + descriptor rebuild
            MarkAllDirty();
        }
    }

    // A process-wide dense index per column type.
    internal static class ColumnKeys
    {
        private static readonly Dictionary<Type, int> _keys = new();

        public static int Of(Type t)
        {
            lock (_keys)
            {
                if (!_keys.TryGetValue(t, out int key)) _keys[t] = key = _keys.Count;
                return key;
            }
        }
    }

    internal static class ColumnKey<T> where T : struct
    {
        public static readonly int index = ColumnKeys.Of(typeof(T));
    }
}
