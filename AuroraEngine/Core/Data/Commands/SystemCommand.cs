using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Data.Commands
{
    // What a command does to the target column. Every op is pure data — there is deliberately no
    // delegate or closure form, because a command that cannot be inspected cannot be sorted,
    // deduplicated or coalesced, and that batching is the entire reason the queue exists.
    public enum CommandOp : byte
    {
        None = 0,

        // Write one element. Payload is a single T at ArenaOffset.
        SetOne,

        // Write Count contiguous elements starting at StableId. Payload is Count T's.
        SetRange,

        // Write the same value into Count contiguous elements. Payload is a single T.
        FillRange,

        // Copy Count elements from another element range of the same column. Payload is a single
        // int: the source start index.
        CopyRange,

        // Lifecycle. Neither carries a payload.
        Allocate,
        Free,
    }

    // One message from a system to the thread that owns a data table. 24 bytes, unmanaged, so a
    // stream of these can live in a lock-free ring and be radix-sorted in place.
    //
    // The target is identified by (PoolId, StableId, Version) — never by a dense/storage index. A
    // slot can be freed and recycled between the moment a command is enqueued and the moment the
    // owner drains it; the version is what lets the owner notice that and drop the command instead
    // of writing over whoever now lives in that slot.
    //
    // Payloads always live in the producer's CommandArena, never inline. That keeps this struct
    // uniform and sortable, and the arena is a bump allocator in the producer's own memory, so the
    // "indirection" is a sequential read of a hot buffer rather than a pointer chase.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SystemCommand
    {
        public ushort PoolId;
        public ushort ColumnId;
        public int StableId;      // target slot, or start of the range for the range ops
        public int Version;       // handle version at enqueue time; stale commands are dropped
        public int Count;         // 1 for scalar ops, element count for range ops
        public int ArenaOffset;   // byte offset into the producer's arena, -1 when there is no payload
        public CommandOp Op;
        public byte Producer;     // originating system id, so an apply-time fault names its source

        // Packed ordering key for the planner's radix sort: pool, then column, then slot. Sorting
        // on this is what turns a scattered stream of SetOne into contiguous runs that collapse
        // into a handful of copies.
        public readonly ulong SortKey => ((ulong)PoolId << 48) | ((ulong)ColumnId << 32) | (uint)StableId;

        public static SystemCommand SetOne(DataHandle target, ushort columnId, int arenaOffset, byte producer) => new SystemCommand
        {
            PoolId = target.PoolId,
            ColumnId = columnId,
            StableId = target.StableId,
            Version = target.Version,
            Count = 1,
            ArenaOffset = arenaOffset,
            Op = CommandOp.SetOne,
            Producer = producer,
        };

        public static SystemCommand SetRange(DataHandle start, ushort columnId, int count, int arenaOffset, byte producer) => new SystemCommand
        {
            PoolId = start.PoolId,
            ColumnId = columnId,
            StableId = start.StableId,
            Version = start.Version,
            Count = count,
            ArenaOffset = arenaOffset,
            Op = CommandOp.SetRange,
            Producer = producer,
        };

        public static SystemCommand FillRange(DataHandle start, ushort columnId, int count, int arenaOffset, byte producer) => new SystemCommand
        {
            PoolId = start.PoolId,
            ColumnId = columnId,
            StableId = start.StableId,
            Version = start.Version,
            Count = count,
            ArenaOffset = arenaOffset,
            Op = CommandOp.FillRange,
            Producer = producer,
        };

        public static SystemCommand CopyRange(DataHandle dest, ushort columnId, int count, int sourceOffsetInArena, byte producer) => new SystemCommand
        {
            PoolId = dest.PoolId,
            ColumnId = columnId,
            StableId = dest.StableId,
            Version = dest.Version,
            Count = count,
            ArenaOffset = sourceOffsetInArena,
            Op = CommandOp.CopyRange,
            Producer = producer,
        };

        public static SystemCommand Free(DataHandle target, byte producer) => new SystemCommand
        {
            PoolId = target.PoolId,
            ColumnId = 0,
            StableId = target.StableId,
            Version = target.Version,
            Count = 1,
            ArenaOffset = -1,
            Op = CommandOp.Free,
            Producer = producer,
        };

        public static SystemCommand Allocate(ushort poolId, int count, byte producer) => new SystemCommand
        {
            PoolId = poolId,
            ColumnId = 0,
            StableId = -1,
            Version = 0,
            Count = count,
            ArenaOffset = -1,
            Op = CommandOp.Allocate,
            Producer = producer,
        };
    }
}
