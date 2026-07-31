using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;
// WinForms is enabled in this project; alias the engine control to avoid the
// System.Windows.Forms.ScrollableControl clash.
using ScrollableControl = ArctisAurora.Core.UISystem.Controls.Containers.ScrollableControl;

namespace ArctisAurora.Core.UISystem.Controls.Text.Document
{
    // The document's coordinate space. Its height is the layout cache's extent rather than the sum
    // of its children, because most of the document has no children — that is what lets the
    // scrollbar describe a whole note while only a viewport of it exists. Children are placed at
    // absolute cache coordinates rather than flowed, so nothing here re-derives any geometry.
    //
    // A container base rather than a plain VulkanControl because that one accepts a single child by
    // design — the canvas holds one control per visible strip.
    public class DocumentCanvasControl : AbstractContainerControl
    {
        public float contentHeight;

        public DocumentCanvasControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            horizontalAlignment = HorizontalAlignment.Stretch;
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            foreach (Entity child in children)
                if (child is VulkanControl control)
                    control.Measure(availableSize);

            DesiredSize = new Vector2D<float>(availableSize.X, contentHeight);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            WriteArrangedTransform(finalRect);

            // finalRect is the full content box, shifted above the viewport by the scroll offset,
            // so intersecting with the viewport's clip is what keeps the off-screen part off screen.
            ClipRect = parent is VulkanControl parentControl
                ? LayoutRect.Intersect(finalRect, parentControl.ClipRect)
                : finalRect;

            foreach (Entity child in children)
            {
                if (child is not TextRunControl run) continue;

                run.Arrange(new LayoutRect(
                    finalRect.x + run.documentRect.x,
                    finalRect.y + run.documentRect.y,
                    run.documentRect.width,
                    run.documentRect.height));
            }

            isArrangeDirty = false;
        }
    }

    // The view over a RichTextDocument. A scrollable viewport whose single child is a canvas sized
    // by the layout cache; the visible line segments are materialized into it as TextRunControls
    // placed at cached coordinates, and everything outside a one-viewport buffer is released.
    //
    // The cache is the only thing that decides geometry here. The view asks it what is visible and
    // where each strip goes, and never measures a line break of its own — which is why the run
    // renderer is TextRunControl rather than TextInputControl.
    //
    // Read-only. Editing (working copy, caret, input) arrives in P3.
    [A_XSDType("DocumentEditor", "UI")]
    public class DocumentEditorControl : ScrollableControl
    {
        public RichTextDocument activeDocument { get; private set; }

        // Identity of one materialized strip. Keyed rather than held in a flat list so a scroll that
        // keeps a segment on screen leaves its control completely untouched — only the strips
        // crossing the buffer edge are built or torn down.
        private readonly struct SegmentKey : IEquatable<SegmentKey>
        {
            public readonly int block;
            public readonly int line;
            public readonly int segment;

            public SegmentKey(int block, int line, int segment)
            {
                this.block = block;
                this.line = line;
                this.segment = segment;
            }

            public bool Equals(SegmentKey other) =>
                block == other.block && line == other.line && segment == other.segment;

            public override bool Equals(object obj) => obj is SegmentKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(block, line, segment);
        }

        private readonly DocumentLayoutCache cache = new DocumentLayoutCache(new FontAssetGlyphMetrics());
        private readonly Dictionary<SegmentKey, TextRunControl> materialized = new Dictionary<SegmentKey, TextRunControl>();
        private readonly HashSet<SegmentKey> visible = new HashSet<SegmentKey>();
        private readonly List<SegmentKey> leaving = new List<SegmentKey>();

        private DocumentCanvasControl canvas;
        private bool needsRebuild;

        public DocumentEditorControl()
        {
            scrollDirection = ScrollDirection.Vertical;
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        // Engine-XML note to load. Path is resolved through the VFS (Paths.Doc) when relative, or used
        // as-is when rooted (the vault passes absolute paths in P5).
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
            materialized.Clear();

            // The cache cannot be built yet: wrapping needs the content width, which is not known
            // until this control is measured. Deferred to the first Measure.
            needsRebuild = true;

            // Destroy rather than drop: clearing `children` alone orphans the previous canvas, which
            // keeps its pool row and its place in the "Controls" group while being unreachable from
            // the tree — the exact state the DFS order cannot produce an ordering for.
            foreach (Entity child in children.ToArray())
                child.Destroy();
            children.Clear();

            canvas = new DocumentCanvasControl();
            AddChild(canvas);
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            if (activeDocument != null)
            {
                // Mirrors the width the base measures the child against, since that is the column
                // the text has to wrap inside.
                float own = preferredWidth > 0 ? preferredWidth : MathF.Max(availableSize.X, minWidth);
                float contentWidth = MathF.Max(0, own - padding.totalHorizontal);

                bool rewrapped;
                if (needsRebuild)
                {
                    cache.Rebuild(activeDocument, contentWidth);
                    needsRebuild = false;
                    rewrapped = true;
                }
                else rewrapped = cache.SetContentWidth(contentWidth);

                // Line and segment indices are what the materialized controls are keyed by, and a
                // rewrap changes what those indices mean — so every strip has to go, not just the
                // ones that moved.
                if (rewrapped) ReleaseAll();

                // Scroll extent comes from the cache rather than from measuring children. Measuring
                // would only ever see the materialized viewport and size the scrollbar to it.
                canvas.contentHeight = cache.Extent;
            }

            return base.Measure(availableSize);
        }

        public override void Arrange(LayoutRect finalRect)
        {
            if (activeDocument != null)
            {
                LayoutRect inner = finalRect.Shrink(padding);
                SyncVisible(GetScrollOffset().Y, inner.height);
            }

            base.Arrange(finalRect);
        }

        // Materialize the strips inside the viewport plus one viewport of buffer either side, and
        // release the rest.
        //
        // The buffer is what makes the deferred destroy invisible: releasing only enqueues, and the
        // control still draws until ProcessDestroys runs at the top of the next tick. A strip
        // released at the buffer edge spends that frame a full viewport off screen, so the frame it
        // survives is a frame outside the clip rect.
        private void SyncVisible(float scrollY, float viewportHeight)
        {
            if (cache.BlockCount == 0) return;

            float top = scrollY - viewportHeight;
            float bottom = scrollY + viewportHeight * 2f;

            visible.Clear();

            for (int b = cache.BlockAtY(top); b < cache.BlockCount; b++)
            {
                float blockTop = cache.TopOf(b);
                if (blockTop > bottom) break;

                BlockLayout block = cache.BlockAt(b);
                ContentBlock content = (ContentBlock)activeDocument.blocks[b];
                int fontSize = activeDocument.layout.FontSizeFor(content);

                for (int l = 0; l < block.lines.Count; l++)
                {
                    TextLine line = block.lines[l];
                    float lineTop = blockTop + line.top;
                    if (lineTop + line.height < top || lineTop > bottom) continue;

                    // Segments sit end to end across the line, so a segment's x is the width of
                    // everything before it — which the cache already measured.
                    float x = 0f;
                    for (int s = 0; s < line.segments.Count; s++)
                    {
                        LineSegment segment = line.segments[s];

                        // An empty segment is a real caret slot (an empty paragraph is one) but has
                        // nothing to draw, so it gets geometry in the cache and no control here.
                        if (segment.charCount > 0)
                            Materialize(new SegmentKey(b, l, s), content, segment, line, x, lineTop, fontSize);

                        x += segment.width;
                    }
                }
            }

            Release();
        }

        private void Materialize(SegmentKey key, ContentBlock content, LineSegment segment, TextLine line,
            float x, float lineTop, int fontSize)
        {
            visible.Add(key);

            bool isNew = !materialized.TryGetValue(key, out TextRunControl run);
            if (isNew)
            {
                run = new TextRunControl();
                materialized[key] = run;
            }

            // Set before SetSegment, which reads the height for its DesiredSize.
            run.documentRect = new LayoutRect(x, lineTop, segment.width, line.height);
            run.baselineOffset = line.ascent;

            // Only on creation: a strip already on screen holds the same characters it did last
            // frame, because any change to what a key means goes through ReleaseAll instead.
            if (!isNew) return;

            TextRun source = content.inlines[segment.runIndex];
            run.SetSegment(source.text.Substring(segment.charStart, segment.charCount),
                source.fontName, fontSize, source.runColorHex);

            canvas.AddChild(run);
        }

        // Destroy() detaches from the canvas itself, so the collection being iterated is always
        // `materialized` and never the children list it mutates.
        private void Release()
        {
            leaving.Clear();
            foreach (KeyValuePair<SegmentKey, TextRunControl> entry in materialized)
                if (!visible.Contains(entry.Key))
                    leaving.Add(entry.Key);

            foreach (SegmentKey key in leaving)
            {
                materialized[key].Destroy();
                materialized.Remove(key);
            }
        }

        private void ReleaseAll()
        {
            foreach (TextRunControl run in materialized.Values)
                run.Destroy();

            materialized.Clear();
        }
    }
}
