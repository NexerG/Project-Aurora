using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Thorium.Editor.CustomControls;

namespace Thorium.Tests
{
    internal static class VaultScopeTests
    {
        private const string stickyDocument = "sticky-window";
        private const string key = "vault-scope-test";

        [A_XSDActionDependency("VaultScope.CloseKeepsStickies", "Test")]
        private static IEnumerator<int> CloseKeepsStickies(TestContext t)
        {
            string editorNote = TempNote();
            string stickyNote = TempNote();
            t.Show(new StackPanelControl());

            RenderWindow? editor = TabViewControl.OpenSticky(SessionLayout.tabFactory(editorNote, false), stickyDocument, 100, 100, key);
            RenderWindow? sticky = TabViewControl.OpenSticky(SessionLayout.tabFactory(stickyNote, false), stickyDocument, 420, 100, key);
            if (editor != null) editor.anchor = true;
            yield return 3;

            SessionLayout.Close(key, window => window == sticky);
            yield return 3;
            t.Check(editor?.closeRequested != false && sticky?.closeRequested == false, "closing a scope closes its windows but the kept ones");

            SessionLayout.Capture();
            SessionScope? recorded = SettingsRegistry.Get<SessionLayout>().scopes.FirstOrDefault(s => s.key == key);
            t.Check(recorded?.windows.Count == 2 && recorded.windows.Count(w => w.primary) == 1,
                "a later capture keeps the closed window's record beside the kept one");

            t.Check(SessionLayout.Open(key), "reopening the scope finds what it closed");
            yield return 3;
            List<RenderWindow> open = Engine.windows.Values.Where(w => !w.closeRequested && w.sessionScope == key).ToList();
            t.Check(open.Count == 2 && open.Count(w => w.anchor) == 1, "only the closed window comes back, anchored, and the kept one is not doubled");

            foreach (RenderWindow window in open) Engine.CloseWindow(window);
            SettingsRegistry.Get<SessionLayout>().scopes.RemoveAll(s => s.key == key);
            yield return 3;
            File.Delete(editorNote);
            File.Delete(stickyNote);
        }

        [A_XSDActionDependency("VaultScope.SheetInHomeVault", "Test")]
        private static IEnumerator<int> SheetInHomeVault(TestContext t)
        {
            string a = TempVault();
            string b = TempVault();
            KnownVaults known = SettingsRegistry.Get<KnownVaults>();
            known.Remember(a);
            known.Remember(b);

            string? fromA = VaultBrowserControl.FindSheet(Path.Combine(a, "note.md"), "Budget");
            string? fromB = VaultBrowserControl.FindSheet(Path.Combine(b, "note.md"), "Budget");
            t.Check(fromA != null && fromA.StartsWith(a, StringComparison.OrdinalIgnoreCase), "a reference resolves in the vault of the file holding it");
            t.Check(fromB != null && fromB.StartsWith(b, StringComparison.OrdinalIgnoreCase), "and the same name in another vault resolves there");

            known.vaults.RemoveAll(v => KnownVaults.SamePath(v.path, a) || KnownVaults.SamePath(v.path, b));
            Directory.Delete(a, true);
            Directory.Delete(b, true);
            yield break;
        }

        private static string TempNote()
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-scope-{Guid.NewGuid():N}.axml"));
            File.WriteAllText(path, "<Document><Block><Run Text=\"scope text\"/></Block></Document>\n");
            return path;
        }

        private static string TempVault()
        {
            string vault = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-scope-vault-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(vault);
            File.WriteAllText(Path.Combine(vault, "Budget" + SheetDocument.extension), "<Sheet/>\n");
            return vault;
        }
    }
}
