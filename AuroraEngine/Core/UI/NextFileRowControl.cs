using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // One row of a file browser, carrying the entry it was built for.
    [A_XSDType("NextFileRow", "UI")]
    public class NextFileRowControl : NextButtonControl
    {
        // Public, because a context-menu action is zero-argument and reaches the entry only through
        // the row NextContextMenus.target sits in.
        public FileObject file = null!;
        public NextFileBrowserControl browser = null!;
        internal NextEditableLabelControl label = null!;
    }
}
