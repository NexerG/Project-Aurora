using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // Which notification a dispatch carries. Every phase walks the same way — from the control under
    // the pointer up through its parents until one consumes it.
    public enum PointerPhase
    {
        Enter,
        Exit,
        Move,
        Press,
        Release,
        Tap
    }

    // One pointer notification. target is the control that was actually under the pointer, so an
    // ancestor handling a bubbled event still knows what was hit.
    public struct PointerEvent
    {
        public const int leftButton = 0;
        public const int rightButton = 1;

        public Control target;
        public Vector2D<float> point;
        public Vector2D<float> delta;
        public int button;
        public int tapCount;
    }
}
