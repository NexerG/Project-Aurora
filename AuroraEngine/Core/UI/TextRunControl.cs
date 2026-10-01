using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Threading;
using ArctisAurora.EngineWork.Registry;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // How text flows around a picture in a note.
    [A_XSDType("PictureWrap", "UI")]
    public enum PictureWrap
    {
        Inline,
        Square,
        Tight,
        TopAndBottom,
        Behind,
        InFront
    }

    // What a turned Square picture pushes text away from.
    [A_XSDType("PictureCollision", "UI")]
    public enum PictureCollision
    {
        Box,
        Shape
    }

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
        public string effect;

        // decorations; a null highlight draws nothing behind the text
        public bool strikethrough;
        public bool underline;
        public string highlightHex;

        // where an unauthored size comes from, kept so a note saves back as it was written
        public TextStyleType stylingType;
        public bool fontSizeAuthored;

        // a picture: one U+FFFC drawn from this file; a 0 size takes the picture's own
        public string imageSource;
        public float imageWidth;
        public float imageHeight;

        // a floating picture's wrap and its offset from the column's left and its paragraph's top
        public PictureWrap wrap;
        public float imageX;
        public float imageY;

        // a picture's turn in clockwise degrees, and what a turned Square float wraps
        public float imageRotation;
        public PictureCollision collision;

        public Quaternion Rotation => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, imageRotation * MathF.PI / 180f);
        public bool IsFloating => IsPicture && wrap != PictureWrap.Inline;
        public bool IsBold => style == FontStyle.Bold || style == FontStyle.BoldItalic;
        public bool IsItalic => style == FontStyle.Italic || style == FontStyle.BoldItalic;
        public bool IsPicture => imageSource != null;

        // The same style with the picture taken off.
        public StyleSpan AsText()
        {
            StyleSpan text = this;
            text.imageSource = null;
            text.imageWidth = 0f;
            text.imageHeight = 0f;
            text.wrap = PictureWrap.Inline;
            text.imageX = 0f;
            text.imageY = 0f;
            text.imageRotation = 0f;
            text.collision = PictureCollision.Box;
            return text;
        }
    }

    // What a run tells when a glyph is pressed.
    public interface IGlyphPressTarget
    {
        void GlyphPressed(TextRunControl run, int index);
        void PicturePressed(TextRunControl run, int index, int button);
    }

    // A block of text as one control: the string, the spans styling it, and one emitted quad per
    // visible character. Glyphs are quads and not controls, so the hit-test lands on the run and
    // IndexAt resolves which character was under the point.
    [A_XSDType("TextRun", "UI", isAbstract: true)]
    public class TextRunControl : Control
    {
        private static FontAssetGlyphMetrics metrics = null!;

        private FontAsset _fontAsset;
        private BlockLayout _layout;
        private float _wrapWidth;

        // one entry per span, rebuilt each measure; LineSegment.runIndex indexes all of them
        private readonly List<TextMeasurer.Run> _runs = new List<TextMeasurer.Run>();
        private readonly List<uint> _runPaints = new List<uint>();
        private readonly List<FontAsset> _runFonts = new List<FontAsset>();
        private readonly List<uint> _runGradients = new List<uint>();
        private readonly List<uint> _runEffects = new List<uint>();
        private readonly List<(bool underline, bool strike, uint highlight)> _runDecorations = new List<(bool, bool, uint)>();
        private readonly List<TextureAsset?> _runTextures = new List<TextureAsset?>();
        private readonly List<(Vector2 size, Quaternion rotation)> _runPictures = new List<(Vector2, Quaternion)>();

        // characters under a selection, which a highlight leaves a gap for; -1 when none
        internal int selectedFrom = -1;
        internal int selectedTo = -1;

        // decoration geometry, as fractions of the font size
        private const float underlineDrop = 0.12f;
        private const float strikeRise = 0.28f;
        private const float decorationWeight = 0.07f;

        // where the first line's pen starts, in design space
        private Vector2 _origin;

        private uint _paint = Palettes.Inline(Vector3.One);

        // authored text
        public readonly List<StyleSpan> spans = new List<StyleSpan>();
        public FontStyle style = FontStyle.Regular;
        public float lineHeight = 1.5f;

        // multiplies every font size at layout; the authored sizes are untouched
        internal float textZoom = 1f;

        protected virtual bool Wraps => true;

        public TextRunControl()
        {
            _fontAsset = ResolveFont(fontName);
            role = PaletteRole.Ink;
            kind = VulkanControlType.MTSDFControl;
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
                InvalidateLayout();
            }
        }

        // Repaints the runs that take the run's own colour, without a re-measure.
        protected override void SetPaint(uint paint)
        {
            base.SetPaint(paint);
            if (_paint == paint) return;
            _paint = paint;
            if (isMeasureDirty || _runPaints.Count != Math.Max(1, spans.Count)) return;

            for (int i = 0; i < _runPaints.Count; i++)
                if (spans.Count == 0 || spans[i].colorHex == null)
                    _runPaints[i] = paint;
        }

        // Replaces the span list and re-measures.
        public void SetSpans(params StyleSpan[] value)
        {
            spans.Clear();
            spans.AddRange(value);
            InvalidateLayout();
        }

        // Resolves a font name, falling back to default.
        private static FontAsset ResolveFont(string name)
        {
            Dictionary<string, FontAsset> fonts =
                AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));

            return fonts.TryGetValue(name ?? "default", out FontAsset named) ? named : fonts["default"];
        }

        #region ---- layout ----
        // Spans in order over the shared string, the last one taking whatever is left. An empty span
        // still takes a slot, so runIndex keeps indexing the span list.
        private void BuildRuns(float wrapWidth)
        {
            _runs.Clear();
            _runPaints.Clear();
            _runFonts.Clear();
            _runGradients.Clear();
            _runEffects.Clear();
            _runDecorations.Clear();
            _runTextures.Clear();
            _runPictures.Clear();

            string s = text ?? string.Empty;
            if (spans.Count == 0)
            {
                _runs.Add(new TextMeasurer.Run(s, 0, s.Length, fontName, _fontAsset.atlasMetaData, Zoomed(fontSize), style));
                _runPaints.Add(_paint);
                _runFonts.Add(_fontAsset);
                _runGradients.Add(gradientId);
                _runEffects.Add(visual.effect);
                _runDecorations.Add((false, false, 0u));
                _runTextures.Add(null);
                _runPictures.Add((Vector2.Zero, Quaternion.Identity));
                return;
            }

            int start = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                int count = i == spans.Count - 1
                    ? s.Length - start
                    : Math.Clamp(spans[i].count, 0, s.Length - start);
                if (count < 0) count = 0;

                string spanFont = FontFor(spans[i]);
                int spanSize = spans[i].fontSize > 0 ? spans[i].fontSize : fontSize;
                FontAsset font = spanFont == fontName ? _fontAsset : ResolveFont(spanFont);

                if (spans[i].IsPicture)
                {
                    TextureAsset? picture = TextureAsset.ForFile(spans[i].imageSource);
                    (float w, float h) = PictureSize(spans[i], picture, wrapWidth);
                    Quaternion rotation = spans[i].Rotation;
                    Vector2 box = spans[i].IsFloating ? new Vector2(w, h) : new LayoutRect(0f, 0f, w, h).Turned(rotation).size;
                    _runs.Add(new TextMeasurer.Run(s, start, count, spanFont, font.atlasMetaData, Zoomed(spanSize), spans[i].style, true, box.X, box.Y,
                        spans[i].IsFloating));
                    _runTextures.Add(picture);
                    _runPictures.Add((new Vector2(w, h), rotation));
                }
                else
                {
                    _runs.Add(new TextMeasurer.Run(s, start, count, spanFont, font.atlasMetaData, Zoomed(spanSize), spans[i].style));
                    _runTextures.Add(null);
                    _runPictures.Add((Vector2.Zero, Quaternion.Identity));
                }
                _runPaints.Add(spans[i].colorHex == null ? _paint : Palettes.Inline(spans[i].colorHex));
                _runFonts.Add(font);
                _runGradients.Add(spans[i].gradient == null
                    ? gradientId : Gradients.IndexOf(spans[i].gradient));
                _runEffects.Add(spans[i].effect == null ? visual.effect : Effects.IndexOf(spans[i].effect));
                _runDecorations.Add((spans[i].underline, spans[i].strikethrough,
                    spans[i].highlightHex == null ? 0u : Palettes.Inline(spans[i].highlightHex)));
                start += count;
            }
        }

        // The authored size at the picture's aspect, or its own size capped to the column; zoomed.
        private (float width, float height) PictureSize(StyleSpan span, TextureAsset? picture, float wrapWidth)
        {
            Vector2 native = picture != null ? new Vector2(picture.image.Width, picture.image.Height) : Vector2.Zero;
            float aspect = native.X > 0f ? native.Y / native.X : 0f;
            float w = span.imageWidth;
            float h = span.imageHeight;

            if (w <= 0f && h <= 0f)
            {
                w = MathF.Min(native.X * textZoom, wrapWidth);
                return (w, w * aspect);
            }

            if (w <= 0f) w = aspect > 0f ? h / aspect : 0f;
            else if (h <= 0f) h = w * aspect;
            return (w * textZoom, h * textZoom);
        }

        private int Zoomed(int size) => textZoom == 1f ? size : Math.Max(1, (int)MathF.Round(size * textZoom));

        protected virtual string FontFor(in StyleSpan span) => span.fontName ?? fontName;

        protected virtual TextAlignment Alignment => TextAlignment.Left;

        // Shifts each line across its room by the alignment, trailing spaces left hanging.
        private void Align()
        {
            float factor = Alignment switch { TextAlignment.Center => 0.5f, TextAlignment.Right => 1f, _ => 0f };
            if (factor == 0f || _layout == null) return;

            string s = text ?? string.Empty;
            foreach (TextLine line in _layout.lines)
            {
                float room = line.room > 0f ? line.room : _wrapWidth;
                if (room == float.MaxValue) continue;

                float pen = 0f, visible = 0f;
                foreach (LineSegment segment in line.segments)
                    for (int k = segment.charStart; k < segment.charStart + segment.charCount && k < s.Length; k++)
                    {
                        pen += TextMeasurer.MeasureAdvance(s[k], _runs[segment.runIndex], pen);
                        if (s[k] != ' ' && s[k] != '\t') visible = pen;
                    }

                line.left += MathF.Max(0f, room - visible) * factor;
            }

            _layout.width = 0f;
            foreach (TextLine line in _layout.lines)
                _layout.width = MathF.Max(_layout.width, line.left + line.width);
        }

        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            metrics ??= new FontAssetGlyphMetrics();

            ref ArrangeData a = ref arrange;
            float contentWidth = a.preferredWidth > 0 ? a.preferredWidth : availableSize.X;
            float wrapWidth = Wraps ? contentWidth : float.MaxValue;

            if (!isMeasureDirty && _layout != null && wrapWidth == _wrapWidth) return a.desired;

            Profiling.Zone.Start("Text.BuildRuns");
            BuildRuns(wrapWidth);
            Profiling.Zone.End("Text.BuildRuns");

            Profiling.Zone.Start("Text.MeasureBlock");
            _layout = TextMeasurer.MeasureBlock(_runs, wrapWidth, metrics, lineHeight);
            laidAround = false;
            Profiling.Zone.End("Text.MeasureBlock");
            _wrapWidth = wrapWidth;
            Align();

            float w = a.preferredWidth > 0 ? a.preferredWidth : _layout.width;
            float h = a.preferredHeight > 0 ? a.preferredHeight : _layout.height;

            a.desired = new Vector2(w, h);
            SetFlag(ArrangeFlags.MeasureDirty, false);
            return a.desired;
        }

        // Re-lays the measured runs line by line through the slots; null lays them plainly again.
        internal float LayoutAround(ILineSlots? slots)
        {
            if (_layout == null) return 0f;

            _layout = TextMeasurer.MeasureBlock(_runs, _wrapWidth, metrics, lineHeight, 0f, slots);
            laidAround = slots != null;
            Align();
            return _layout.height;
        }

        // the lines were last laid around something rather than plainly
        internal bool laidAround;

        // A picture's drawn size at this run's zoom.
        internal Vector2 PictureSizeAt(int index)
        {
            for (int i = 0; i < _runs.Count; i++)
                if (_runs[i].picture && index >= _runs[i].charStart && index < _runs[i].charStart + _runs[i].charCount)
                    return _runPictures[i].size;
            return Vector2.Zero;
        }

        // Places the text block. The glyphs are cut at emit, against the clip of the moment.
        protected override void ArrangeCore(LayoutRect finalRect)
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
            _origin = new Vector2(inner.x + slackX, inner.y + slackY);
        }

        // The run's own box is not ink, so only glyphs land. A line outside the clip is skipped
        // whole — the pen restarts per line, so dropping one costs the next nothing.
        internal override void Emit(float z)
        {
            if (_layout == null || _fontAsset == null) return;

            LayoutRect box = arrange.clip;
            LayoutRect arranged = arrange.arranged;
            z += depthStep;
            Vector4 clip = new Vector4(box.x, box.y, box.Right, box.Bottom);
            Vector4 gradientRect = new Vector4(arranged.x, arranged.y, arranged.Right, arranged.Bottom);
            DataPool quads = UIEngine.Quads;
            string s = text ?? string.Empty;
            float started = visual.effectStart;
            float alpha = this.alpha;

            foreach (TextLine line in _layout.lines)
            {
                float lineTop = _origin.Y + line.top;
                if (lineTop + line.height <= box.y) continue;
                if (lineTop >= box.Bottom) break;

                float baselineY = _origin.Y + line.baseline;
                float pen = _origin.X + line.left;
                float lineStart = pen;

                foreach (LineSegment segment in line.segments)
                {
                    uint runGradient = _runGradients[segment.runIndex];
                    uint paint = runGradient != 0
                        ? Gradients.Word(runGradient, palette ?? Palettes.Default)
                        : _runPaints[segment.runIndex];
                    TextMeasurer.Run run = _runs[segment.runIndex];
                    FontAsset font = _runFonts[segment.runIndex];
                    uint effect = _runEffects[segment.runIndex];
                    float stagger = Effects.Stagger(effect);
                    (bool underline, bool strike, uint highlight) = _runDecorations[segment.runIndex];
                    float segmentX = pen;

                    if (run.picture)
                    {
                        for (int k = 0; k < segment.charCount && !run.floating; k++)
                        {
                            (Vector2 size, Quaternion rotation) = _runPictures[segment.runIndex];
                            WriteImage(quads, _runTextures[segment.runIndex], new LayoutRect(pen, baselineY - run.imageHeight, run.imageWidth, run.imageHeight),
                                       size, rotation, alpha, z, clip);
                            pen += run.imageWidth;
                        }
                        continue;
                    }

                    if (highlight != 0)
                        WriteHighlight(quads, segment, segmentX, lineTop, line.height, highlight, alpha, z - depthStep, clip, gradientRect);

                    for (int k = 0; k < segment.charCount; k++)
                    {
                        int index = segment.charStart + k;
                        if (index >= s.Length) break;

                        float effectStart = started + (index - run.charStart) * stagger;
                        if (s[index] == '\t')
                        {
                            pen += TextMeasurer.TabAdvance(run, pen - lineStart);
                            continue;
                        }
                        pen += WriteGlyph(quads, s[index], run.style, paint, alpha, font, run.fontSize, effect, effectStart,
                                          pen, baselineY, z, clip, gradientRect);
                    }

                    float weight = MathF.Max(1f, run.fontSize * decorationWeight);
                    if (underline)
                        WriteRect(quads, segmentX, baselineY + run.fontSize * underlineDrop, pen - segmentX, weight, paint, alpha, z, clip, gradientRect);
                    if (strike)
                        WriteRect(quads, segmentX, baselineY - run.fontSize * strikeRise - weight * 0.5f, pen - segmentX, weight, paint, alpha, z, clip, gradientRect);

                    if (effect != 0)
                        FrameScheduler.RequestFrameAt(started + (segment.charStart + segment.charCount - run.charStart) * stagger + Effects.Duration(effect));
                }
            }
        }

        // A segment's highlight, with a gap where the selection covers it.
        private void WriteHighlight(DataPool quads, LineSegment segment, float x, float top, float height,
                                    uint paint, float alpha, float z, Vector4 clip, Vector4 gradientRect)
        {
            float right = x + segment.width;
            int end = segment.charStart + segment.charCount;

            if (selectedTo <= segment.charStart || selectedFrom >= end || selectedFrom < 0)
            {
                WriteRect(quads, x, top, segment.width, height, paint, alpha, z, clip, gradientRect);
                return;
            }

            float gapLeft = selectedFrom <= segment.charStart ? x : _origin.X + CaretAt(selectedFrom).x;
            float gapRight = selectedTo >= end ? right : _origin.X + CaretAt(selectedTo).x;
            if (gapLeft > x) WriteRect(quads, x, top, gapLeft - x, height, paint, alpha, z, clip, gradientRect);
            if (right > gapRight) WriteRect(quads, gapRight, top, right - gapRight, height, paint, alpha, z, clip, gradientRect);
        }

        // One flat rectangle: a highlight or a text decoration.
        protected static void WriteRect(DataPool quads, float x, float y, float width, float height,
                                      uint paint, float alpha, float z, Vector4 clip, Vector4 gradientRect)
        {
            if (width <= 0f || height <= 0f) return;

            int row = quads.Append();
            ref ControlGeometry g = ref quads.GetSpan<ControlGeometry>()[row];
            g.matrix = Matrix4x4.CreateScale(width, height, 1f)
                     * Matrix4x4.CreateTranslation(x + width * 0.5f, y + height * 0.5f, z);
            g.clip = clip;
            g.gradientRect = gradientRect;

            ref VulkanControl v = ref quads.GetSpan<VulkanControl>()[row];
            v = default;
            v.type = VulkanControlType.PanelControl;
            v.paint = paint;
            v.alpha = alpha;
            v.textureIndex = VulkanControl.noTexture;
        }

        // One picture, the whole texture turned about the centre of its box.
        private static void WriteImage(DataPool quads, TextureAsset? picture, LayoutRect box, Vector2 size, Quaternion rotation,
                                       float alpha, float z, Vector4 clip)
        {
            if (picture == null || size.X <= 0f || size.Y <= 0f) return;

            float cx = box.x + box.width * 0.5f;
            float cy = box.y + box.height * 0.5f;
            int row = quads.Append();
            ref ControlGeometry g = ref quads.GetSpan<ControlGeometry>()[row];
            g.matrix = Matrix4x4.CreateScale(size.X, size.Y, 1f)
                     * Matrix4x4.CreateFromQuaternion(rotation)
                     * Matrix4x4.CreateTranslation(cx, cy, z);
            g.clip = clip;
            g.gradientRect = new Vector4(cx - size.X * 0.5f, cy - size.Y * 0.5f, cx + size.X * 0.5f, cy + size.Y * 0.5f);

            ref VulkanControl v = ref quads.GetSpan<VulkanControl>()[row];
            v = default;
            v.type = VulkanControlType.ImageControl;
            v.uvs.uv1 = new Vector2(1f, 1f);
            v.uvs.uv2 = new Vector2(0f, 0f);
            v.uvs.uv3 = new Vector2(0f, 1f);
            v.uvs.uv4 = new Vector2(1f, 0f);
            v.paint = Palettes.Inline(Vector3.One);
            v.alpha = alpha;
            v.textureIndex = picture.textureIndex;
        }

        // Cuts one glyph's quad out of the atlas and writes both of its columns, returning the pen
        // advance. Cell geometry is GlyphControl's, which is also what FontAssetGlyphMetrics
        // reproduces — three copies of it would drift.
        private float WriteGlyph(DataPool quads, char character, FontStyle glyphStyle, uint paint, float alpha,
                                 FontAsset font, int size, uint effect, float effectStart,
                                 float penX, float baselineY, float z,
                                 Vector4 clip, Vector4 gradientRect)
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

            float cellW = m.glyphWidth * size * TextMeasurer.CellScale;
            float cellH = m.glyphHeight * size * TextMeasurer.CellScale;
            float bearingX = m.leftSideOffset * size - cellW * TextMeasurer.atlasInkMargin;

            float ascent = 0f;
            int range = m.yMax - m.yMin;
            if (range != 0)
            {
                // baselineFromTop is a fraction OF THE CELL, so it scales the cell height
                float baselineFromTop = TextMeasurer.atlasInkMargin
                    + (1f - 2f * TextMeasurer.atlasInkMargin) * m.yMax / range;
                ascent = baselineFromTop * cellH;
            }

            float x = penX + bearingX;
            float y = baselineY - ascent;

            Matrix4x4 matrix = Matrix4x4.Identity;
            matrix *= Matrix4x4.CreateScale(cellW, cellH, 1f);
            matrix *= Matrix4x4.CreateTranslation(x + cellW * 0.5f, y + cellH * 0.5f, z);

            int row = quads.Append();
            ref ControlGeometry g = ref quads.GetSpan<ControlGeometry>()[row];
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

            ref VulkanControl v = ref quads.GetSpan<VulkanControl>()[row];
            v.type = VulkanControlType.MTSDFControl;
            v.uvs.uv1 = new Vector2(u1, v1);
            v.uvs.uv2 = new Vector2(u0, v0);
            v.uvs.uv3 = new Vector2(u0, v1);
            v.uvs.uv4 = new Vector2(u1, v0);
            v.paint = paint;
            v.alpha = alpha;
            v.textureIndex = font.textureAsset.textureIndex;
            v.cornerRadius = Vector4.Zero;
            v.edgePaint = 0;
            v.edgeThickness = Thickness.Zero;
            v.state = 0f;
            v.effect = effect;
            v.effectStart = effectStart;

            return m.advanceWidth * size;
        }
        #endregion

        #region ---- caret geometry ----
        // Design space point to the character slot it belongs to. A press past a character's
        // midpoint takes the slot after it, or the end of a word could never be clicked.
        public int IndexAt(Vector2 point)
        {
            if (_layout == null) return 0;

            TextLine line = _layout.lines[LineAt(point.Y - _origin.Y)];
            float localX = point.X - _origin.X - line.left;
            float pen = 0f;

            foreach (LineSegment segment in line.segments)
            {
                TextMeasurer.Run run = _runs[segment.runIndex];
                for (int i = 0; i < segment.charCount; i++)
                {
                    float advance = TextMeasurer.MeasureAdvance(text[segment.charStart + i], run, pen);
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
                float x = line.left;

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
                        x += TextMeasurer.MeasureAdvance(text[c], run, x - line.left);

                    return new CaretGeometry(x, line.top, line.height, line.baseline);
                }
            }

            TextLine lastLine = _layout.lines[_layout.lines.Count - 1];
            return new CaretGeometry(lastLine.left + lastLine.width, lastLine.top, lastLine.height, lastLine.baseline);
        }

        // What CaretAt is relative to: the design-space point the first line's pen starts at.
        public Vector2 TextOrigin => _origin;

        // The measured lines, in this run's own space. Null until the first measure.
        public IReadOnlyList<TextLine> Lines => _layout?.lines;

        public int Length => (text ?? string.Empty).Length;

        // The picture under a design-space point, or -1.
        public int PictureAt(Vector2 point)
        {
            if (_layout == null) return -1;

            TextLine line = _layout.lines[LineAt(point.Y - _origin.Y)];
            foreach (LineSegment segment in line.segments)
            {
                if (!_runs[segment.runIndex].picture) continue;

                for (int i = segment.charStart; i < segment.charStart + segment.charCount; i++)
                    if (PictureFrame(i, out LayoutRect rect, out Quaternion rotation) && rect.Contains(rect.Unturned(point, rotation))) return i;
            }
            return -1;
        }

        // A picture character's drawn box in design space; false when the index is not a picture.
        public bool PictureBox(int index, out LayoutRect box)
        {
            box = LayoutRect.Empty;
            if (_layout == null) return false;

            foreach (TextLine line in _layout.lines)
                foreach (LineSegment segment in line.segments)
                {
                    if (index < segment.charStart || index >= segment.charStart + segment.charCount) continue;

                    TextMeasurer.Run run = _runs[segment.runIndex];
                    if (!run.picture || run.floating) return false;

                    float x = line.left;
                    foreach (LineSegment before in line.segments)
                    {
                        if (before.charStart == segment.charStart) break;
                        x += before.width;
                    }
                    x += (index - segment.charStart) * run.imageWidth;

                    box = new LayoutRect(_origin.X + x, _origin.Y + line.baseline - run.imageHeight, run.imageWidth, run.imageHeight);
                    return true;
                }
            return false;
        }

        // An inline picture's drawn rect, unturned and centred in its box, and its turn.
        public bool PictureFrame(int index, out LayoutRect rect, out Quaternion rotation)
        {
            rect = LayoutRect.Empty;
            rotation = Quaternion.Identity;
            if (!PictureBox(index, out LayoutRect box)) return false;

            for (int i = 0; i < _runs.Count; i++)
                if (index >= _runs[i].charStart && index < _runs[i].charStart + _runs[i].charCount)
                {
                    (Vector2 size, rotation) = _runPictures[i];
                    rect = new LayoutRect(box.x + (box.width - size.X) * 0.5f, box.y + (box.height - size.Y) * 0.5f, size.X, size.Y);
                    return true;
                }
            return false;
        }

        // Re-stacks the lines from blockTop, pushing each across page breaks; returns the height.
        internal float Paginate(float blockTop, PageBands bands)
        {
            if (_layout == null) return 0f;

            float y = blockTop;
            foreach (TextLine line in _layout.lines)
            {
                y = bands.Push(y, line.height);
                line.top = y - blockTop;
                y += line.height;
            }
            return y - blockTop;
        }

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

            int picture = PictureAt(e.point);
            if (picture >= 0) target.PicturePressed(this, picture, e.button);
            else target.GlyphPressed(this, IndexAt(e.point));
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
