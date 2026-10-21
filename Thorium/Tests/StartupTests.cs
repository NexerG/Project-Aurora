using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

namespace Thorium.Tests
{
    internal static class StartupTests
    {
        private const string stickyDocument = "sticky-window";

        [A_XSDActionDependency("Startup.FreeStickyKeepsScope", "Test")]
        private static IEnumerator<int> FreeStickyKeepsScope(TestContext t)
        {
            string path = TempNote();
            string before = SessionLayout.scope;
            SessionLayout.scope = "startup-a";
            t.Show(new StackPanelControl());

            RenderWindow? free = TabViewControl.OpenSticky(SessionLayout.tabFactory(path, false), stickyDocument, 100, 100, Startup.freeScope);
            yield return 3;
            t.Check(free != null && free.sessionScope == Startup.freeScope, "a free sticky is tagged with the stickies scope");

            SessionLayout.ChangeScope("startup-b");
            yield return 3;
            t.Check(free != null && !free.closeRequested, "changing vault leaves the free sticky open");

            SessionLayout.Capture();
            SessionScope? recorded = SettingsRegistry.Get<SessionLayout>().scopes.FirstOrDefault(s => s.key == Startup.freeScope);
            t.Check(recorded?.windows.Count == 1 && recorded.windows[0].document == stickyDocument,
                "the free sticky is recorded under the stickies scope");
            SessionScope? vault = SettingsRegistry.Get<SessionLayout>().scopes.FirstOrDefault(s => s.key == "startup-b");
            t.Check(vault != null && vault.windows.All(w => w.document != stickyDocument), "and not under the vault's");

            if (free != null) Engine.CloseWindow(free);
            SessionLayout.scope = before;
            yield return 3;
            File.Delete(path);
        }

        [A_XSDActionDependency("Startup.TrayHidesVault", "Test")]
        private static IEnumerator<int> TrayHidesVault(TestContext t)
        {
            string path = TempNote();
            string other = TempNote();
            t.Show(new StackPanelControl());
            RenderWindow? free = TabViewControl.OpenSticky(SessionLayout.tabFactory(path, false), stickyDocument, 100, 100, Startup.freeScope);
            RenderWindow? vault = TabViewControl.OpenSticky(SessionLayout.tabFactory(other, false), stickyDocument, 420, 100, null);
            yield return 3;

            bool canTray = Background.canTray;
            Startup.tray = true;
            Startup.Place(false);
            Startup.tray = false;
            yield return 2;

            t.Check(Engine.primary.hidden == canTray, "--tray hides the main window when there is a tray");
            t.Check(vault?.hidden == canTray, "and the vault's sticky");
            t.Check(free?.hidden == false, "but shows the free sticky");

            Background.Show();
            yield return 2;
            t.Check(!Engine.primary.hidden && vault?.hidden == false, "opening from the tray shows every hidden window");

            if (free != null) Engine.CloseWindow(free);
            if (vault != null) Engine.CloseWindow(vault);
            yield return 3;
            File.Delete(path);
            File.Delete(other);
        }

        [A_XSDActionDependency("Startup.LoginWithoutStickies", "Test")]
        private static IEnumerator<int> LoginWithoutStickies(TestContext t)
        {
            string path = TempNote();
            t.Show(new StackPanelControl());
            RenderWindow? free = TabViewControl.OpenSticky(SessionLayout.tabFactory(path, false), stickyDocument, 100, 100, Startup.freeScope);
            yield return 3;

            StartupSetting settings = SettingsRegistry.Get<ThoriumSettings>().startup;
            settings.stickies = false;
            Startup.login = true;
            Startup.Place(false);
            Startup.login = false;
            settings.stickies = true;
            yield return 2;

            t.Check(!Engine.primary.hidden, "a login without InTray keeps the main window");
            t.Check(free?.hidden == Background.canTray, "and hides the free stickies when Stickies is off");

            Background.Show();
            if (free != null) Engine.CloseWindow(free);
            yield return 3;
            File.Delete(path);
        }

        [A_XSDActionDependency("Startup.ToggleSwitch", "Test")]
        private static IEnumerator<int> ToggleSwitch(TestContext t)
        {
            bool? picked = null;
            ToggleSwitchControl toggle = new ToggleSwitchControl { leftText = "Last vault", rightText = "Vault browser" };
            toggle.onChanged = right => picked = right;
            StackPanelControl host = new StackPanelControl { padding = new Thickness(12f) };
            host.AddChild(toggle);
            t.Show(host);
            yield return 2;
            yield return t.Golden("Left", toggle);

            yield return t.Click(toggle);
            yield return 20;
            t.Check(toggle.right && picked == true, "a click picks the right option and reports it");
            yield return t.Golden("Right", toggle);

            t.Show(new StackPanelControl());
        }

        private static string TempNote()
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-startup-{Guid.NewGuid():N}.axml"));
            File.WriteAllText(path, "<Document><Block><Run Text=\"sticky text\"/></Block></Document>\n");
            return path;
        }
    }
}
