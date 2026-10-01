using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;

namespace ArctisAurora.Tests
{
    internal static class FontTests
    {
        [A_XSDActionDependency("Fonts.MathFontBaked", "Test")]
        private static IEnumerator<int> MathFontBaked(TestContext t)
        {
            AtlasMetaData atlas = new AtlasMetaData();
            Serializer.DeserializeAttributed(Paths.Font("cambria", "cambria.agd"), ref atlas);
            atlas.BuildCharIndex();

            GlyphMetrics sum = atlas.GetGlyphAndIndex('∑').Item1.regular;
            GlyphMetrics alpha = atlas.GetGlyphAndIndex('α').Item1.regular;
            GlyphMetrics integral = atlas.GetGlyphAndIndex('∫').Item1.regular;
            t.Check(!Same(sum, alpha) && !Same(alpha, integral) && !Same(sum, integral),
                "sum, alpha and integral bake distinct glyphs, not .notdef");

            Glyph x = atlas.GetGlyphAndIndex('x').Item1;
            t.Check(atlas.hasItalic && !Same(x.regular, x.italic), "x has its own italic face");

            MathConstants math = MathConstants.Load(Paths.Font("cambria", "cambria.math.xml"));
            t.Check(math.axisHeight > 0f && math.fractionRuleThickness > 0f && math.fractionNumeratorGapMin > 0f
                && math.scriptPercentScaleDown > 0f, "MATH constants are read and non-zero");
            yield break;
        }

        private static bool Same(GlyphMetrics a, GlyphMetrics b) =>
            a.xMin == b.xMin && a.yMin == b.yMin && a.xMax == b.xMax && a.yMax == b.yMax && a.advanceWidth == b.advanceWidth;
    }
}
