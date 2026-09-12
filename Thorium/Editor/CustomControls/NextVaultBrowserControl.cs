using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using System.Xml.Linq;

namespace Thorium.Editor.CustomControls
{
    // Lists the vault as an openable tree and opens the clicked note in the document editor.
    [A_XSDType("NextVaultBrowser", "UI")]
    public class NextVaultBrowserControl : NextFileTreeControl
    {
        // control names in NextUI.ui.xml
        private const string browserName = "Browser";
        private const string tabsName = "Tabs";

        // context declared in Contexts/Thorium.contexts.xml
        private const string tabsContext = "NextActiveTabViewer";

        // matching the NextDocumentEditor attributes in NextWorkspace.ui.xml
        private const string caretHex = "#23221E";
        private const string selectionHex = "#D7D5CD";
        private const string thumbHex = "#D7D5CD";
        private const string thumbHoverHex = "#C9C6BC";
        private const string thumbPressHex = "#BAB7AC";

        public NextVaultBrowserControl()
        {
            Rebuild();
        }

        protected override string RootPath =>
            KnownVaults.Resolve(SettingsRegistry.Get<ThoriumSettings>().vault.path);

        protected override bool Accepts(FileObject file) =>
            file.path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

        protected override string DisplayName(FileObject file) =>
            file.type == FileObject.FileType.Directory
                ? file.name
                : Path.GetFileNameWithoutExtension(file.path);

        protected override void Activate(FileObject file) => Open(file.path);

        protected override void Rename(FileObject file, string newName) => RenameNote(file.path, newName);

        // The name lives in three places: the file, the Name inside it, and every editor already
        // holding the note open. Static, because a tab renames through here too and has no row.
        private static void RenameNote(string path, string newName)
        {
            string name = newName?.Trim();
            if (string.IsNullOrEmpty(name) || name == Path.GetFileNameWithoutExtension(path)) return;

            string target = FreePath(Path.GetDirectoryName(path)!, name);
            File.Move(path, target);
            WriteName(target, name);

            foreach ((NextTabItemControl item, NextTabViewControl view) in NextTabViewControl.FindOpenDocuments(path))
            {
                NextDocumentEditorControl editor = NextTabViewControl.EditorOf(item);
                editor.session.Repath(target);
                editor.session.document.name = name;
                item.name = target;
                view.Retitle(item, name);
            }

            (Engine.primary.uiNext.uiRoot.FindByName(browserName) as NextVaultBrowserControl)?.Rebuild();
        }

        // Focuses the note wherever it is already open, and only opens a tab when it is not.
        private static void Open(string notePath)
        {
            NextTabItemControl already = NextTabViewControl.FindOpenDocument(notePath, out NextTabViewControl owner);
            if (already != null)
            {
                owner.SetActive(already);
                UIEngine.WindowOf(owner)?.Focus();
                return;
            }

            NextTabViewControl tabs = FocusedTabs()
                ?? Engine.primary.uiNext.uiRoot.FindByName(tabsName) as NextTabViewControl;
            if (tabs == null) return;

            NextTabItemControl tab = BuildTab(notePath);
            tabs.AddChild(tab);
            tabs.SetActive(tab);
        }

        // The split pane last clicked in, so a note opens where the work is. Only this window counts —
        // the browser has no business opening notes in one that was torn off.
        private static NextTabViewControl FocusedTabs()
        {
            NextTabViewControl view = Context.Get<NextTabViewControl>(tabsContext);
            return view != null && UIEngine.WindowOf(view) == Engine.primary ? view : null;
        }

        // One tab holding one note, for whichever view is going to take it. Loaded before the tab is
        // built, so the caption can come from the note's own name.
        internal static NextTabItemControl BuildTab(string notePath)
        {
            NextDocumentEditorControl editor = new NextDocumentEditorControl
            {
                caretColorHex = caretHex,
                selectionColorHex = selectionHex,
                thumbColorHex = thumbHex,
                thumbHoverColorHex = thumbHoverHex,
                thumbPressColorHex = thumbPressHex
            };
            editor.LoadPath(notePath);

            NextTabItemControl tab = new NextTabItemControl
            {
                name = notePath,
                header = editor.session?.document?.name ?? Path.GetFileNameWithoutExtension(notePath),
                onRename = name => RenameNote(editor.session.path, name)
            };
            tab.AddChild(editor);
            return tab;
        }

        // "Name", then "Name 2", "Name 3" — a name already taken is never written over.
        private static string FreePath(string folder, string baseName)
        {
            string path = Path.Combine(folder, baseName + ".xml");
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(folder, $"{baseName} {i}.xml");

            return path;
        }

        private static void WriteName(string path, string name)
        {
            XDocument xml = XDocument.Load(path);
            xml.Root!.SetAttributeValue("Name", name);
            xml.Save(path);
        }
    }
}
