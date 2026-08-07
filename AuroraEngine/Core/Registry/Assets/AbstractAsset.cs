namespace ArctisAurora.Core.Registry.Assets
{
    public abstract class AbstractAsset
    {
        // Source is a VFS-relative logical path from the asset manifest.
        public abstract void Load(string name, string source);
    }
}
