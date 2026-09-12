using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.Core.UISystem.Controls.Text.Document;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UI
{
    // One styled slice of a run's string. Spans tile the string in order and the last one absorbs
    // whatever is left, so an edit never has to re-cut the list.
    public struct StyleSpan
    {
        public int count;
        public FontStyle style;
        public string colorHex;

        // per-span overrides; null or 0 takes the run's own
        public string fontName;
        public int fontSize;
        public string gradient;

        // carried, never drawn
        public bool strikethrough;

        // where an unauthored size comes from, kept so a note saves back as it was written
        public TextStyleType stylingType;
        public bool fontSizeAuthored;
    }

    // What a run tells when a glyph is pressed.
    public interface IGlyphPressTarget
    {
        void GlyphPressed(TextRunControl run, int index);
    }

    // A block of text as one control: the string, the spans styling it, and one emitted quad per
    // visible character. Glyphs are quads and not controls, so the hit-test lands on the run and
    // IndexAt resolves which character was under the point.
    [A_XSDType("NextTextRun", "UI", isAbstract: true)]
    public class TextRunControl : Control
    {
        private static FontAssetGlyphMetrics metrics = null!;

        private FontAsset _fontAsset;
        private BlockLayout _layout;

        // one entry per span, rebuilt each measure; LineSegment.runIndex indexes all four
        private readonly List<TextMeasurer.Run> _runs = new List<TextMeasurer.Run>();
        private readonly List<Vector3D<float>> _runColors = new List<Vector3D<float>>();
        private readonly List<FontAsset> _runFonts = new List<FontAsset>();
        private readonly List<uint> _runGradients = new List<uint>();

        // where the first line's pen starts, in design space
        private Vector2D<float> _origin;

        private Vector3D<float> _color = new Vector3D<float>(1, 1, 1);
        private float _alpha = 1f;

        // authored text
        public readonly List<StyleSpan> spans = new List<StyleSpan>();
        public FontStyle style = FontStyle.Regular;
        public float lineHeight = 1.5f;

        public TextRunControl()
        {
            _fontAsset = ResolveFont(fontName);
        }

        [A_XSDElementProperty("Text", "UI", "The string this run lays out.")]
        public string text
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                InvalidateLayout();
            }
        } = string.Empty;

        [A_XSDElementProperty("FontSize", "UI", "Type size in design-space pixels.")]
        public int fontSize
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                InvalidateLayout();
            }
        } = 16;

        [A_XSDElementProperty("FontName", "UI", "Font family, as named in the asset manifest.")]
        public string fontName
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                _fontAsset = ResolveFont(value);
                InvalidateLayout();
            }
        } = "default";

        // Colour and alpha reach the glyphs, never the run's own quad, which is never emitted.
        public override string colorHex
        {
            get => base.colorHex;
            set
            {
                base.colorHex = value;
                _color = HexToRGB(value);
                InvalidateArrange();
            }
        }

        public override float alpha
        {
            get => _alpha;
            set
            {
                _alpha = value;
                InvalidateArrange();
            }
        }

        // Replaces the span list and re-measures.
        public void SetSpans(params StyleSpan[] value)
        {
            spans.Clear();
            spans.AddRange(value);
            InvalidateLayout();
        }

        // Mirrors FontAssetGlyphMetrics.Resolve — a run measuring by one name and drawing from
        // another puts every glyph at an x the measurement never predicted.
        private static FontAsset ResolveFont(string name)
        {
            Dictionary<string, FontAsset> fonts =
                AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));

            return fonts.TryGetValue(name ?? "default", out FontAsset named) ? named : fonts["default"];
        }

        #region ---- layout ----
        // Spans in order over the shared string, the last one taking whatever is left. An empty span
        // still takes a slot, so runIndex keeps indexing the span list.
        private void BuildRuns()
        {
            _runs.Clear();
            _runColors.Clear();
            _runFonts.Clear();
            _runGradients.Clear();

            string s = text ?? string.Empty;
            if (spans.Count == 0)
            {
                _runs.Add(new TextMeasurer.Run(s, 0, s.Length, fontName, fontSize, style));
                _runColors.Add(_color);
                _runFonts.Add(_fontAsset);
                _runGradients.Add(visual.gradientIndex);
                return;
            }

            int start = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                int count = i == spans.Count - 1
                    ? s.Length - start
                    : Math.Clamp(spans[i].count, 0, s.Length - start);
                if (count < 0) count = 0;

                string spanFont = spans[i].fontName ?? fontName;
                int spanSize = spans[i].fontSize > 0 ? spans[i].fontSize : fontSize;

                _runs.Add(new TextMeasurer.Run(s, start, count, spanFont, spanSize, spans[i].style));
                _runColors.Add(spans[i].colorHex == null ? _color : HexToRGB(spans[i].colorHex));
                _runFonts.Add(spans[i].fontName == null ? _fontAsset : ResolveFont(spanFont));
                _runGradients.Add(spans[i].gradient == null
                    ? visual.gradientIndex : Gradients.IndexOf(spans[i].gradient));
                start += count;
            }
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            metrics ??= new FontAssetGlyphMetrics();

            ref ArrangeData a = ref arrange;
            float contentWidth = a.preferredWidth > 0 ? a.preferredWidth : availableSize.X;

            BuildRuns();
            _layout = TextMeasurer.MeasureBlock(_runs, contentWidth, metrics, lineHeight);

            float w = a.preferredWidth > 0 ? a.preferredWidth : _layout.width;
            float h = a.preferredHeight > 0 ? a.preferredHeight : _layout.height;

            a.desired = new Vector2D<float>(w, h);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return a.desired;
        }

        // Places the text block. The glyphs are cut at emit, against the clip of the moment.
        public override void Arrange(LayoutRect finalRect)
        {
            WriteArranged(finalRect);
            SetFlag(ArrangeFlags.ArrangeDirty, false);
            if (_layout == null || _fontAsset == null) return;

            ref ArrangeData a = ref arrange;
            LayoutRect inner = finalRect.Shrink(a.padding);

            // text position inside an authored box, the same handshake the outgoing stack has
            float slackX = a.preferredWidth > 0
                ? MathF.Max(0f, inner.width - _layout.width) * a.horizontalPosition : 0f;
            float slackY = a.preferredHeight > 0
                ? MathF.Max(0f, inner.height - _layout.height) * a.verticalPosition : 0f;
            _origin = new Vector2D<float>(inner.x + slackX, inner.y + slackY);
        }

        // The run's own box is not ink, so only glyphs land. A line outside the clip is skipped
        // whole — the pen restarts per line, so dropping one costs the next nothing.
        internal override void Emit(DrawList list)
        {
            if (_layout == null || _fontAsset == null) return;

            LayoutRect box = arrange.clip;
            float z = depth + depthStep;
            Vector4D<float> clip = geometry.clip;
            Vector4D<float> gradientRect = geometry.gradientRect;
            string s = text ?? string.Empty;

            foreach (TextLine line in _layout.lines)
            {
                float lineTop = _origin.Y + line.top;
                if (lineTop + line.height <= box.y) continue;
                if (lineTop >= box.Bottom) break;

                float baselineY = _origin.Y + line.baseline;
                float pen = _origin.X;

                foreach (LineSegment segment in line.segments)
                {
                    Vector3D<float> color = _runColors[segment.runIndex];
                    TextMeasurer.Run run = _runs[segment.runIndex];
                    FontAsset font = _runFonts[segment.runIndex];
                    uint gradientIndex = _runGradients[segment.runIndex];

                    for (int k = 0; k < segment.charCount; k++)
                    {
                        int index = segment.charStart + k;
                        if (index >= s.Length) break;

                        pen += WriteGlyph(list, s[index], run.style, color, font, run.fontSize, gradientIndex,
                                          pen, baselineY, z, clip, gradientRect);
                    }
                }
            }
        }

        // Cuts one glyph's quad out of the atlas and writes both of its columns, returning the pen
        // advance. Cell geometry is GlyphControl's, which is also what FontAssetGlyphMetrics
        // reproduces — three copies of it would drift.
        private float WriteGlyph(DrawList list, char character, FontStyle glyphStyle, Vector3D<float> color,
                                 FontAsset font, int size, uint gradientIndex,
                                 float penX, float baselineY, float z,
                                 Vector4D<float> clip, Vector4D<float> gradientRect)
        {
            AtlasMetaData atlas = font.atlasMetaData;
            (Glyph glyph, int index) = atlas.GetGlyphAndIndex(character);

            // An unimported character draws as a blank of space width rather than throwing.
            if (glyph == null)
            {
                (glyph, index) = atlas.GetGlyphAndIndex(' ');
                if (glyph == null) return 0f;
            }

            FontStyle effective = atlas.Effective(glyphStyle);
            GlyphMetrics m = glyph.Metrics(effective);

            float cellW = m.glyphWidth * size * GlyphControl.CellScale;
            float cellH = m.glyphHeight * size * GlyphControl.CellScale;
            float bearingX = m.leftSideOffset * size - cellW * GlyphControl.atlasInkMargin;

            float ascent = 0f;
            int range = m.yMax - m.yMin;
            if (range != 0)
            {
                // baselineFromTop is a fraction OF THE CELL, so it scales the cell height
                float baselineFromTop = GlyphControl.atlasInkMargin
                    + (1f - 2f * GlyphControl.atlasInkMargin) * m.yMax / range;
                ascent = baselineFromTop * cellH;
            }

            float x = penX + bearingX;
            float y = baselineY - ascent;

            Matrix4X4<float> matrix = Matrix4X4<float>.Identity;
            matrix *= Matrix4X4.CreateScale(cellW, cellH, 1f);
            matrix *= Matrix4X4.CreateTranslation(x + cellW * 0.5f, y + cellH * 0.5f, z);

            int slot = list.Next();
            ref ControlGeometry g = ref list.GeometryAt(slot);
            g.matrix = matrix;
            g.clip = clip;
            g.gradientRect = gradientRect;

            int cell = atlas.CellIndex(index, effective);
            float k = MathF.Ceiling(MathF.Sqrt(atlas.cellCount));
            float cellUV = 1f / k;
            float xOffset = cell % k * cellUV;
            float yOffset = MathF.Floor(cell / k) * cellUV;
            float texelPad = 1f / font.textureAsset.image.Width;

            float u0 = xOffset + texelPad;
            float v0 = yOffset + texelPad;
            float u1 = xOffset + cellUV - texelPad;
            float v1 = yOffset + cellUV - texelPad;

            ref VulkanControl v = ref list.VisualAt(slot);
            v.type = VulkanControlType.MTSDFControl;
            v.uvs.uv1 = new Vector2D<float>(u1, v1);
            v.uvs.uv2 = new Vector2D<float>(u0, v0);
            v.uvs.uv3 = new Vector2D<float>(u0, v1);
            v.uvs.uv4 = new Vector2D<float>(u1, v0);
            v.tint = new Vector4D<float>(color, _alpha);
            v.textureIndex = font.textureAsset.textureIndex;
            v.cornerRadius = Vector4D<float>.Zero;
            v.edgeColor = Vector3D<float>.Zero;
            v.edgeThickness = 0f;
            v.gradientIndex = gradientIndex;

            return m.advanceWidth * size;
        }
        #endregion

        #region ---- caret geometry ----
        // Design space point to the character slot it belongs to. A press past a character's
        // midpoint takes the slot after it, or the end of a word could never be clicked.
        public int IndexAt(Vector2D<float> point)
        {
            if (_layout == null) return 0;

            float localX = point.X - _origin.X;
            TextLine line = _layout.lines[LineAt(point.Y - _origin.Y)];
            float pen = 0f;

            foreach (LineSegment segment in line.segments)
            {
                TextMeasurer.Run run = _runs[segment.runIndex];
                for (int i = 0; i < segment.charCount; i++)
                {
                    float advance = TextMeasurer.MeasureAdvance(text[segment.charStart + i], run, metrics);
                    if (localX < pen + advance * 0.5f) return segment.charStart + i;
                    pen += advance;
                }
            }

            LineSegment last = line.segments[line.segments.Count - 1];
            return last.charStart + last.charCount;
        }

        // Character slot to a caret rect, local to TextOrigin. The slot at the end of a wrapped line
        // is claimed by the line below, or the caret sits off the right edge instead of before the
        // word that wrapped.
        public CaretGeometry CaretAt(int offset)
        {
            if (_layout == null) return new CaretGeometry(0f, 0f, fontSize * lineHeight, 0f);

            for (int i = 0; i < _layout.lines.Count; i++)
            {
                TextLine line = _layout.lines[i];
                bool isLastLine = i == _layout.lines.Count - 1;
                float x = 0f;

                for (int s = 0; s < line.segments.Count; s++)
                {
                    LineSegment segment = line.segments[s];
                    int end = segment.charStart + segment.charCount;

                    bool endBelongsHere = isLastLine || s < line.segments.Count - 1;
                    bool holds = offset >= segment.charStart
                                 && (offset < end || (endBelongsHere && offset == end));

                    if (!holds)
                    {
                        x += segment.width;
                        continue;
                    }

                    TextMeasurer.Run run = _runs[segment.runIndex];
                    for (int c = segment.charStart; c < offset; c++)
                        x += TextMeasurer.MeasureAdvance(text[c], run, metrics);

                    return new CaretGeometry(x, line.top, line.height, line.baseline);
                }
            }

            TextLine lastLine = _layout.lines[_layout.lines.Count - 1];
            return new CaretGeometry(lastLine.width, lastLine.top, lastLine.height, lastLine.baseline);
        }

        // What CaretAt is relative to: the design-space point the first line's pen starts at.
        public Vector2D<float> TextOrigin => _origin;

        // The measured lines, in this run's own space. Null until the first measure.
        public IReadOnlyList<TextLine> Lines => _layout?.lines;

        public int Length => (text ?? string.Empty).Length;

        // Lowest line not past y; clamps at both ends.
        private int LineAt(float y)
        {
            int low = 0;
            int high = _layout.lines.Count - 1;

            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (_layout.lines[mid].top <= y) low = mid;
                else high = mid - 1;
            }
            return low;
        }
        #endregion

        public override bool OnPointerPress(PointerEvent e)
        {
            if (base.OnPointerPress(e)) return true;

            IGlyphPressTarget target = FindPressTarget();
            if (target == null) return false;

            target.GlyphPressed(this, IndexAt(e.point));
            return true;
        }

        private IGlyphPressTarget FindPressTarget()
        {
            Entity current = parent;
            while (current != null)
            {
                if (current is IGlyphPressTarget target) return target;
                current = current.parent;
            }
            return null;
        }
    }
}
