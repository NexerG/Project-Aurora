using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using AuroraPeriodic;
using Microsoft.VisualBasic.FileIO;
using System.Xml.Linq;

namespace Periodic.Editor.CustomControls
{
    // Lists the vault as an openable tree and opens the clicked note in the document editor.
    [A_XSDType("VaultBrowser", "UI")]
    public class VaultBrowserControl : FileTreeControl
    {
        // control names in UI.xml
        private const string browserName = "Browser";
        private const string tabsName = "Tabs";

        // context declared in Contexts/Periodic.xml
        private const string tabsContext = "ActiveTabViewer";

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

        // The list's own ground, which stands for the vault root.
        public override void BuildContextMenu(ContextMenuBuilder menu) => menu.Add("New note", () => NewNote(RootPath));

        protected override void BuildRowMenu(FileObject file, ContextMenuBuilder menu)
        {
            if (file.type == FileObject.FileType.Directory)
            {
                menu.Add("New note", () => NewNote(file.path));
                return;
            }

            menu.Add("New note", () => NewNote(file.parent.path));
            menu.Add("Rename note", () => BeginRename(file));
            menu.Add("Duplicate note", () => DuplicateNote(file));
            menu.Add("Delete note", () => DeleteNote(file));
        }

        #region ---- note operations ----
        private void NewNote(string folder) =>
            NoteNameWindow.Ask(RenderWindow.Of(this), "Untitled", name => CreateNote(folder, name), null, null);

        // A note needs a block holding a run before it can be typed into — the editor places its
        // caret on a run and builds neither.
        private void CreateNote(string folder, string name)
        {
            string path = FreePath(folder, name);

            RichTextDocument document = new RichTextDocument { name = Path.GetFileNameWithoutExtension(path) };
            ContentBlock block = new ContentBlock();
            block.AddChild(new TextRun { text = string.Empty });
            document.blocks.Add(block);
            document.Save(path);
            block.Destroy();

            Expand(folder);
            Rebuild();
            Open(path);
        }

        private void DuplicateNote(FileObject file)
        {
            string path = FreePath(file.parent.path, Path.GetFileNameWithoutExtension(file.path) + " copy");

            File.Copy(file.path, path);
            WriteName(path, Path.GetFileNameWithoutExtension(path));

            Rebuild();
            Open(path);
        }

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

            foreach ((TabItemControl item, TabViewControl view) in TabViewControl.FindOpenDocuments(path))
            {
                DocumentEditorControl editor = TabViewControl.EditorOf(item);
                editor.session.Repath(target);
                editor.session.document.name = name;
                item.name = target;
                view.Retitle(item, name);
            }

            (Engine.primary.ui.uiRoot.FindByName(browserName) as VaultBrowserControl)?.Rebuild();
        }

        private void DeleteNote(FileObject file) =>
            ConfirmWindow.Ask(RenderWindow.Of(this), $"Delete \"{DisplayName(file)}\"?", () => Delete(file.path), null);

        // The tab goes first and goes unwritten, or closing it would put the note back on disk.
        private void Delete(string path)
        {
            TabItemControl open = TabViewControl.FindOpenDocument(path, out TabViewControl owner);
            if (open != null) owner.DiscardTab(open);

            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            Rebuild();
        }

        // "Name", then "Name 2", "Name 3" — a name already taken is never written over.
        private static string FreePath(string folder, string baseName)
        {
            string path = Path.Combine(folder, baseName + ".xml");
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(folder, $"{baseName} {i}.xml");

            return path;
        }

        // Copying a note copies the name written inside it, which is what a tab captions itself with.
        private static void WriteName(string path, string name)
        {
            XDocument xml = XDocument.Load(path);
            xml.Root!.SetAttributeValue("Name", name);
            xml.Save(path);
        }
        #endregion

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
            TabViewControl view = Context.Get<TabViewControl>(tabsContext);
            return view != null && RenderWindow.Of(view) == Engine.primary ? view : null;
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
                header = editor.session?.document?.name ?? Path.GetFileNameWithoutExtension(notePath),
                onRename = name => RenameNote(editor.session.path, name)
            };
            tab.AddChild(editor);
            tabs.AddChild(tab);
            tabs.SetActive(tab);
        }
    }
}
