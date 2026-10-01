using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;

namespace ArctisAurora.Tests
{
    internal static class MathTests
    {
        private static AtlasMetaData atlas;
        private static MathConstants constants;

        private static void LoadFont()
        {
            if (atlas != null) return;
            atlas = new AtlasMetaData();
            Serializer.DeserializeAttributed(Paths.Font("cambria", "cambria.agd"), ref atlas);
            atlas.BuildCharIndex();
            constants = MathConstants.Load(Paths.Font("cambria", "cambria.math.xml"));
        }

        private static MathBox Lay(string tex, bool display = false)
        {
            LoadFont();
            return MathLayout.Layout(MathParser.Parse(tex), display, atlas, constants);
        }

        private static MathGlyph Find(MathBox box, char ch) => box.glyphs.First(g => g.ch == ch);

        // a placed glyph's ink, bottom and top, in the box's coordinates
        private static (float bottom, float top) InkOf(MathGlyph g)
        {
            GlyphMetrics m = atlas.GetGlyph(g.ch).Metrics(atlas.Effective(g.face));
            int range = m.yMax - m.yMin;
            return (g.y + m.glyphHeight * m.yMin / range * g.scale, g.y + m.glyphHeight * m.yMax / range * g.scale);
        }

        [A_XSDActionDependency("Math.SymbolsInCharset", "Test")]
        private static IEnumerator<int> SymbolsInCharset(TestContext t)
        {
            LoadFont();
            List<char> chars = new List<char>();
            chars.AddRange(MathSymbols.commands.Values.Select(v => v.ch));
            chars.AddRange(MathSymbols.bigOps.Values.Select(v => v.ch));
            chars.AddRange(MathSymbols.accents.Values);
            chars.AddRange(MathSymbols.blackboard.Values);
            chars.AddRange(MathSymbols.delimiters.Values);
            foreach (var set in MathLayout.pieces.Values)
                chars.AddRange(new[] { set.top, set.ext, set.bottom });
            chars.AddRange("√⎷′−∗");

            string missing = new string(chars.Where(c => c != '\0' && atlas.GetGlyph(c) == null).Distinct().ToArray());
            t.Check(missing.Length == 0, $"every symbol is in the Math charset (missing {missing.Length})");
            yield break;
        }

        [A_XSDActionDependency("Math.ParseNeverThrows", "Test")]
        private static IEnumerator<int> ParseNeverThrows(TestContext t)
        {
            string[] invalid = { @"\frac{a", "}", @"\foo", "x^a^b", "^", @"\left(", @"\right)", "a & b", @"\sqrt", @"\\", @"\left( x \right" };
            foreach (string tex in invalid)
            {
                MathNode node = MathParser.Parse(tex);
                t.Check(node is MathError && Lay(tex).error, $"'{tex}' is an error formula");
            }

            string[] valid = { "", "x^2", @"\frac{a}{b}", @"\sqrt[3]{x}", @"\left(\frac{1}{2}\right)", @"\sum_{i=1}^{n} i",
                               @"\text{if } x", @"\mathbb{R}", "f'(x)", @"\hat{a}", @"a \, b", @"\left. x \right|", @"\lim_{x \to 0}" };
            foreach (string tex in valid)
                t.Check(MathParser.Parse(tex) is not MathError && !Lay(tex, true).error, $"'{tex}' parses");
            yield break;
        }

        [A_XSDActionDependency("Math.SuperscriptRaises", "Test")]
        private static IEnumerator<int> SuperscriptRaises(TestContext t)
        {
            MathBox x = Lay("x");
            MathBox squared = Lay("x^2");
            MathGlyph two = Find(squared, '2');
            t.Check(squared.height > x.height, "x^2 is taller than x");
            t.Check(two.y > 0f, "the 2 sits above the baseline");
            t.Check(MathF.Abs(two.scale - constants.scriptPercentScaleDown / 100f) < 1e-4f, "the 2 is at script scale");
            yield break;
        }

        [A_XSDActionDependency("Math.SubscriptBelowBaseline", "Test")]
        private static IEnumerator<int> SubscriptBelowBaseline(TestContext t)
        {
            MathBox box = Lay("x_i");
            t.Check(Find(box, 'i').y < 0f, "the i sits below the baseline");
            t.Check(box.depth > 0f, "the box has depth");
            yield break;
        }

        [A_XSDActionDependency("Math.FractionOnAxis", "Test")]
        private static IEnumerator<int> FractionOnAxis(TestContext t)
        {
            MathBox box = Lay(@"\frac{a}{b}");
            t.Check(box.rules.Count == 1, "one rule");
            MathRule rule = box.rules[0];
            t.Check(MathF.Abs(rule.y - rule.height * 0.5f - constants.axisHeight) < 1e-4f, "the rule is centred on the axis");

            MathGlyph a = Find(box, 'a'), b = Find(box, 'b');
            t.Check(a.y > rule.y && b.y < rule.y - rule.height, "numerator above the rule, denominator below");
            t.Check(a.x >= rule.x && b.x >= rule.x && a.x < rule.x + rule.width && b.x < rule.x + rule.width,
                "both sit within the rule's width");
            t.Check(box.depth > 0f, "the fraction has depth");
            yield break;
        }

        [A_XSDActionDependency("Math.BigOpDisplay", "Test")]
        private static IEnumerator<int> BigOpDisplay(TestContext t)
        {
            MathBox display = Lay(@"\sum_{i=1}^{n}", true);
            MathBox inline = Lay(@"\sum_{i=1}^{n}");
            t.Check(Find(display, '∑').scale > Find(inline, '∑').scale, "the sum is larger in display style");

            MathGlyph displaySum = Find(display, '∑');
            float displayOp = Lay(@"\sum", true).width;
            t.Check(Find(display, 'n').x < displayOp && Find(display, 'n').y > displaySum.y,
                "display limits sit above, within the operator's width");
            t.Check(Find(inline, 'n').x >= Lay(@"\sum").width - 1e-4f, "inline limits sit to the right");
            yield break;
        }

        [A_XSDActionDependency("Math.DelimitersGrow", "Test")]
        private static IEnumerator<int> DelimitersGrow(TestContext t)
        {
            foreach (string body in new[] { @"\frac{a}{b}", @"\frac{\frac{a}{b}}{\frac{\frac{c}{d}}{e}}" })
            {
                MathBox inner = Lay(body, true);
                MathBox box = Lay(@"\left(" + body + @"\right)", true);
                float bottom = float.MaxValue, top = float.MinValue;
                foreach (MathGlyph g in box.glyphs.Where(g => "(⎛⎜⎝".Contains(g.ch)))
                {
                    (float gb, float gt) = InkOf(g);
                    bottom = MathF.Min(bottom, gb);
                    top = MathF.Max(top, gt);
                }
                t.Check(top - bottom >= 0.9f * (inner.height + inner.depth), $"the paren covers {body}");
            }
            yield break;
        }

        [A_XSDActionDependency("Math.SqrtCoversBody", "Test")]
        private static IEnumerator<int> SqrtCoversBody(TestContext t)
        {
            MathBox x = Lay("x");
            MathBox box = Lay(@"\sqrt{x}");
            t.Check(box.rules.Count >= 1 && box.rules[^1].y - box.rules[^1].height >= x.height, "the bar is above the x");
            t.Check(box.width > x.width, "the radical is wider than its body");
            yield break;
        }

        [A_XSDActionDependency("Math.Spacing", "Test")]
        private static IEnumerator<int> Spacing(TestContext t)
        {
            float ab = Lay("ab").width, plus = Lay("a+b").width - Lay("+").width, equals = Lay("a=b").width - Lay("=").width;
            t.Check(ab < plus && plus < equals, "ab < a+b < a=b once the operator itself is taken out");
            t.Check(Lay("-a").width < Lay("a-a").width - Lay("a").width, "a unary minus takes no binary space");
            yield break;
        }

        [A_XSDActionDependency("Math.Classes", "Test")]
        private static IEnumerator<int> Classes(TestContext t)
        {
            MathSymbol First(string tex) => (MathSymbol)((MathList)MathParser.Parse(tex)).items[0];

            MathSymbol alpha = First(@"\alpha"), gamma = First(@"\Gamma"), le = First(@"\le");
            t.Check(alpha.ch == 'α' && alpha.face == FontStyle.Italic, "alpha is an italic α");
            t.Check(gamma.ch == 'Γ' && gamma.face == FontStyle.Regular, "Gamma is an upright Γ");
            t.Check(le.ch == '≤' && le.cls == MathClass.Rel, "le is a ≤ relation");
            yield break;
        }
    }
}
