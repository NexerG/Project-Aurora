using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Security;
using System.Xml;
using System.Xml.Linq;

namespace Thorium.Editor
{
    // A vault's id and the vaults it references. <vault>/.aurora/Vault.manifest.axml:
    // <Vault Version Id><Depends Id Alias/></Vault>.
    public static class VaultManifest
    {
        private static readonly LogChannel Log = LogChannel.For("Vault");

        private const string fileName = "Vault.manifest.axml";

        public static string PathOf(string vault) => Path.Combine(vault, VaultLock.folderName, fileName);

        // The vault's id, written on first ask; empty when the manifest can't be read or written.
        public static string IdOf(string vault)
        {
            if (!Directory.Exists(vault)) return "";
            XElement? file = Load(vault);
            if (file != null) return (string?)file.Attribute("Id") ?? "";
            if (File.Exists(PathOf(vault))) return "";

            string id = VaultLock.HasLock(vault) ? VaultLock.IdOf(vault) : "";
            if (id.Length == 0) id = Guid.NewGuid().ToString("N");
            return Save(vault, new XElement("Vault", new XAttribute("Version", 1), new XAttribute("Id", id))) ? id : "";
        }

        public static IEnumerable<(string id, string alias)> Depends(string vault) =>
            Load(vault)?.Elements("Depends").Select(d => ((string?)d.Attribute("Id") ?? "", (string?)d.Attribute("Alias") ?? "")).ToList()
            ?? new List<(string, string)>();

        // Adds a referenced vault; false when its id or alias is already listed.
        public static bool AddDependency(string vault, string id, string alias)
        {
            if (IdOf(vault).Length == 0) return false;
            XElement file = Load(vault)!;
            if (file.Elements("Depends").Any(d => (string?)d.Attribute("Id") == id
                || string.Equals((string?)d.Attribute("Alias"), alias, StringComparison.OrdinalIgnoreCase)))
                return false;

            file.Add(new XElement("Depends", new XAttribute("Id", id), new XAttribute("Alias", alias)));
            return Save(vault, file);
        }

        public static string? AliasTarget(string vault, string alias) =>
            Depends(vault).FirstOrDefault(d => string.Equals(d.alias, alias, StringComparison.OrdinalIgnoreCase)).id is { Length: > 0 } id ? id : null;

        private static XElement? Load(string vault)
        {
            string path = PathOf(vault);
            if (!File.Exists(path)) return null;
            try { return XElement.Load(path); }
            catch (XmlException e)
            {
                Log.Warn($"Vault manifest {path} can't be read and is left as it is: {e.Message}");
                return null;
            }
        }

        private static bool Save(string vault, XElement file)
        {
            string path = PathOf(vault);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                file.Save(path);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Vault manifest {path} can't be written: {e.Message}");
                return false;
            }
        }
    }
}
