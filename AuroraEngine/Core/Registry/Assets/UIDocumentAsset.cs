using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Registry.Assets
{
    [A_XSDType("UIDocumentAsset", "AssetRegistry")]
    public class UIDocumentAsset : AbstractAsset
    {
        public string name = string.Empty;
        public string path = string.Empty;

        public UIDocumentAsset() { }

        // Resolves only. One document builds a tree per window, so parsing stays at the use site.
        public override void Load(string name, string source)
        {
            this.name = name;
            path = VirtualFileSystem.ResolveFile(source);
        }
    }
}
