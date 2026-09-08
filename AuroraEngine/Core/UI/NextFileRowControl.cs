using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // One row of a file browser, carrying the entry it was built for.
    [A_XSDType("NextFileRow", "UI")]
    public class NextFileRowControl : NextButtonControl
    {
        internal FileObject file = null!;
        internal NextFileBrowserControl browser = null!;
        internal NextEditableLabelControl label = null!;
    }
}
