using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using System.Numerics;

namespace ArctisAurora.Core.Animation
{
    // Named or anonymous values that springs follow, created and written from the main thread.
    public static class Signals
    {
        // signal id -> its name, beside its SignalLife row; ids are reused through free
        private static string?[] names = new string?[64];
        private static readonly Stack<int> free = new Stack<int>();
        private static readonly Dictionary<string, SignalHandle> byName = new Dictionary<string, SignalHandle>();

        private static DataPool? _pool;
        private static DataPool Pool => _pool ??= DataManager.Get("Signals");

        public static SignalHandle Create()
        {
            DataPool pool = Pool;
            int id;
            if (free.Count > 0) id = free.Pop();
            else
            {
                id = pool.Append();
                pool.GetSpan<SignalValue>()[id] = default;
                pool.GetSpan<SignalLife>()[id] = default;
                if (id == names.Length) Array.Resize(ref names, id * 2);
            }

            ref SignalLife life = ref pool.GetSpan<SignalLife>()[id];
            life.generation++;
            life.live = true;
            names[id] = null;
            SignalHandle handle = new SignalHandle(id, life.generation);
            Set(handle, Vector4.Zero);
            return handle;
        }

        // The same signal for the same name, created on first use.
        public static SignalHandle Named(string name)
        {
            if (byName.TryGetValue(name, out SignalHandle handle)) return handle;

            handle = Create();
            names[handle.id] = name;
            byName[name] = handle;
            return handle;
        }

        // Writes the value and wakes the tracks following it when it changed.
        public static void Set(SignalHandle handle, Vector4 value)
        {
            if (!IsLive(handle)) return;
            Profiling.Zone.Increment("Anim.Request");
            DataPool pool = Pool;
            ref SignalValue row = ref pool.GetSpan<SignalValue>()[handle.id];
            if (row.value == value) return;
            row.value = value;
            pool.MarkRangeDirty(handle.id, handle.id);
            Animations.WakeFollowers(handle.id);
        }

        // Whether a live signal holds something other than value.
        internal static bool Differs(SignalHandle handle, Vector4 value)
            => IsLive(handle) && Pool.Backing<SignalValue>()[handle.id].value != value;

        public static void Release(SignalHandle handle)
        {
            if (!IsLive(handle)) return;

            string? name = names[handle.id];
            if (name != null) byName.Remove(name);
            Pool.GetSpan<SignalLife>()[handle.id].live = false;
            names[handle.id] = null;
            free.Push(handle.id);
        }

        private static bool IsLive(SignalHandle handle)
        {
            DataPool pool = Pool;
            if (handle.id < 0 || handle.id >= pool.Count) return false;
            ref readonly SignalLife life = ref pool.Backing<SignalLife>()[handle.id];
            return life.live && life.generation == handle.generation;
        }
    }
}
