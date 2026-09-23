using System.Numerics;

namespace ArctisAurora.Core.Animation
{
    // Named or anonymous values that springs follow, created and written from the main thread.
    public static class Signals
    {
        private sealed class Entry
        {
            public uint generation;
            public bool live;
            public string? name;
        }

        // signal id -> entry; ids are reused through free
        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly Stack<int> free = new Stack<int>();
        private static readonly Dictionary<string, SignalHandle> byName = new Dictionary<string, SignalHandle>();

        public static SignalHandle Create()
        {
            int id;
            if (free.Count > 0) id = free.Pop();
            else
            {
                id = entries.Count;
                entries.Add(new Entry());
            }

            Entry entry = entries[id];
            entry.generation++;
            entry.live = true;
            entry.name = null;
            SignalHandle handle = new SignalHandle(id, entry.generation);
            Set(handle, Vector4.Zero);
            return handle;
        }

        // The same signal for the same name, created on first use.
        public static SignalHandle Named(string name)
        {
            if (byName.TryGetValue(name, out SignalHandle handle)) return handle;

            handle = Create();
            entries[handle.id].name = name;
            byName[name] = handle;
            return handle;
        }

        public static void Set(SignalHandle handle, Vector4 value)
        {
            if (!IsLive(handle)) return;
            Animations.Write(new AnimationRequest { op = AnimationOp.SetSignal, track = handle.id, to = value });
        }

        public static void Release(SignalHandle handle)
        {
            if (!IsLive(handle)) return;

            Entry entry = entries[handle.id];
            if (entry.name != null) byName.Remove(entry.name);
            entry.live = false;
            entry.name = null;
            free.Push(handle.id);
        }

        private static bool IsLive(SignalHandle handle)
            => handle.id >= 0 && handle.id < entries.Count && entries[handle.id].live && entries[handle.id].generation == handle.generation;
    }
}
