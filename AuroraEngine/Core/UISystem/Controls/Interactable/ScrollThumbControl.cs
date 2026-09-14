using ArctisAurora.Core.UISystem.Controls.Containers;
using System.Numerics;

namespace ArctisAurora.Core.UISystem.Controls.Interactable
{
    // The draggable thumb of a ScrollableControl's vertical scrollbar. Created and positioned by the
    // viewport, never authored in XML.
    public class ScrollThumbControl : ButtonControl
    {
        private readonly ScrollableControl viewport;
        private float grab;
        private float grabOffset;

        public ScrollThumbControl(ScrollableControl viewport)
        {
            this.viewport = viewport;
            controlColorHex = "#3A3A3A";
            hoverColorHex = "#4E4E4E";
            pressColorHex = "#5E5E5E";
        }

        public override void ResolveOnClick(Vector2 oldPos, Vector2 delta)
        {
            grab = (oldPos + delta).Y;
            grabOffset = viewport.GetScrollOffset().Y;
            StartDrag();
            base.ResolveOnClick(oldPos, delta);
        }

        // Pointer travel down the track maps onto the scroll range by the ratio between the two.
        public override void ResolveDrag(Vector2 lastPos, Vector2 delta)
        {
            float travel = viewport.ThumbTravel;
            if (travel > 0f)
            {
                float moved = (lastPos + delta).Y - grab;
                viewport.SetScrollOffset(new Vector2(0f,
                    grabOffset + moved * viewport.MaxScrollOffset.Y / travel));
            }
            base.ResolveDrag(lastPos, delta);
        }
    }
}
