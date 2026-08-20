using ArctisAurora.Core.Filing;
using ArctisAurora.Core.UISystem.Controls.Interactable;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    // One row of a file browser. It carries the entry it was built for, because the browser sees only
    // that a right click arrived and not which of its rows it landed on.
    public class FileRowControl : ButtonControl
    {
        internal FileObject file;
        internal FileBrowserControl browser;

        public override void BuildContextMenu(ContextMenuBuilder menu) => browser?.BuildRowMenu(file, menu);
    }
}
