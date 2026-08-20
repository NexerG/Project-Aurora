using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using AuroraPeriodic;

namespace Periodic.Editor.CustomControls
{
    // Lists the vault as an openable tree and opens the clicked note in the document editor.
    [A_XSDType("VaultBrowser", "UI")]
    public class VaultBrowserControl : FileTreeControl
    {
        // control names in UI.xml
        private const string browserName = "Browser";
        private const string tabsName = "Tabs";

        public VaultBrowserControl()
        {
            Rebuild();
        }

        protected override string RootPath
        {
            get
            {
                string path = SettingsRegistry.Get<PeriodicSettings>().vault.path;
                return Path.IsPathRooted(path) ? path : VirtualFileSystem.ResolveDir(path);
            }
        }

        protected override bool Accepts(FileObject file) =>
            file.path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

        protected override string DisplayName(FileObject file) =>
            file.type == FileObject.FileType.Directory
                ? file.name
                : Path.GetFileNameWithoutExtension(file.path);

        protected override void Activate(FileObject file) => Open(file.path);

        // Called from app startup once the whole tree exists — the rows are built while the XML is
        // still parsing, before the editor is, and OnStart cannot do it because Engine.Interpolate
        // drains its queue with a foreach and building a document creates entities.
        public static void OpenFirstNote()
        {
            VaultBrowserControl browser = Engine.primary.ui.uiRoot.FindByName(browserName) as VaultBrowserControl;
            string note = browser?.FirstNote(browser.root);
            if (note != null) Open(note);
        }

        // Walks the model rather than the rows — a collapsed folder contributes no row but its
        // notes still count for tree order.
        private string FirstNote(FileObject folder)
        {
            if (folder == null) return null;

            foreach (FileObject child in folder.Children)
            {
                if (child.type == FileObject.FileType.Directory)
                {
                    string found = FirstNote(child);
                    if (found != null) return found;
                    continue;
                }

                if (Accepts(child)) return child.path;
            }
            return null;
        }

        // The split pane last clicked in, so a note opens where the work is. Only this window counts —
        // the browser has no business opening notes in one that was torn off.
        private static TabViewControl FocusedTabs()
        {
            VulkanControl control = UICollisionHandling.activeControl;
            while (control != null && control is not TabViewControl)
                control = control.parent as VulkanControl;

            return control is TabViewControl view && RenderWindow.Of(view) == Engine.primary ? view : null;
        }

        // Focuses the note wherever it is already open, and only opens a tab when it is not.
        private static void Open(string notePath)
        {
            TabItemControl already = TabViewControl.FindOpenDocument(notePath, out TabViewControl owner);
            if (already != null)
            {
                owner.SetActive(already);
                RenderWindow.Of(owner)?.Focus();
                return;
            }

            TabViewControl tabs = FocusedTabs() ?? Engine.primary.ui.uiRoot.FindByName(tabsName) as TabViewControl;
            if (tabs == null) return;

            // Loaded before the tab is built, so the caption can come from the note's own name.
            DocumentEditorControl editor = new DocumentEditorControl();
            editor.LoadPath(notePath);

            TabItemControl tab = new TabItemControl
            {
                name = notePath,
                header = editor.session?.document?.name ?? Path.GetFileNameWithoutExtension(notePath)
            };
            tab.AddChild(editor);
            tabs.AddChild(tab);
            tabs.SetActive(tab);
        }
    }
}
