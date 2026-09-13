using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.Core.UISystem.Actions;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using Thorium.Editor.CustomControls;

namespace Thorium.Editor
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

        // control name in NextUI.ui.xml
        private const string browserName = "Browser";

        // layout
        private const int rowHeight = 44;
        private const int nameHeight = 18;
        private const int pathHeight = 14;

        // palette, matching the app chrome
        private const string rowHex = "#E3E1D9";
        private const string rowHoverHex = "#DCDAD3";
        private const string rowPressHex = "#EAE8E2";
        private const string nameHex = "#23221E";
        private const string currentHex = "#3A3833";
        private const string pathHex = "#918F87";

        private static NextStackPanelControl _rows = null!;

        // One screen at a time; a second ask raises the one already up and returns no root.
        public static void Open(RenderWindow source)
        {
            WindowRoot root = MenuScreen.Open(windowName, document, windowWidth, windowHeight, source);
            if (root == null) return;

            _rows = (NextStackPanelControl)root.FindByName("Rows");
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
        private static string Current() => KnownVaults.Resolve(SettingsRegistry.Get<ThoriumSettings>().vault.path);

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

        private static Control Row(string path, bool current)
        {
            NextStackPanelControl content = new NextStackPanelControl
            {
                orientation = NextStackPanelControl.Orientation.Vertical,
                alpha = 0f,
                hitTestable = false,
                horizontalPosition = 0f
            };

            content.AddChild(new NextLabelControl
            {
                text = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
                fontSize = 14,
                colorHex = current ? currentHex : nameHex,
                preferredHeight = nameHeight,
                horizontalPosition = 0f
            });

            content.AddChild(new NextLabelControl
            {
                text = path,
                fontSize = 11,
                colorHex = pathHex,
                preferredHeight = pathHeight,
                horizontalPosition = 0f
            });

            NextButtonControl row = new NextButtonControl
            {
                preferredHeight = rowHeight,
                horizontalAlignment = HorizontalAlignment.Stretch,
                padding = new Thickness(0, 0, 0, 10),
                colorHex = rowHex,
                hoverColorHex = rowHoverHex,
                pressColorHex = rowPressHex,
                cornerRadius = new CornerRadii(4)
            };
            row.AddChild(content);

            // Posted, so the tree this click is still bubbling through is not torn down under it.
            row.RegisterOnRelease(_ => { Engine.Post(() => Switch(path)); return true; });

            return row;
        }
        #endregion

        #region ---- switching ----
        // The vault is the setting, so switching writes it and everything downstream re-reads it.
        private static void Switch(string path)
        {
            if (!KnownVaults.SamePath(path, Current()))
            {
                SettingsRegistry.Get<ThoriumSettings>().vault.path = path;
                SettingsRegistry.Commit();

                CloseTabs();
                NextVaultBrowserControl browser = Engine.primary.uiNext.uiRoot.FindByName(browserName) as NextVaultBrowserControl;
                browser?.Rebuild();
                NextVaultBrowserControl.OpenFirstNote();
            }

            if (Engine.windows.TryGetValue(windowName, out RenderWindow window)) Engine.CloseWindow(window);
        }

        // Every note on screen belongs to the vault being left. CloseTab writes each one first.
        private static void CloseTabs()
        {
            foreach (NextTabViewControl view in NextTabViewControl.TabViews(Engine.primary.uiNext.uiRoot).ToList())
                foreach (NextTabItemControl item in view.Items.ToArray())
                    view.CloseTab(item);
        }
        #endregion
    }
}
