using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // The note document model — the source of truth, and the on-disk format (serialized as engine
    // XML, not markdown). The control tree is a view synced from this; editing mutates a working
    // copy of it (see DocumentEditSession). XML load/save is added in P1.
    [A_XSDType("Document", "UI", allowedChildren: typeof(Block))]
    public class RichTextDocument : IXMLParser<RichTextDocument>
    {
        public List<Block> blocks = new List<Block>();

        // Load from an engine-XML note file. Implements the engine's IXMLParser<T> contract (same as
        // VulkanControl / InputHandler); the string is a file path, resolved by the caller (vault).
        public static RichTextDocument ParseXML(string path) => DocumentXml.Load(path);

        public void Save(string path) => DocumentXml.Save(this, path);

        // Deep copy — used to make the isolated working copy the editor edits before a save.
        public RichTextDocument Clone()
        {
            RichTextDocument copy = new RichTextDocument();
            foreach (Block block in blocks)
                copy.blocks.Add(block.Clone());
            return copy;
        }
    }
}
