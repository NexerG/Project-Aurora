using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Actions;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Interactable;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using AuroraPeriodic;
using Periodic.Editor.CustomControls;

namespace Periodic.Editor
{
    // Every vault that has been opened, as one screen. The shell is Vaults.ui.xml; only the rows are
    // built here, because the list is data. Built per open and closed by its own title bar, the same
    // as the settings screen.
    public static class VaultsWindow
    {
        private const string windowName = "vaults";
        private const string document = "vaults";
        private const uint windowWidth = 520;
        private const uint windowHeight = 400;

        // control name in UI.ui.xml
        private const string browserName = "Browser";

        // layout
        private const int rowHeight = 44;
        private const int nameHeight = 18;
        private const int pathHeight = 14;

        // palette, matching the app chrome
        private const string rowHex = "#2A2A2A";
        private const string rowHoverHex = "#343434";
        private const string rowPressHex = "#232323";
        private const string nameHex = "#EAEAEA";
        private const string currentHex = "#8AB4F8";
        private const string pathHex = "#8A8A8A";

        private static StackPanelControl _rows = null!;

        // One screen at a time; a second ask raises the one already up and returns no root.
        public static void Open(RenderWindow source)
        {
            WindowControl root = MenuScreen.Open(windowName, document, windowWidth, windowHeight, source);
            if (root == null) return;

            _rows = (StackPanelControl)root.FindByName("Rows");
            Fill();
        }

        [A_XSDActionDependency("Vaults.Open", "UI", "Opens the vault browser over the window that asked")]
        public static void Open() => Open(UIActions.Invoking());

        [A_XSDActionDependency("Vaults.Add", "UI", "Picks a folder and opens it as a vault")]
        public static void Add()
        {
            RenderWindow source = Engine.windows.GetValueOrDefault(windowName) ?? Engine.primary;
            FolderPicker.Pick(source, Current(), picked =>
            {
                if (picked == null) return;

                SettingsRegistry.Get<KnownVaults>().Remember(picked);
                Switch(picked);
            });
        }

        #region ---- rows ----
        private static string Current() => KnownVaults.Resolve(SettingsRegistry.Get<PeriodicSettings>().vault.path);

        // Pruning happens here rather than on a timer — the list is only ever looked at from this
        // screen, and a vault that was moved should not be offered once it is on screen.
        private static void Fill()
        {
            KnownVaults known = SettingsRegistry.Get<KnownVaults>();
            known.Prune();

            foreach (Entity child in _rows.children.ToArray())
                child.Destroy();

            string current = Current();
            foreach (KnownVault vault in known.vaults)
                _rows.AddChild(Row(vault.path, KnownVaults.SamePath(vault.path, current)));

            _rows.InvalidateLayout();
        }

        private static VulkanControl Row(string path, bool current)
        {
            StackPanelControl content = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Vertical,
                maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible"),
                horizontalPosition = 0f
            };
            content.BubbleAll();

            content.AddChild(new LabelControl
            {
                text = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
                fontSize = 14,
                controlColorHex = current ? currentHex : nameHex,
                preferredHeight = nameHeight,
                horizontalPosition = 0f
            });

            content.AddChild(new LabelControl
            {
                text = path,
                fontSize = 11,
                controlColorHex = pathHex,
                preferredHeight = pathHeight,
                horizontalPosition = 0f
            });

            ButtonControl row = new ButtonControl
            {
                preferredHeight = rowHeight,
                horizontalAlignment = VulkanControl.HorizontalAlignment.Stretch,
                padding = new VulkanControl.Thickness(0, 0, 0, 10),
                controlColorHex = rowHex,
                hoverColorHex = rowHoverHex,
                pressColorHex = rowPressHex,
                cornerRadius = new VulkanControl.CornerRadii(4)
            };
            row.AddChild(content);

            // Posted, so the tree this click is still bubbling through is not torn down under it.
            row.RegisterOnRelease(() => Engine.Post(() => Switch(path)));

            return row;
        }
        #endregion

        #region ---- switching ----
        // The vault is the setting, so switching writes it and everything downstream re-reads it.
        private static void Switch(string path)
        {
            if (!KnownVaults.SamePath(path, Current()))
            {
                SettingsRegistry.Get<PeriodicSettings>().vault.path = path;
                SettingsRegistry.Commit();

                CloseTabs();
                VaultBrowserControl browser = Engine.primary.ui.uiRoot.FindByName(browserName) as VaultBrowserControl;
                browser?.Rebuild();
                VaultBrowserControl.OpenFirstNote();
            }

            if (Engine.windows.TryGetValue(windowName, out RenderWindow window)) Engine.CloseWindow(window);
        }

        // Every note on screen belongs to the vault being left. CloseTab writes each one first.
        private static void CloseTabs()
        {
            List<TabViewControl> views = new List<TabViewControl>();
            Collect(Engine.primary.ui.uiRoot, views);

            foreach (TabViewControl view in views)
                foreach (TabItemControl item in view.Items.ToArray())
                    view.CloseTab(item);
        }

        // Stops at a tab view rather than descending into it — below one lies a document, which is
        // one control per glyph.
        private static void Collect(Entity node, List<TabViewControl> found)
        {
            if (node == null) return;
            if (node is TabViewControl view) { found.Add(view); return; }

            foreach (Entity child in node.children.ToArray())
                Collect(child, found);
        }
        #endregion
    }
}
