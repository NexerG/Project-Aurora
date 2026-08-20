using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Interactable;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using AuroraPeriodic;

namespace Periodic.Editor.CustomControls
{
    // Lists the vault's notes and opens the clicked one in the document editor.
    [A_XSDType("VaultBrowser", "UI")]
    public class VaultBrowserControl : ScrollableControl
    {
        private const int rowHeight = 22;
        private const float indentPerDepth = 12f;
        private const float rowSpacing = 2f;
        private const float rowTextInset = 6f;

        // sidebar palette
        private const string rowGround = "#171717";
        private const string rowHover = "#232323";
        private const string rowPress = "#2D2D2D";
        private const string folderText = "#8A8A8A";
        private const string noteText = "#D4D4D4";

        // control names in UI.xml
        private const string browserName = "Browser";
        private const string tabsName = "Tabs";

        private readonly StackPanelControl rows = new StackPanelControl();
        private string firstNote;

        public VaultBrowserControl()
        {
            scrollDirection = ScrollDirection.Vertical;

            // The viewport paints the sidebar; the panel inside it must not, or the default mask
            // covers the whole column.
            rows.maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            rows.orientation = StackPanelControl.Orientation.Vertical;
            rows.Spacing = rowSpacing;
            AddChild(rows);
            Rebuild();
        }

        // Called from app startup once the whole tree exists — the rows are built while the XML is
        // still parsing, before the editor is, and OnStart cannot do it because Engine.Interpolate
        // drains its queue with a foreach and building a document creates entities.
        public static void OpenFirstNote()
        {
            VaultBrowserControl browser = Engine.primary.ui.uiRoot.FindByName(browserName) as VaultBrowserControl;
            if (browser?.firstNote != null) Open(browser.firstNote);
        }

        // Re-reads the vault folder and replaces every row.
        public void Rebuild()
        {
            foreach (Entity row in rows.children.ToArray())
                row.Destroy();

            firstNote = null;

            string root = VaultRoot();
            if (!Directory.Exists(root)) return;

            AddEntries(new FileObject(root), 0);
        }

        private void AddEntries(FileObject folder, int depth)
        {
            foreach (FileObject child in folder.children)
            {
                if (child.type == FileObject.FileType.Directory)
                {
                    rows.AddChild(Row(Path.GetFileName(child.path), depth, null));
                    AddEntries(child, depth + 1);
                    continue;
                }

                if (!child.path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;

                firstNote ??= child.path;
                rows.AddChild(Row(Path.GetFileNameWithoutExtension(child.path), depth, child.path));
            }
        }

        // A folder is a label; a note is a button that opens it.
        private VulkanControl Row(string name, int depth, string notePath)
        {
            LabelControl label = new LabelControl { text = name, fontSize = 14 };

            if (notePath == null)
            {
                label.controlColorHex = folderText;
                label.margin = new Thickness(0, 0, 0, depth * indentPerDepth + rowTextInset);
                label.preferredHeight = rowHeight;
                return label;
            }

            label.controlColorHex = noteText;
            label.horizontalPosition = 0f;

            ButtonControl row = new ButtonControl
            {
                preferredHeight = rowHeight,
                horizontalAlignment = HorizontalAlignment.Stretch,
                margin = new Thickness(0, 0, 0, depth * indentPerDepth),
                padding = new Thickness(0, 0, 0, rowTextInset),
                controlColorHex = rowGround,
                hoverColorHex = rowHover,
                pressColorHex = rowPress
            };
            row.AddChild(label);
            row.RegisterOnRelease(() => Open(notePath));
            return row;
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

        // Focuses the note's tab, opening one if it is not already open.
        private static void Open(string notePath)
        {
            TabViewControl tabs = FocusedTabs() ?? Engine.primary.ui.uiRoot.FindByName(tabsName) as TabViewControl;
            if (tabs == null) return;

            TabItemControl open = tabs.FindTab(notePath);
            if (open != null)
            {
                tabs.SetActive(open);
                return;
            }

            DocumentEditorControl editor = new DocumentEditorControl();
            TabItemControl tab = new TabItemControl
            {
                name = notePath,
                header = Path.GetFileNameWithoutExtension(notePath)
            };
            tab.AddChild(editor);
            tabs.AddChild(tab);
            tabs.SetActive(tab);
            editor.LoadPath(notePath);
        }

        private static string VaultRoot()
        {
            string path = SettingsRegistry.Get<PeriodicSettings>().vault.path;
            return Path.IsPathRooted(path) ? path : VirtualFileSystem.ResolveDir(path);
        }
    }
}
