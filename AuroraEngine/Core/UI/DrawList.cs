namespace ArctisAurora.Core.UI
{
    // The quads one window draws, in paint order, rebuilt by the walk that runs after Arrange.
    // Two parallel arrays because the two columns are two separate SSBOs the same instance index
    // addresses.
    public sealed class DrawList
    {
        private ControlGeometry[] _geometry = new ControlGeometry[256];
        private VulkanControl[] _visual = new VulkanControl[256];

        // the walk's fill position, and what the render thread may read
        private int _cursor;
        private int _count;

        public int Count => Volatile.Read(ref _count);
        public int Capacity => _geometry.Length;

        public ControlGeometry[] Geometry => _geometry;
        public VulkanControl[] Visual => _visual;

        public ref ControlGeometry GeometryAt(int slot) => ref _geometry[slot];
        public ref VulkanControl VisualAt(int slot) => ref _visual[slot];

        // Rewinds the walk. The count the render thread sees stays on the last completed frame.
        public void Clear() => _cursor = 0;

        // Hands the walk's result to the render thread. The write is ordered after the slot writes.
        public void Publish() => Volatile.Write(ref _count, _cursor);

        // Appends a slot and returns its index. The pair is NOT cleared — an emitter writes both
        // structs whole, or it inherits whatever the slot held some frames ago.
        public int Next()
        {
            if (_cursor == _geometry.Length)
            {
                Array.Resize(ref _geometry, _geometry.Length * 2);
                Array.Resize(ref _visual, _visual.Length * 2);
            }
            return _cursor++;
        }
    }
}
