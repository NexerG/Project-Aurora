using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using Microsoft.VisualBasic.FileIO;
using System.Xml.Linq;

namespace Thorium.Editor.CustomControls
{
    // Lists the vault as an openable tree and opens the clicked note in the document editor.
    [A_XSDType("VaultBrowser", "UI")]
    public class VaultBrowserControl : FileTreeControl
    {
        // control names in UI.ui.xml
        private const string browserName = "Browser";
        private const string tabsName = "Tabs";

        // context declared in Contexts/Thorium.contexts.xml
        private const string tabsContext = "ActiveTabViewer";

        // menu documents registered in ThoriumAssets.assets.xml
        private const string vaultMenu = "vault";
        private const string folderMenu = "vault-folder";
        private const string noteMenu = "vault-note";

        public VaultBrowserControl()
        {
            contextMenu = vaultMenu;
            Rebuild();
            Context.changed += OnContextChanged;
            TabViewControl.activeChanged += OnActiveTabChanged;
        }

        public override void OnDestroy()
        {
            Context.changed -= OnContextChanged;
            TabViewControl.activeChanged -= OnActiveTabChanged;
            base.OnDestroy();
        }

        #region ---- current note ----
        private void OnContextChanged(string name, object? value)
        {
            if (name == tabsContext) FollowFocusedTab();
        }

        private void OnActiveTabChanged(TabViewControl view)
        {
            TabViewControl focused = FocusedTabs();
            if (focused == null ? UIEngine.WindowOf(view) == Engine.primary : ReferenceEquals(view, focused))
                SetCurrent(view.activeItem?.name);
        }

        private void FollowFocusedTab() => SetCurrent(FocusedTabs()?.activeItem?.name);
        #endregion

        protected override string RootPath =>
            KnownVaults.Resolve(SettingsRegistry.Get<ThoriumSettings>().vault.path);

        protected override bool Accepts(FileObject file) =>
            RichTextDocument.extensions.Contains(Path.GetExtension(file.path).ToLowerInvariant());

        protected override string DisplayName(FileObject file) =>
            file.type == FileObject.FileType.Directory
                ? file.name
                : Path.GetFileNameWithoutExtension(file.path);

        protected override void Activate(FileObject file) => Open(file.path);

        protected override void Rename(FileObject file, string newName) => RenameNote(file.path, newName);

        protected override string RowContextMenu(FileObject file) =>
            file.type == FileObject.FileType.Directory ? folderMenu : noteMenu;

        #region ---- note operations ----
        [A_XSDActionDependency("Notes.New", "UI", "Creates a note at the vault root and opens it")]
        public static void New()
        {
            VaultBrowserControl browser = Browser();
            browser?.NewNote(browser.RootPath);
        }

        [A_XSDActionDependency("Notes.NewHere", "UI", "Creates a note beside the entry the menu was opened on")]
        public static void NewHere()
        {
            FileRowControl row = MenuRow();
            if (row == null) { New(); return; }

            Browser()?.NewNote(row.file.type == FileObject.FileType.Directory ? row.file.path : row.file.parent.path);
        }

        [A_XSDActionDependency("Notes.Rename", "UI", "Turns the name of the note the menu was opened on into a field")]
        public static void RenameEntry()
        {
            FileRowControl row = MenuRow();
            if (row != null) Browser()?.BeginRename(row.file);
        }

        [A_XSDActionDependency("Notes.Duplicate", "UI", "Copies the note the menu was opened on and opens the copy")]
        public static void Duplicate()
        {
            FileRowControl row = MenuRow();
            if (row != null) Browser()?.DuplicateNote(row.file);
        }

        [A_XSDActionDependency("Notes.Delete", "UI", "Asks, then sends the note the menu was opened on to the recycle bin")]
        public static void Delete()
        {
            FileRowControl row = MenuRow();
            if (row != null) Browser()?.DeleteNote(row.file);
        }

        // The row the open menu was opened on, or null when it was opened on the browser's ground.
        private static FileRowControl MenuRow()
        {
            for (Control c = ContextMenus.target; c != null; c = c.parent as Control)
                if (c is FileRowControl row) return row;

            return null;
        }

        private static VaultBrowserControl Browser() =>
            Engine.primary.ui.uiRoot?.FindByName(browserName) as VaultBrowserControl;

        private void NewNote(string folder) =>
            NoteNameWindow.Ask(UIEngine.WindowOf(this), "Untitled", name => CreateNote(folder, name), null, null);

        // A note needs a block holding a run before it can be typed into — the editor places its
        // caret on a run and builds neither.
        private void CreateNote(string folder, string name)
        {
            string path = FreePath(folder, name, ".md");

            RichTextDocument document = new RichTextDocument { name = Path.GetFileNameWithoutExtension(path) };
            BlockControl block = new BlockControl();
            block.AppendRun(new Run());
            document.blocks.Add(block);
            document.Save(path);
            block.Destroy();

            Expand(folder);
            Rebuild();
            Open(path);
        }

        private void DuplicateNote(FileObject file)
        {
            string path = FreePath(file.parent.path, Path.GetFileNameWithoutExtension(file.path) + " copy", Path.GetExtension(file.path));

            File.Copy(file.path, path);
            WriteName(path, Path.GetFileNameWithoutExtension(path));

            Rebuild();
            Open(path);
        }

        private void DeleteNote(FileObject file) =>
            ConfirmWindow.Ask(UIEngine.WindowOf(this), $"Delete \"{DisplayName(file)}\"?",
                () => DeleteFile(file.path), null);

        // The tab goes first and goes unwritten, or closing it would put the note back on disk.
        private void DeleteFile(string path)
        {
            TabItemControl open = TabViewControl.FindOpenDocument(path, out TabViewControl owner);
            if (open != null) owner.FinishClose(open);

            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            Rebuild();
        }
        #endregion

        // The name lives in three places: the file, the Name inside it, and every editor already
        // holding the note open. Static, because a tab renames through here too and has no row.
        private static void RenameNote(string path, string newName)
        {
            string name = newName?.Trim();
            if (string.IsNullOrEmpty(name) || name == Path.GetFileNameWithoutExtension(path)) return;

            string target = FreePath(Path.GetDirectoryName(path)!, name, Path.GetExtension(path));
            File.Move(path, target);
            WriteName(target, name);

            foreach ((TabItemControl item, TabViewControl view) in TabViewControl.FindOpenDocuments(path))
            {
                DocumentEditorControl editor = TabViewControl.EditorOf(item);
                editor.session.Repath(target);
                editor.session.document.name = name;
                item.name = target;
                view.Retitle(item, name);
            }

            VaultBrowserControl? browser = Engine.primary.ui.uiRoot.FindByName(browserName) as VaultBrowserControl;
            browser?.Rebuild();
            browser?.FollowFocusedTab();
        }

        // Focuses the note wherever it is already open, and only opens a tab when it is not.
        private static void Open(string notePath)
        {
            TabItemControl already = TabViewControl.FindOpenDocument(notePath, out TabViewControl owner);
            if (already != null)
            {
                owner.SetActive(already);
                UIEngine.WindowOf(owner)?.Focus();
                return;
            }

            TabViewControl tabs = FocusedTabs()
                ?? Engine.primary.ui.uiRoot.FindByName(tabsName) as TabViewControl;
            if (tabs == null) return;

            TabItemControl tab = BuildTab(notePath);
            tabs.AddChild(tab);
            tabs.SetActive(tab);
        }

        // Opens the first note in tree order.
        public static void OpenFirstNote()
        {
            VaultBrowserControl browser = Browser();
            string note = browser?.FirstNote(browser.root);
            if (note != null) Open(note);
        }

        // Walks the model rather than the rows, so a collapsed folder's notes still count.
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
            TabViewControl view = Context.Get<TabViewControl>(tabsContext);
            return view != null && UIEngine.WindowOf(view) == Engine.primary ? view : null;
        }

        // One tab holding one note, for whichever view is going to take it. Loaded before the tab is
        // built, so the caption can come from the note's own name.
        internal static TabItemControl BuildTab(string notePath)
        {
            DocumentEditorControl editor = new DocumentEditorControl();
            editor.LoadPath(notePath);
            editor.onNamed = name => RenameNote(editor.session.path, name);

            TabItemControl tab = new TabItemControl
            {
                name = notePath,
                header = editor.session?.document?.name ?? Path.GetFileNameWithoutExtension(notePath),
                onRename = name => RenameNote(editor.session.path, name)
            };
            tab.AddChild(editor);
            return tab;
        }

        // "Name", then "Name 2", "Name 3" — a name already taken is never written over.
        private static string FreePath(string folder, string baseName, string extension)
        {
            string path = Path.Combine(folder, baseName + extension);
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(folder, $"{baseName} {i}{extension}");

            return path;
        }

        // Only XML carries a name of its own; other formats are named by their file.
        private static void WriteName(string path, string name)
        {
            if (!Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase)) return;

            XDocument xml = XDocument.Load(path);
            xml.Root!.SetAttributeValue("Name", name);
            xml.Save(path);
        }
    }
}
