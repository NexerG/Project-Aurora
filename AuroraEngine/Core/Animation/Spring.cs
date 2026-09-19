using System.Numerics;

namespace ArctisAurora.Core.Animation
{
    // A damped spring toward a target, stepped with the exact solution so any dt is stable.
    public static class Spring
    {
        // frequency in Hz; dampingRatio 1 is critical, below 1 overshoots, above 1 creeps.
        public static void Step(ref Vector4 x, ref Vector4 v, Vector4 target, float frequency, float dampingRatio, float dt)
        {
            float omega = 2f * MathF.PI * frequency;
            float zeta = dampingRatio;
            Vector4 d = x - target;

            if (MathF.Abs(zeta - 1f) < 1e-4f)
            {
                float e = MathF.Exp(-omega * dt);
                Vector4 b = v + omega * d;
                x = target + e * (d + b * dt);
                v = e * (v - omega * dt * b);
            }
            else if (zeta < 1f)
            {
                float wd = omega * MathF.Sqrt(1f - zeta * zeta);
                float e = MathF.Exp(-zeta * omega * dt);
                float cos = MathF.Cos(wd * dt);
                float sin = MathF.Sin(wd * dt);
                Vector4 b = (v + zeta * omega * d) / wd;
                Vector4 nextV = e * (v * cos - (zeta * omega * v + omega * omega * d) / wd * sin);
                x = target + e * (d * cos + b * sin);
                v = nextV;
            }
            else
            {
                float root = MathF.Sqrt(zeta * zeta - 1f);
                float r1 = -omega * (zeta - root);
                float r2 = -omega * (zeta + root);
                Vector4 c2 = (v - r1 * d) / (r2 - r1);
                Vector4 c1 = d - c2;
                float e1 = MathF.Exp(r1 * dt);
                float e2 = MathF.Exp(r2 * dt);
                x = target + c1 * e1 + c2 * e2;
                v = c1 * (r1 * e1) + c2 * (r2 * e2);
            }
        }
    }
}
