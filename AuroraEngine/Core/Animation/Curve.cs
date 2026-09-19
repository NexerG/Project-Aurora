namespace ArctisAurora.Core.Animation
{
    public enum EaseKind : byte
    {
        Linear,
        SineIn, SineOut, SineInOut,
        QuadIn, QuadOut, QuadInOut,
        CubicIn, CubicOut, CubicInOut,
        QuartIn, QuartOut, QuartInOut,
        QuintIn, QuintOut, QuintInOut,
        ExpoIn, ExpoOut, ExpoInOut,
        CircIn, CircOut, CircInOut,
        BackIn, BackOut, BackInOut,
        ElasticIn, ElasticOut, ElasticInOut,
        BounceIn, BounceOut, BounceInOut,
        CubicBezier,
        Steps
    }

    // An easing: a named kind, a CSS cubic-bezier, or a step count.
    public struct Curve
    {
        public EaseKind kind;
        // CubicBezier control points
        public float x1, y1, x2, y2;
        // Steps count
        public int steps;

        public static Curve Ease(EaseKind kind) => new Curve { kind = kind };
        public static Curve Bezier(float x1, float y1, float x2, float y2) => new Curve { kind = EaseKind.CubicBezier, x1 = x1, y1 = y1, x2 = x2, y2 = y2 };
        public static Curve Stepped(int steps) => new Curve { kind = EaseKind.Steps, steps = steps };

        // Eased progress for t in 0..1.
        public static float Evaluate(in Curve curve, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            switch (curve.kind)
            {
                case EaseKind.Linear: return t;
                case EaseKind.CubicBezier: return Bezier(curve, t);
                case EaseKind.Steps: return t >= 1f ? 1f : MathF.Floor(t * Math.Max(curve.steps, 1)) / Math.Max(curve.steps, 1);
            }

            int family = ((int)curve.kind - 1) / 3;
            switch (((int)curve.kind - 1) % 3)
            {
                case 0: return In(family, t);
                case 1: return 1f - In(family, 1f - t);
                default: return t < 0.5f ? In(family, 2f * t) * 0.5f : 1f - In(family, 2f - 2f * t) * 0.5f;
            }
        }

        // The ease-in form of each family, in EaseKind order.
        private static float In(int family, float t)
        {
            const float c1 = 1.70158f;
            const float c4 = 2f * MathF.PI / 3f;
            switch (family)
            {
                case 0: return 1f - MathF.Cos(t * MathF.PI * 0.5f);
                case 1: return t * t;
                case 2: return t * t * t;
                case 3: return t * t * t * t;
                case 4: return t * t * t * t * t;
                case 5: return t == 0f ? 0f : MathF.Pow(2f, 10f * t - 10f);
                case 6: return 1f - MathF.Sqrt(1f - t * t);
                case 7: return (c1 + 1f) * t * t * t - c1 * t * t;
                case 8: return t == 0f || t == 1f ? t : -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((10f * t - 10.75f) * c4);
                default: return 1f - BounceOut(1f - t);
            }
        }

        private static float BounceOut(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        // Solves x(s) = t on the bezier, then returns y(s).
        private static float Bezier(in Curve c, float t)
        {
            float s = t;
            for (int i = 0; i < 8; i++)
            {
                float error = Cubic(c.x1, c.x2, s) - t;
                if (MathF.Abs(error) < 1e-6f) return Cubic(c.y1, c.y2, s);
                float slope = Slope(c.x1, c.x2, s);
                if (MathF.Abs(slope) < 1e-6f) break;
                s -= error / slope;
            }

            float lo = 0f, hi = 1f;
            s = t;
            for (int i = 0; i < 32; i++)
            {
                float x = Cubic(c.x1, c.x2, s);
                if (MathF.Abs(x - t) < 1e-6f) break;
                if (x < t) lo = s; else hi = s;
                s = (lo + hi) * 0.5f;
            }
            return Cubic(c.y1, c.y2, s);
        }

        private static float Cubic(float p1, float p2, float s)
            => ((1f - 3f * p2 + 3f * p1) * s + (3f * p2 - 6f * p1)) * s * s + 3f * p1 * s;

        private static float Slope(float p1, float p2, float s)
            => 3f * (1f - 3f * p2 + 3f * p1) * s * s + 2f * (3f * p2 - 6f * p1) * s + 3f * p1;
    }
}
