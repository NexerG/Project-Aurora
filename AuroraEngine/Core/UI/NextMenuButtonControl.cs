using ArctisAurora.Core.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // A menu bar entry: a left press drops the menu it names under it, and the active control stays
    // where it was so the entries act on it.
    [A_XSDType("NextMenuButton", "UI")]
    public class NextMenuButtonControl : NextButtonControl
    {
        public override bool takesActiveControl => false;

        public override bool OnPointerPress(PointerEvent e)
        {
            base.OnPointerPress(e);
            if (e.button != PointerEvent.leftButton || contextMenu == null) return true;

            ContextMenu? menu = NextContextMenus.Get(contextMenu);
            if (menu != null)
                NextContextMenus.Open(menu.entries, this,
                    new Vector2D<float>(arrangedRect.x, arrangedRect.y + arrangedRect.height));
            return true;
        }
    }
}
