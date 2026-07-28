namespace ArctisAurora.Core.Data.Commands
{
    // Turns one drained command back into a write on the owning pool. Runs on the owner's thread
    // only, which is what makes writing the column in place safe.
    public static class CommandApplier
    {
        public static void Apply(in SystemCommand cmd, CommandArena arena)
        {
            DataPool pool = DataManager.Get(cmd.PoolId);
            DataHandle target = new(cmd.PoolId, cmd.StableId, cmd.Version);

            switch (cmd.Op)
            {
                case CommandOp.Free:
                    pool.Free(target);   // already deferred to the frame edge, and idempotent
                    return;

                case CommandOp.Allocate:
                    // Fire and forget: commands have no return path, so the producer never learns
                    // the handles. Only useful for a system that will find the new slots by diffing
                    // a PoolCursor; anything needing the handle must allocate on the owning thread
                    // until there is a response channel.
                    for (int i = 0; i < cmd.Count; i++)
                        pool.Allocate();
                    return;
            }

            int dense = pool.DenseOf(target);
            if (dense < 0) return;   // slot recycled since enqueue — drop rather than clobber

            if (cmd.Count < 1 || dense + cmd.Count > pool.Count)
            {
                Console.WriteLine($"[CommandApplier] {cmd.Op} from system {cmd.Producer} on pool '{pool.Name}' spans dense [{dense},{dense + cmd.Count - 1}] but the pool holds {pool.Count} — dropped.");
                return;
            }

            IPoolColumn column = pool.ColumnAt(cmd.ColumnId);
            int size = column.ElementSize;

            switch (cmd.Op)
            {
                case CommandOp.SetOne:
                    column.WriteBytes(dense, 1, arena.ReadBytes(cmd.ArenaOffset, size));
                    break;

                // Range ops resolve only their START through the handle and then run contiguously
                // in DENSE space, the only space in which anything is contiguous. The version check
                // above therefore covers the first element, not the rest — these are for a producer
                // that knows the run is stable; use SetOne per element when it is not.
                case CommandOp.SetRange:
                    column.WriteBytes(dense, cmd.Count, arena.ReadBytes(cmd.ArenaOffset, size * cmd.Count));
                    break;

                case CommandOp.FillRange:
                    column.FillBytes(dense, cmd.Count, arena.ReadBytes(cmd.ArenaOffset, size));
                    break;

                case CommandOp.CopyRange:
                    column.CopyWithin(dense, arena.Read<int>(cmd.ArenaOffset), cmd.Count);
                    break;

                default:
                    return;
            }

            pool.MarkRangeDirty(dense, dense + cmd.Count - 1);
        }
    }
}
