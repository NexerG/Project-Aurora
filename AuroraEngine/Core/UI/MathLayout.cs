using ArctisAurora.Core.Filing;

namespace ArctisAurora.Core.UI
{
    public enum MathStyle { Display, Text, Script, ScriptScript }

    // one glyph of a laid-out formula; em at font size 1, y up from the baseline
    public readonly struct MathGlyph
    {
        public readonly char ch;
        public readonly FontStyle face;
        public readonly float x;
        public readonly float y;
        public readonly float scale;

        public MathGlyph(char ch, FontStyle face, float x, float y, float scale)
        {
            this.ch = ch;
            this.face = face;
            this.x = x;
            this.y = y;
            this.scale = scale;
        }
    }

    // a filled rectangle; y is its top edge
    public readonly struct MathRule
    {
        public readonly float x;
        public readonly float y;
        public readonly float width;
        public readonly float height;

        public MathRule(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    public sealed class MathBox
    {
        public float width;
        public float height;
        public float depth;
        public readonly List<MathGlyph> glyphs = new List<MathGlyph>();
        public readonly List<MathRule> rules = new List<MathRule>();
        public bool error;

        // italic correction of a single-glyph box
        internal float italic;

        // Copies another box's contents in, offset.
        internal void Place(MathBox child, float dx, float dy)
        {
            foreach (MathGlyph g in child.glyphs)
                glyphs.Add(new MathGlyph(g.ch, g.face, g.x + dx, g.y + dy, g.scale));
            foreach (MathRule r in child.rules)
                rules.Add(new MathRule(r.x + dx, r.y + dy, r.width, r.height));
            height = MathF.Max(height, child.height + dy);
            depth = MathF.Max(depth, child.depth - dy);
        }
    }

    public static class MathLayout
    {
        // TeX's 1.2pt at 10pt, either side of a fraction or delimiter pair
        private const float nullDelimiterSpace = 0.12f;
        private const float delimiterFactor = 0.901f;
        private const float delimiterShortfall = 0.5f;
        private const float maxDelimiterScale = 2.4f;
        private const float maxRadicalScale = 2f;

        // stretchy delimiter pieces: top, extender, middle, bottom ('\0' = none)
        internal static readonly Dictionary<char, (char top, char ext, char mid, char bottom)> pieces = new()
        {
            ['('] = ('⎛', '⎜', '\0', '⎝'), [')'] = ('⎞', '⎟', '\0', '⎠'),
            ['['] = ('⎡', '⎢', '\0', '⎣'), [']'] = ('⎤', '⎥', '\0', '⎦'),
            ['{'] = ('⎧', '⎪', '⎨', '⎩'), ['}'] = ('⎫', '⎪', '⎬', '⎭'),
            ['⌈'] = ('⎡', '⎢', '\0', '⎢'), ['⌉'] = ('⎤', '⎥', '\0', '⎥'),
            ['⌊'] = ('⎢', '⎢', '\0', '⎣'), ['⌋'] = ('⎥', '⎥', '\0', '⎦'),
        };

        // inter-atom spacing in mu, rows left class, columns right class; negative only outside script styles
        private static readonly int[,] spacing =
        {
            //        Ord Op Bin Rel Open Close Punct Inner
            /*Ord*/  { 0,  3, -4, -5,  0,  0,  0, -3 },
            /*Op*/   { 3,  3,  0, -5,  0,  0,  0, -3 },
            /*Bin*/  {-4, -4,  0,  0, -4,  0,  0, -4 },
            /*Rel*/  {-5, -5,  0,  0, -5,  0,  0, -5 },
            /*Open*/ { 0,  0,  0,  0,  0,  0,  0,  0 },
            /*Close*/{ 0,  3, -4, -5,  0,  0,  0, -3 },
            /*Punct*/{-3, -3,  0, -3, -3, -3, -3, -3 },
            /*Inner*/{-3,  3, -4, -5, -3,  0, -3, -3 },
        };

        // Lays out a parsed formula, in em at font size 1.
        public static MathBox Layout(MathNode root, bool display, AtlasMetaData atlas, MathConstants constants)
        {
            Layouter layouter = new Layouter(atlas, constants);
            if (root is MathError error)
            {
                MathBox box = layouter.Text(error.source, FontStyle.Regular, 1f);
                box.error = true;
                return box;
            }
            return layouter.Node(root, display ? MathStyle.Display : MathStyle.Text, false);
        }

        private sealed class Layouter
        {
            private readonly AtlasMetaData atlas;
            private readonly MathConstants c;

            public Layouter(AtlasMetaData atlas, MathConstants constants)
            {
                this.atlas = atlas;
                c = constants;
            }

            private float Scale(MathStyle style) => style switch
            {
                MathStyle.Script => c.scriptPercentScaleDown / 100f,
                MathStyle.ScriptScript => c.scriptScriptPercentScaleDown / 100f,
                _ => 1f
            };

            private static MathStyle Smaller(MathStyle style) => style switch
            {
                MathStyle.Display => MathStyle.Text,
                MathStyle.Text => MathStyle.Script,
                _ => MathStyle.ScriptScript
            };

            private static MathStyle ScriptStyle(MathStyle style) =>
                style <= MathStyle.Text ? MathStyle.Script : MathStyle.ScriptScript;

            public MathBox Node(MathNode node, MathStyle style, bool cramped) => node switch
            {
                MathList list => List(list.items, style, cramped),
                MathSymbol symbol => Symbol(symbol, style),
                MathText text => Text(text.text, text.face, Scale(style)),
                MathScripts scripts => Scripts(scripts, style, cramped),
                MathFraction fraction => Fraction(fraction, style, cramped),
                MathRadical radical => Radical(radical, style),
                MathDelimited delimited => Delimited(delimited, style, cramped),
                MathAccent accent => Accent(accent, style),
                MathSpace space => new MathBox { width = space.em * Scale(style) },
                _ => new MathBox()
            };

            #region glyphs

            // One glyph at a scale, sitting on the baseline.
            private MathBox Glyph(char ch, FontStyle face, float scale)
            {
                MathBox box = new MathBox();
                Glyph glyph = atlas.GetGlyph(ch);
                if (glyph == null)
                {
                    glyph = atlas.GetGlyph(' ');
                    ch = ' ';
                    if (glyph == null) return box;
                }

                GlyphMetrics m = glyph.Metrics(atlas.Effective(face));
                int range = m.yMax - m.yMin;
                box.width = m.advanceWidth * scale;
                if (range != 0)
                {
                    box.height = MathF.Max(0f, m.glyphHeight * m.yMax / range * scale);
                    box.depth = MathF.Max(0f, -m.glyphHeight * m.yMin / range * scale);
                }
                box.italic = MathF.Max(0f, (m.leftSideOffset + m.glyphWidth - m.advanceWidth) * scale);
                box.glyphs.Add(new MathGlyph(ch, face, 0f, 0f, scale));
                return box;
            }

            // A glyph's ink extent: bottom and top relative to its baseline, left and width, at a scale.
            private (float bottom, float top, float left, float width) Ink(char ch, FontStyle face, float scale)
            {
                Glyph glyph = atlas.GetGlyph(ch);
                if (glyph == null) return (0f, 0f, 0f, 0f);
                GlyphMetrics m = glyph.Metrics(atlas.Effective(face));
                int range = m.yMax - m.yMin;
                if (range == 0) return (0f, 0f, m.leftSideOffset * scale, 0f);
                return (m.glyphHeight * m.yMin / range * scale, m.glyphHeight * m.yMax / range * scale,
                        m.leftSideOffset * scale, m.glyphWidth * scale);
            }

            public MathBox Text(string text, FontStyle face, float scale)
            {
                MathBox box = new MathBox();
                foreach (char ch in text)
                {
                    MathBox glyph = Glyph(ch, face, scale);
                    box.Place(glyph, box.width, 0f);
                    box.width += glyph.width;
                }
                return box;
            }

            private MathBox Symbol(MathSymbol symbol, MathStyle style)
            {
                float k = Scale(style);
                if (!symbol.bigOp) return Glyph(symbol.ch, symbol.face, k);

                MathBox natural = Glyph(symbol.ch, symbol.face, k);
                float total = natural.height + natural.depth;
                float grow = style == MathStyle.Display && total > 0f
                    ? MathF.Max(1f, c.displayOperatorMinHeight * k / total) : 1f;
                MathBox op = grow == 1f ? natural : Glyph(symbol.ch, symbol.face, k * grow);
                return OnAxis(op, k);
            }

            // Shifts a box so it is centred on the math axis.
            private MathBox OnAxis(MathBox inner, float k)
            {
                float shift = c.axisHeight * k - (inner.height - inner.depth) * 0.5f;
                MathBox box = new MathBox { width = inner.width, italic = inner.italic };
                box.Place(inner, 0f, shift);
                return box;
            }

            #endregion

            #region lists

            private static MathClass? ClassOf(MathNode node) => node switch
            {
                MathSpace => null,
                MathSymbol symbol => symbol.cls,
                MathText text => text.cls,
                MathScripts scripts => ClassOf(scripts.nucleus) ?? MathClass.Ord,
                MathFraction => MathClass.Inner,
                MathDelimited => MathClass.Inner,
                _ => MathClass.Ord
            };

            private MathBox List(List<MathNode> items, MathStyle style, bool cramped)
            {
                // classes, with binary operators demoted where TeX demotes them
                MathClass?[] classes = new MathClass?[items.Count];
                int previous = -1;
                for (int i = 0; i < items.Count; i++)
                {
                    classes[i] = ClassOf(items[i]);
                    if (classes[i] == null) continue;

                    MathClass? before = previous < 0 ? null : classes[previous];
                    if (classes[i] == MathClass.Bin && (before == null || before == MathClass.Bin || before == MathClass.Op
                        || before == MathClass.Rel || before == MathClass.Open || before == MathClass.Punct))
                        classes[i] = MathClass.Ord;
                    if (before == MathClass.Bin && (classes[i] == MathClass.Rel || classes[i] == MathClass.Close
                        || classes[i] == MathClass.Punct))
                        classes[previous] = MathClass.Ord;
                    previous = i;
                }
                if (previous >= 0 && classes[previous] == MathClass.Bin) classes[previous] = MathClass.Ord;

                // boxes, spaced
                MathBox box = new MathBox();
                float mu = Scale(style) / 18f;
                bool script = style >= MathStyle.Script;
                previous = -1;
                for (int i = 0; i < items.Count; i++)
                {
                    if (classes[i] != null)
                    {
                        if (previous >= 0)
                        {
                            int space = spacing[(int)classes[previous], (int)classes[i]];
                            if (space > 0 || (space < 0 && !script)) box.width += Math.Abs(space) * mu;
                        }
                        previous = i;
                    }

                    MathBox child = Node(items[i], style, cramped);
                    box.Place(child, box.width, 0f);
                    box.width += child.width;
                    if (items[i] is MathSymbol { bigOp: false }) box.width += child.italic;
                }
                return box;
            }

            #endregion

            #region scripts

            private MathBox Scripts(MathScripts scripts, MathStyle style, bool cramped)
            {
                float k = Scale(style);
                bool display = style == MathStyle.Display;
                bool limits = display && (scripts.nucleus is MathSymbol { bigOp: true, limits: true }
                                          || scripts.nucleus is MathText { limits: true });

                MathBox nucleus = Node(scripts.nucleus, style, cramped);
                MathStyle scriptStyle = ScriptStyle(style);
                MathBox sup = scripts.sup == null ? null : Node(scripts.sup, scriptStyle, cramped);
                MathBox sub = scripts.sub == null ? null : Node(scripts.sub, scriptStyle, true);

                return limits ? Limits(nucleus, sup, sub, k) : Attach(scripts.nucleus, nucleus, sup, sub, cramped, k);
            }

            private MathBox Limits(MathBox nucleus, MathBox sup, MathBox sub, float k)
            {
                float width = MathF.Max(nucleus.width, MathF.Max(sup?.width ?? 0f, sub?.width ?? 0f));
                MathBox box = new MathBox { width = width };
                box.Place(nucleus, (width - nucleus.width) * 0.5f, 0f);
                if (sup != null)
                {
                    float rise = MathF.Max(c.upperLimitGapMin * k + sup.depth, c.upperLimitBaselineRiseMin * k);
                    box.Place(sup, (width - sup.width) * 0.5f, nucleus.height + rise);
                }
                if (sub != null)
                {
                    float drop = MathF.Max(c.lowerLimitGapMin * k + sub.height, c.lowerLimitBaselineDropMin * k);
                    box.Place(sub, (width - sub.width) * 0.5f, -(nucleus.depth + drop));
                }
                return box;
            }

            private MathBox Attach(MathNode nucleusNode, MathBox nucleus, MathBox sup, MathBox sub, bool cramped, float k)
            {
                bool character = nucleusNode is MathSymbol { bigOp: false };
                float u = 0f, v = 0f;

                if (sup != null)
                {
                    u = MathF.Max((cramped ? c.superscriptShiftUpCramped : c.superscriptShiftUp) * k,
                                  sup.depth + c.superscriptBottomMin * k);
                    if (!character) u = MathF.Max(u, nucleus.height - c.superscriptBaselineDropMax * k);
                }
                if (sub != null)
                {
                    v = c.subscriptShiftDown * k;
                    if (!character) v = MathF.Max(v, nucleus.depth + c.subscriptBaselineDropMin * k);
                    if (sup == null) v = MathF.Max(v, sub.height - c.subscriptTopMax * k);
                }
                if (sup != null && sub != null)
                {
                    float gap = (u - sup.depth) - (sub.height - v);
                    float gapMin = c.subSuperscriptGapMin * k;
                    if (gap < gapMin) v += gapMin - gap;

                    float lift = c.superscriptBottomMaxWithSubscript * k - (u - sup.depth);
                    if (lift > 0f)
                    {
                        u += lift;
                        v -= lift;
                    }
                }

                MathBox box = new MathBox();
                box.Place(nucleus, 0f, 0f);
                float end = nucleus.width;
                if (sup != null)
                {
                    box.Place(sup, nucleus.width + nucleus.italic, u);
                    end = MathF.Max(end, nucleus.width + nucleus.italic + sup.width);
                }
                if (sub != null)
                {
                    box.Place(sub, nucleus.width, -v);
                    end = MathF.Max(end, nucleus.width + sub.width);
                }
                box.width = end + c.spaceAfterScript * k;
                return box;
            }

            #endregion

            #region fractions

            private MathBox Fraction(MathFraction fraction, MathStyle style, bool cramped)
            {
                if (fraction.style is MathStyle forced) style = forced;
                float k = Scale(style);
                bool display = style == MathStyle.Display;

                MathBox num = Node(fraction.num, Smaller(style), cramped);
                MathBox den = Node(fraction.den, Smaller(style), true);

                float t = c.fractionRuleThickness * k;
                float axis = c.axisHeight * k;
                float shiftUp = (display ? c.fractionNumeratorDisplayStyleShiftUp : c.fractionNumeratorShiftUp) * k;
                float shiftDown = (display ? c.fractionDenominatorDisplayStyleShiftDown : c.fractionDenominatorShiftDown) * k;
                float gapNum = (display ? c.fractionNumDisplayStyleGapMin : c.fractionNumeratorGapMin) * k;
                float gapDen = (display ? c.fractionDenomDisplayStyleGapMin : c.fractionDenominatorGapMin) * k;

                shiftUp = MathF.Max(shiftUp, axis + t * 0.5f + gapNum + num.depth);
                shiftDown = MathF.Max(shiftDown, gapDen + t * 0.5f - axis + den.height);

                float pad = nullDelimiterSpace * k;
                float inner = MathF.Max(num.width, den.width);
                MathBox box = new MathBox { width = inner + 2f * pad };
                box.Place(num, pad + (inner - num.width) * 0.5f, shiftUp);
                box.Place(den, pad + (inner - den.width) * 0.5f, -shiftDown);
                box.rules.Add(new MathRule(pad, axis + t * 0.5f, inner, t));
                return box;
            }

            #endregion

            #region radicals

            private MathBox Radical(MathRadical radical, MathStyle style)
            {
                float k = Scale(style);
                MathBox body = Node(radical.body, style, true);

                float t = c.radicalRuleThickness * k;
                float gap = (style == MathStyle.Display ? c.radicalDisplayStyleVerticalGap : c.radicalVerticalGap) * k;
                float needed = body.height + body.depth + gap + t;

                (float inkBottom, float inkTop, _, _) = Ink('√', FontStyle.Regular, k);
                float natural = inkTop - inkBottom;

                MathBox sign = new MathBox();
                float signTop, signBottom;
                if (natural > 0f && needed <= natural * maxRadicalScale)
                {
                    float grow = MathF.Max(1f, needed / natural);
                    MathBox glyph = Glyph('√', FontStyle.Regular, k * grow);
                    float excess = natural * grow - needed;
                    signTop = body.height + gap + t + excess * 0.5f;
                    sign.width = glyph.width;
                    sign.Place(glyph, 0f, signTop - inkTop * grow);
                    signBottom = signTop - natural * grow;
                }
                else
                {
                    // ⎷ for the hook, a rule for the rest of the stem
                    (float hookBottom, float hookTop, float hookLeft, float hookWidth) = Ink('⎷', FontStyle.Regular, k);
                    MathBox hook = Glyph('⎷', FontStyle.Regular, k);
                    signTop = body.height + gap + t;
                    signBottom = signTop - needed;
                    sign.width = hookLeft + hookWidth;
                    sign.Place(hook, 0f, signBottom - hookBottom);
                    float stemTop = signTop;
                    float stemBottom = signBottom + (hookTop - hookBottom);
                    if (stemTop > stemBottom)
                        sign.rules.Add(new MathRule(hookLeft + hookWidth - t, stemTop, t, stemTop - stemBottom));
                }

                MathBox box = new MathBox();
                float x = 0f;
                if (radical.degree != null)
                {
                    MathBox degree = Node(radical.degree, MathStyle.ScriptScript, false);
                    float raise = c.radicalDegreeBottomRaisePercent / 100f * (signTop - signBottom);
                    float before = c.radicalKernBeforeDegree * k;
                    box.Place(degree, before, signBottom + raise + degree.depth);
                    x = MathF.Max(0f, before + degree.width + c.radicalKernAfterDegree * k);
                }

                box.Place(sign, x, 0f);
                x += sign.width;
                box.Place(body, x, 0f);
                box.rules.Add(new MathRule(x, signTop, body.width, t));
                box.width = x + body.width;
                box.height = MathF.Max(box.height, signTop + c.radicalExtraAscender * k);
                return box;
            }

            #endregion

            #region delimiters

            private MathBox Delimited(MathDelimited delimited, MathStyle style, bool cramped)
            {
                float k = Scale(style);
                MathBox body = Node(delimited.body, style, cramped);

                float axis = c.axisHeight * k;
                float half = MathF.Max(body.height - axis, body.depth + axis);
                float target = MathF.Max(2f * half * delimiterFactor, 2f * half - delimiterShortfall * k);

                MathBox left = Delimiter(delimited.left, target, k);
                MathBox right = Delimiter(delimited.right, target, k);

                MathBox box = new MathBox();
                box.Place(left, 0f, 0f);
                box.Place(body, left.width, 0f);
                box.Place(right, left.width + body.width, 0f);
                box.width = left.width + body.width + right.width;
                return box;
            }

            // A delimiter at least `target` tall, centred on the axis.
            private MathBox Delimiter(char ch, float target, float k)
            {
                if (ch == '\0') return new MathBox { width = nullDelimiterSpace * k };

                float axis = c.axisHeight * k;
                (float inkBottom, float inkTop, _, _) = Ink(ch, FontStyle.Regular, k);
                float natural = inkTop - inkBottom;
                MathBox box = new MathBox();

                if (ch == '|' || ch == '‖')
                {
                    if (natural >= target) return OnAxis(Glyph(ch, FontStyle.Regular, k), k);

                    MathBox bar = Glyph('|', FontStyle.Regular, k);
                    (_, _, float barLeft, float barWidth) = Ink('|', FontStyle.Regular, k);
                    int count = ch == '‖' ? 2 : 1;
                    for (int i = 0; i < count; i++)
                        box.rules.Add(new MathRule(barLeft + i * bar.width * 0.5f, axis + target * 0.5f, barWidth, target));
                    box.width = bar.width * (count == 2 ? 1.5f : 1f);
                    box.height = axis + target * 0.5f;
                    box.depth = target * 0.5f - axis;
                    return box;
                }

                if (natural <= 0f || target <= natural) return OnAxis(Glyph(ch, FontStyle.Regular, k), k);

                if (target <= natural * maxDelimiterScale || !pieces.TryGetValue(ch, out var set))
                    return OnAxis(Glyph(ch, FontStyle.Regular, k * target / natural), k);

                float top = axis + target * 0.5f;
                float bottom = axis - target * 0.5f;

                (float topBottom, float topTop, _, _) = Ink(set.top, FontStyle.Regular, k);
                (float botBottom, float botTop, _, _) = Ink(set.bottom, FontStyle.Regular, k);
                (float extBottom, float extTop, _, _) = Ink(set.ext, FontStyle.Regular, k);

                PlaceInk(box, set.top, k, top - topTop);
                PlaceInk(box, set.bottom, k, bottom - botBottom);

                float upper = top - (topTop - topBottom);
                float lower = bottom + (botTop - botBottom);
                if (set.mid != '\0')
                {
                    (float midBottom, float midTop, _, _) = Ink(set.mid, FontStyle.Regular, k);
                    float midHalf = (midTop - midBottom) * 0.5f;
                    PlaceInk(box, set.mid, k, axis - midHalf - midBottom);
                    Extend(box, set.ext, k, axis + midHalf, upper, extBottom, extTop);
                    Extend(box, set.ext, k, lower, axis - midHalf, extBottom, extTop);
                }
                else
                {
                    Extend(box, set.ext, k, lower, upper, extBottom, extTop);
                }

                box.width = Glyph(set.top, FontStyle.Regular, k).width;
                box.height = top;
                box.depth = -bottom;
                return box;
            }

            private void PlaceInk(MathBox box, char ch, float k, float baseline)
            {
                box.glyphs.Add(new MathGlyph(ch, FontStyle.Regular, 0f, baseline, k));
            }

            // Repeats an extender, overlapping, to fill [from, to].
            private void Extend(MathBox box, char ext, float k, float from, float to, float extBottom, float extTop)
            {
                float span = to - from;
                float piece = extTop - extBottom;
                if (span <= 0f || piece <= 0f) return;

                int count = Math.Max(1, (int)MathF.Ceiling(span / (piece * 0.9f)));
                float step = count == 1 ? 0f : (span - piece) / (count - 1);
                for (int i = 0; i < count; i++)
                {
                    float inkBottom = count == 1 ? from + (span - piece) * 0.5f : from + i * step;
                    PlaceInk(box, ext, k, inkBottom - extBottom);
                }
            }

            #endregion

            #region accents

            private MathBox Accent(MathAccent accent, MathStyle style)
            {
                float k = Scale(style);
                MathBox body = Node(accent.body, style, true);
                MathBox box = new MathBox { width = body.width };
                box.Place(body, 0f, 0f);

                if (accent.bar)
                {
                    float t = c.overbarRuleThickness * k;
                    float top = body.height + c.overbarVerticalGap * k + t;
                    box.rules.Add(new MathRule(0f, top, body.width, t));
                    box.height = MathF.Max(box.height, top + c.overbarExtraAscender * k);
                    return box;
                }

                (_, float inkTop, float inkLeft, float inkWidth) = Ink(accent.accent, FontStyle.Regular, k);
                float lift = MathF.Max(0f, body.height - c.accentBaseHeight * k);
                float x = body.width * 0.5f - (inkLeft + inkWidth * 0.5f);
                box.glyphs.Add(new MathGlyph(accent.accent, FontStyle.Regular, x, lift, k));
                box.height = MathF.Max(box.height, lift + inkTop);
                return box;
            }

            #endregion
        }
    }
}
