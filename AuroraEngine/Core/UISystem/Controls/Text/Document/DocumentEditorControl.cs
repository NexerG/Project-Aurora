using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;
// WinForms is enabled in this project; alias the engine control to avoid the
// System.Windows.Forms.ScrollableControl clash.
using ScrollableControl = ArctisAurora.Core.UISystem.Controls.Containers.ScrollableControl;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // The view over a RichTextDocument: a scrolling viewport over the document's own block controls.
    [A_XSDType("DocumentEditor", "UI")]
    public class DocumentEditorControl : ScrollableControl
    {
        public RichTextDocument activeDocument { get; private set; }

        private DocumentControl content;

        public DocumentEditorControl()
        {
            scrollDirection = ScrollDirection.Vertical;
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        [A_XSDElementProperty("Source", "UI", "Engine-XML note file to load into the editor.")]
        public string source
        {
            get => field;
            set
            {
                field = value;
                if (!string.IsNullOrEmpty(value))
                    LoadPath(value);
            }
        }

        public void LoadPath(string nameOrPath)
        {
            string path = Path.IsPathRooted(nameOrPath) ? nameOrPath : Paths.Doc(nameOrPath);
            LoadDocument(RichTextDocument.ParseXML(path));
        }

        public void LoadDocument(RichTextDocument document)
        {
            activeDocument = document;

            foreach (Entity child in children.ToArray())
                child.Destroy();
            children.Clear();

            content = new DocumentControl { blockSpacing = document.layout.blockSpacing };
            AddChild(content);

            foreach (Block block in document.blocks)
            {
                block.ApplyLayout(document.layout);
                content.AddChild(block);
            }
        }

        // Places the caret and marks the hit run editable.
        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            TextControl run = RunUnder(UICollisionHandling.hovering);
            if (run != null && content != null)
            {
                // InputHandler.mousePos, not oldPos, which lags the click by a frame
                Vector2D<float> mouse = InputHandler.mousePos;
                LayoutRect inner = run.arrangedRect.Shrink(run.padding);

                run.cursorPosition = run.OffsetAt(mouse.X - inner.x, mouse.Y - inner.y);
                run.BeginEdit();
                content.SetCaret(run);
            }

            base.ResolveOnClick(oldPos, delta);
        }

        // Nearest TextControl at or above the hit control.
        private static TextControl RunUnder(VulkanControl hit)
        {
            for (VulkanControl control = hit; control != null; control = control.parent as VulkanControl)
                if (control is TextControl run) return run;

            return null;
        }
    }
}
