using ArctisAurora.Core.UISystem.Controls.Interactable;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls
{
    // One line of a context menu. A disabled entry answers to nothing — hover, press and release all
    // stop here rather than reaching the action, so the ground never lights up either.
    public class ContextMenuItemControl : ButtonControl
    {
        internal bool enabled = true;

        public override void ResolveOnEnter()
        {
            if (enabled) base.ResolveOnEnter();
        }

        public override void ResolveExit()
        {
            if (enabled) base.ResolveExit();
        }

        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            if (enabled) base.ResolveOnClick(oldPos, delta);
        }

        public override void ResolveOnRelease()
        {
            if (enabled) base.ResolveOnRelease();
        }
    }
}
