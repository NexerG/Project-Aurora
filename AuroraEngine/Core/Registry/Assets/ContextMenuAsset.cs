using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Registry.Assets
{
    [A_XSDType("ContextMenuAsset", "AssetRegistry")]
    public class ContextMenuAsset : AbstractAsset
    {
        public string name = string.Empty;
        public string path = string.Empty;

        public ContextMenuAsset() { }

        // Resolves only. NextContextMenus parses the document on first use.
        public override void Load(string name, string source)
        {
            this.name = name;
            path = VirtualFileSystem.ResolveFile(source);
        }
    }
}
