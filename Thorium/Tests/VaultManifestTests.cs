using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Security;
using ArctisAurora.Core.Testing;
using Thorium.Editor;

namespace Thorium.Tests
{
    internal static class VaultManifestTests
    {
        [A_XSDActionDependency("VaultManifest.Identity", "Test")]
        private static IEnumerator<int> Identity(TestContext t)
        {
            string vault = TempVault();
            string id = VaultManifest.IdOf(vault);
            t.Check(id.Length == 32 && File.Exists(VaultManifest.PathOf(vault)), "the first ask writes a manifest with a new id");
            t.Check(VaultManifest.IdOf(vault) == id, "and every later ask reads the same id back");

            string missing = Path.Combine(vault, "missing");
            t.Check(VaultManifest.IdOf(missing) == "" && !Directory.Exists(missing), "a folder that isn't there gets no id and isn't created");

            Directory.Delete(vault, true);
            yield break;
        }

        [A_XSDActionDependency("VaultManifest.AdoptsLockId", "Test")]
        private static IEnumerator<int> AdoptsLockId(TestContext t)
        {
            string vault = TempVault();
            Directory.CreateDirectory(Path.GetDirectoryName(VaultLock.LockPath(vault))!);
            File.WriteAllText(VaultLock.LockPath(vault), "<VaultLock Version=\"1\" Id=\"0123456789abcdef0123456789abcdef\" State=\"Locked\"/>\n");
            t.Check(VaultManifest.IdOf(vault) == "0123456789abcdef0123456789abcdef", "a locked vault keeps its lock id");

            Directory.Delete(vault, true);
            yield break;
        }

        [A_XSDActionDependency("VaultManifest.MalformedUntouched", "Test")]
        private static IEnumerator<int> MalformedUntouched(TestContext t)
        {
            string vault = TempVault();
            Directory.CreateDirectory(Path.GetDirectoryName(VaultManifest.PathOf(vault))!);
            File.WriteAllText(VaultManifest.PathOf(vault), "<Vault Id=");
            t.Check(VaultManifest.IdOf(vault) == "", "a manifest that can't be read gives no id");
            t.Check(!VaultManifest.AddDependency(vault, "abc", "Other"), "and takes no dependency");
            t.Check(File.ReadAllText(VaultManifest.PathOf(vault)) == "<Vault Id=", "and is left as it was");

            Directory.Delete(vault, true);
            yield break;
        }

        [A_XSDActionDependency("VaultManifest.Depends", "Test")]
        private static IEnumerator<int> Depends(TestContext t)
        {
            string vault = TempVault();
            t.Check(VaultManifest.AddDependency(vault, "abc", "Docs"), "a referenced vault is added");
            t.Check(!VaultManifest.AddDependency(vault, "def", "docs"), "an alias already in use is refused");
            t.Check(!VaultManifest.AddDependency(vault, "abc", "Other"), "and so is an id already listed");
            t.Check(VaultManifest.AliasTarget(vault, "DOCS") == "abc" && VaultManifest.AliasTarget(vault, "Other") == null,
                "an alias resolves to its id, ignoring case");
            t.Check(VaultManifest.Depends(vault).Count() == 1, "the manifest lists one dependency");

            Directory.Delete(vault, true);
            yield break;
        }

        [A_XSDActionDependency("VaultManifest.KnownId", "Test")]
        private static IEnumerator<int> KnownId(TestContext t)
        {
            string vault = TempVault();
            KnownVaults known = SettingsRegistry.Get<KnownVaults>();
            known.vaults.Add(new KnownVault { path = vault });
            known.Remember(vault);

            string id = VaultManifest.IdOf(vault);
            t.Check(known.vaults.Count(v => KnownVaults.SamePath(v.path, vault)) == 1, "remembering a known vault adds no second entry");
            t.Check(known.PathOf(id) is string path && KnownVaults.SamePath(path, vault), "an entry without an id gets the manifest's, and resolves back");
            t.Check(known.PathOf("") == null, "an empty id resolves to nothing");

            known.vaults.RemoveAll(v => KnownVaults.SamePath(v.path, vault));
            Directory.Delete(vault, true);
            yield break;
        }

        private static string TempVault()
        {
            string vault = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"aurora-manifest-vault-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(vault);
            return vault;
        }
    }
}
