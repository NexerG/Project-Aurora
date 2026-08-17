using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Interactable;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork.Registry;
using AuroraPeriodic;
// WinForms is enabled in this project; alias the engine type to avoid the
// System.Windows.Forms.ScrollableControl clash.
using ScrollableControl = ArctisAurora.Core.UISystem.Controls.Containers.ScrollableControl;

namespace Periodic.Editor.CustomControls
{
    // Lists the vault's notes and opens the clicked one in the document editor.
    [A_XSDType("VaultBrowser", "UI")]
    public class VaultBrowserControl : ScrollableControl
    {
        private const int rowHeight = 22;
        private const float indentPerDepth = 12f;

        private readonly StackPanelControl rows = new StackPanelControl();
        private string firstNote;

        public VaultBrowserControl()
        {
            scrollDirection = ScrollDirection.Vertical;
            // Thickness has no TypeConverter, so padding cannot come from the XML attribute.
            padding = new Thickness(8);

            // The viewport paints the sidebar; the panel inside it must not, or the default mask
            // covers the whole column.
            rows.maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            rows.orientation = StackPanelControl.Orientation.Vertical;
            AddChild(rows);
            Rebuild();
        }

        // Called from app startup once the whole tree exists — the rows are built while the XML is
        // still parsing, before the editor is, and OnStart cannot do it because Engine.Interpolate
        // drains its queue with a foreach and building a document creates entities.
        public static void OpenFirstNote()
        {
            VaultBrowserControl browser = Find<VaultBrowserControl>(EntityRegistry.uiTree);
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
                label.margin = new Thickness(0, 0, 0, depth * indentPerDepth);
                label.preferredHeight = rowHeight;
                return label;
            }

            ButtonControl row = new ButtonControl
            {
                preferredHeight = rowHeight,
                horizontalAlignment = HorizontalAlignment.Stretch,
                margin = new Thickness(0, 0, 0, depth * indentPerDepth)
            };
            row.AddChild(label);
            row.RegisterOnClick(() => Open(notePath));
            return row;
        }

        // Commits what is open before leaving it — switching is the only way out of a note and
        // there is no undo to recover a silent discard with.
        private static void Open(string notePath)
        {
            DocumentEditorControl editor = Find<DocumentEditorControl>(EntityRegistry.uiTree);
            if (editor == null) return;

            editor.Save();
            editor.LoadPath(notePath);
        }

        // No control naming exists, so siblings find each other by walking the tree.
        private static T Find<T>(VulkanControl control) where T : VulkanControl
        {
            if (control is T match) return match;

            foreach (Entity child in control.children)
                if (child is VulkanControl candidate && Find<T>(candidate) is T found)
                    return found;

            return null;
        }

        private static string VaultRoot()
        {
            string path = SettingsRegistry.Get<PeriodicSettings>().vault.path;
            return Path.IsPathRooted(path) ? path : VirtualFileSystem.ResolveDir(path);
        }
    }
}
