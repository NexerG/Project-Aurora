using ArctisAurora.Core.Commands;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using System.Numerics;

namespace Thorium.Tests
{
    internal static class PaletteTests
    {
        [A_XSDActionDependency("Palette.SetCrossfades", "Test")]
        private static IEnumerator<int> SetCrossfades(TestContext t)
        {
            string? reply = null;
            CommandConsole.Execute("Palettes.Set nosuch", r => reply = r);
            t.Check(reply != null && reply.StartsWith("error:"), "an unknown palette name answers error:");

            PaletteDefinition from = Palettes.Default;
            Vector3 old = Palettes.ColorOf(Palettes.Surface(from, PaletteRole.Ground));
            PaletteDefinition to = Palettes.Names.Select(name => Palettes.Get(name)!)
                .MaxBy(palette => Vector3.Distance(old, Palettes.ColorOf(Palettes.Surface(palette, PaletteRole.Ground))))!;
            uint slot = Palettes.Surface(to, PaletteRole.Ground);
            Vector3 target = Palettes.ColorOf(slot);
            float range = Vector3.Distance(old, target);
            t.Check(range > 0f, "some palette's Ground differs from the default's");
            if (range == 0f) yield break;

            CommandConsole.Execute($"Palettes.Set {to.name}", r => reply = r);
            t.Check(reply == "ok", "a known palette name answers ok");

            float dt = (float)Engine.fixedStep;
            float maxStep = 3f * dt / to.themeFade * range + 1e-4f;
            int ticks = (int)MathF.Ceiling(to.themeFade / dt) + 3;
            Vector3 previous = old;
            bool monotonic = true, bounded = true;
            int switchedAt = -1;
            for (int i = 1; i <= ticks; i++)
            {
                yield return 1;
                if (switchedAt < 0 && Palettes.Default == to) switchedAt = i;

                Vector4 shown = Palettes.Paints.Backing<GpuPaint>()[slot].color;
                Vector3 sample = new Vector3(shown.X, shown.Y, shown.Z);
                if (Vector3.Distance(sample, target) > Vector3.Distance(previous, target) + 1e-5f) monotonic = false;
                if (Vector3.Distance(sample, previous) > maxStep) bounded = false;
                previous = sample;
            }

            t.Check(switchedAt > 0 && switchedAt <= 3, "the default becomes the new palette within 3 ticks");
            t.Check(monotonic, "the Ground slot never moves away from the new colour");
            t.Check(bounded, "no tick moves the Ground slot further than CubicInOut's peak slope allows");
            t.Check(previous == target, "the Ground slot ends exactly on the new palette's colour");

            CommandConsole.Execute($"Palettes.Set {from.name}", r => reply = r);
            yield return ticks;
            t.Check(Palettes.Default == from, "switching back restores the original palette for the suites after this one");
        }
    }
}
