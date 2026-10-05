using ArctisAurora.Core.Diagnostics;
using System.Numerics;
using static ArctisAurora.Core.Filing.AuroraFont;

namespace ArctisAurora.Core.Filing
{
    // A CFF table's glyph programs, run as Type 2 charstrings into cubic contours in font units.
    internal static class CffOutlines
    {
        private static readonly LogChannel Log = LogChannel.For("Assets");

        private const int MaxSubrDepth = 10;

        #region ---- table ----
        public static (byte[][] charStrings, byte[][] global, byte[][] local) Read(BinaryReader reader, TableEntry cff)
        {
            reader.BaseStream.Position = cff.offset;
            byte[] data = reader.ReadBytes((int)cff.length);

            int pos = data[2];
            ReadIndex(data, ref pos);
            byte[][] topDicts = ReadIndex(data, ref pos);
            ReadIndex(data, ref pos);
            byte[][] global = ReadIndex(data, ref pos);

            Dictionary<int, double[]> top = ReadDict(topDicts[0]);
            if (top.ContainsKey(1230)) Log.Warn($"CFF font is CID-keyed, which is not supported; its glyphs read as empty.");
            if (top.TryGetValue(1206, out double[]? type) && type[0] != 2) Log.Warn($"CFF charstring type {type[0]} is not supported.");

            int charStringsAt = (int)top[17][0];
            byte[][] charStrings = ReadIndex(data, ref charStringsAt);

            byte[][] local = Array.Empty<byte[]>();
            if (top.TryGetValue(18, out double[]? privateEntry))
            {
                int size = (int)privateEntry[0];
                int privateAt = (int)privateEntry[1];
                Dictionary<int, double[]> priv = ReadDict(data[privateAt..(privateAt + size)]);
                if (priv.TryGetValue(19, out double[]? subrs))
                {
                    int subrsAt = privateAt + (int)subrs[0];
                    local = ReadIndex(data, ref subrsAt);
                }
            }
            return (charStrings, global, local);
        }

        private static byte[][] ReadIndex(byte[] data, ref int pos)
        {
            int count = data[pos] << 8 | data[pos + 1];
            pos += 2;
            if (count == 0) return Array.Empty<byte[]>();

            int offSize = data[pos++];
            int[] offsets = new int[count + 1];
            for (int i = 0; i <= count; i++)
            {
                int value = 0;
                for (int b = 0; b < offSize; b++)
                    value = value << 8 | data[pos++];
                offsets[i] = value;
            }

            int start = pos - 1;
            byte[][] items = new byte[count][];
            for (int i = 0; i < count; i++)
                items[i] = data[(start + offsets[i])..(start + offsets[i + 1])];
            pos = start + offsets[count];
            return items;
        }

        // Operator → operands; an escaped operator 12 x is keyed 1200 + x.
        private static Dictionary<int, double[]> ReadDict(byte[] dict)
        {
            Dictionary<int, double[]> entries = new Dictionary<int, double[]>();
            List<double> operands = new List<double>();
            int i = 0;
            while (i < dict.Length)
            {
                int b0 = dict[i];
                if (b0 <= 21)
                {
                    int op = b0 == 12 ? 1200 + dict[i + 1] : b0;
                    i += b0 == 12 ? 2 : 1;
                    entries[op] = operands.ToArray();
                    operands.Clear();
                }
                else if (b0 == 28)
                {
                    operands.Add((short)(dict[i + 1] << 8 | dict[i + 2]));
                    i += 3;
                }
                else if (b0 == 29)
                {
                    operands.Add(dict[i + 1] << 24 | dict[i + 2] << 16 | dict[i + 3] << 8 | dict[i + 4]);
                    i += 5;
                }
                else if (b0 == 30)
                {
                    i++;
                    while (i < dict.Length && (dict[i] & 0x0F) != 0x0F && (dict[i] >> 4) != 0x0F) i++;
                    i++;
                    operands.Add(0);
                }
                else if (b0 <= 246)
                {
                    operands.Add(b0 - 139);
                    i++;
                }
                else if (b0 <= 250)
                {
                    operands.Add((b0 - 247) * 256 + dict[i + 1] + 108);
                    i += 2;
                }
                else
                {
                    operands.Add(-(b0 - 251) * 256 - dict[i + 1] - 108);
                    i += 2;
                }
            }
            return entries;
        }
        #endregion

        #region ---- charstrings ----
        private sealed class Pen
        {
            public readonly List<Bezier> contours = new List<Bezier>();
            public readonly List<double> stack = new List<double>(48);
            public Bezier? current;
            public Vector2 at;
            public int stems;
            public bool widthTaken;
            public bool done;

            public void MoveTo(float dx, float dy)
            {
                Close();
                at += new Vector2(dx, dy);
                current = new Bezier();
                current.points.Add(new Bezier.Point(at, true));
            }

            public void LineTo(float dx, float dy)
            {
                at += new Vector2(dx, dy);
                current?.points.Add(new Bezier.Point(at, true));
            }

            public void CurveTo(float dx1, float dy1, float dx2, float dy2, float dx3, float dy3)
            {
                Vector2 c0 = at + new Vector2(dx1, dy1);
                Vector2 c1 = c0 + new Vector2(dx2, dy2);
                at = c1 + new Vector2(dx3, dy3);
                if (current == null) return;
                current.points.Add(new Bezier.Point(c0, false) { isCubicControl = true });
                current.points.Add(new Bezier.Point(c1, false) { isCubicControl = true });
                current.points.Add(new Bezier.Point(at, true));
            }

            // The closing point repeats the start; the contour wraps there on its own.
            public void Close()
            {
                if (current == null) return;
                List<Bezier.Point> points = current.points;
                if (points.Count > 1 && points[^1].isAnchor && points[^1].pos == points[0].pos) points.RemoveAt(points.Count - 1);
                if (points.Count > 1) contours.Add(current);
                current = null;
            }

            // An odd operand ahead of the first stack-clearing operator is the advance width.
            public int SkipWidth(bool hasWidth)
            {
                if (widthTaken) return 0;
                widthTaken = true;
                return hasWidth ? 1 : 0;
            }

            public float this[int i] => (float)stack[i];
        }

        public static List<Bezier> Interpret(byte[] charstring, byte[][] global, byte[][] local)
        {
            Pen pen = new Pen();
            Run(charstring, global, local, pen, 0);
            pen.Close();
            return pen.contours;
        }

        private static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

        private static void Run(byte[] code, byte[][] global, byte[][] local, Pen pen, int depth)
        {
            List<double> s = pen.stack;
            int i = 0;
            while (i < code.Length && !pen.done)
            {
                int b0 = code[i++];
                if (b0 >= 32 || b0 == 28)
                {
                    if (b0 == 28)
                    {
                        s.Add((short)(code[i] << 8 | code[i + 1]));
                        i += 2;
                    }
                    else if (b0 <= 246) s.Add(b0 - 139);
                    else if (b0 <= 250) s.Add((b0 - 247) * 256 + code[i++] + 108);
                    else if (b0 <= 254) s.Add(-(b0 - 251) * 256 - code[i++] - 108);
                    else
                    {
                        s.Add((code[i] << 24 | code[i + 1] << 16 | code[i + 2] << 8 | code[i + 3]) / 65536.0);
                        i += 4;
                    }
                    continue;
                }

                int k;
                switch (b0)
                {
                    case 1 or 3 or 18 or 23:
                        k = pen.SkipWidth(s.Count % 2 == 1);
                        pen.stems += (s.Count - k) / 2;
                        s.Clear();
                        break;
                    case 19 or 20:
                        k = pen.SkipWidth(s.Count % 2 == 1);
                        pen.stems += (s.Count - k) / 2;
                        i += (pen.stems + 7) / 8;
                        s.Clear();
                        break;
                    case 21:
                        k = pen.SkipWidth(s.Count > 2);
                        pen.MoveTo(pen[k], pen[k + 1]);
                        s.Clear();
                        break;
                    case 22:
                        k = pen.SkipWidth(s.Count > 1);
                        pen.MoveTo(pen[k], 0);
                        s.Clear();
                        break;
                    case 4:
                        k = pen.SkipWidth(s.Count > 1);
                        pen.MoveTo(0, pen[k]);
                        s.Clear();
                        break;
                    case 5:
                        for (k = 0; k + 1 < s.Count; k += 2)
                            pen.LineTo(pen[k], pen[k + 1]);
                        s.Clear();
                        break;
                    case 6 or 7:
                        bool horizontal = b0 == 6;
                        for (k = 0; k < s.Count; k++, horizontal = !horizontal)
                            pen.LineTo(horizontal ? pen[k] : 0, horizontal ? 0 : pen[k]);
                        s.Clear();
                        break;
                    case 8:
                        for (k = 0; k + 5 < s.Count; k += 6)
                            pen.CurveTo(pen[k], pen[k + 1], pen[k + 2], pen[k + 3], pen[k + 4], pen[k + 5]);
                        s.Clear();
                        break;
                    case 24:
                        for (k = 0; k + 7 < s.Count; k += 6)
                            pen.CurveTo(pen[k], pen[k + 1], pen[k + 2], pen[k + 3], pen[k + 4], pen[k + 5]);
                        if (k + 1 < s.Count) pen.LineTo(pen[k], pen[k + 1]);
                        s.Clear();
                        break;
                    case 25:
                        for (k = 0; k + 7 < s.Count; k += 2)
                            pen.LineTo(pen[k], pen[k + 1]);
                        if (k + 5 < s.Count) pen.CurveTo(pen[k], pen[k + 1], pen[k + 2], pen[k + 3], pen[k + 4], pen[k + 5]);
                        s.Clear();
                        break;
                    case 26:
                        {
                            k = 0;
                            float dx1 = 0;
                            if (s.Count % 4 == 1) dx1 = pen[k++];
                            for (; k + 3 < s.Count; k += 4, dx1 = 0)
                                pen.CurveTo(dx1, pen[k], pen[k + 1], pen[k + 2], 0, pen[k + 3]);
                            s.Clear();
                            break;
                        }
                    case 27:
                        {
                            k = 0;
                            float dy1 = 0;
                            if (s.Count % 4 == 1) dy1 = pen[k++];
                            for (; k + 3 < s.Count; k += 4, dy1 = 0)
                                pen.CurveTo(pen[k], dy1, pen[k + 1], pen[k + 2], pen[k + 3], 0);
                            s.Clear();
                            break;
                        }
                    case 30 or 31:
                        {
                            bool vertical = b0 == 30;
                            for (k = 0; k + 3 < s.Count; k += 4, vertical = !vertical)
                            {
                                float last = s.Count - k == 5 ? pen[k + 4] : 0;
                                if (vertical) pen.CurveTo(0, pen[k], pen[k + 1], pen[k + 2], pen[k + 3], last);
                                else pen.CurveTo(pen[k], 0, pen[k + 1], pen[k + 2], last, pen[k + 3]);
                            }
                            s.Clear();
                            break;
                        }
                    case 10 or 29:
                        {
                            byte[][] subrs = b0 == 10 ? local : global;
                            int index = (int)s[^1] + Bias(subrs.Length);
                            s.RemoveAt(s.Count - 1);
                            if (depth >= MaxSubrDepth || index < 0 || index >= subrs.Length)
                            {
                                pen.done = true;
                                break;
                            }
                            Run(subrs[index], global, local, pen, depth + 1);
                            break;
                        }
                    case 11:
                        return;
                    case 14:
                        k = pen.SkipWidth(s.Count == 1 || s.Count == 5);
                        if (s.Count - k >= 4) Log.Warn($"CFF glyph uses endchar accent composition (seac), which is not supported.");
                        pen.done = true;
                        s.Clear();
                        break;
                    case 12:
                        Flex(code[i++], pen);
                        s.Clear();
                        break;
                    default:
                        s.Clear();
                        break;
                }
            }
        }

        private static void Flex(int op, Pen pen)
        {
            List<double> s = pen.stack;
            switch (op)
            {
                case 34 when s.Count >= 7:
                    pen.CurveTo(pen[0], 0, pen[1], pen[2], pen[3], 0);
                    pen.CurveTo(pen[4], 0, pen[5], -pen[2], pen[6], 0);
                    break;
                case 35 when s.Count >= 12:
                    pen.CurveTo(pen[0], pen[1], pen[2], pen[3], pen[4], pen[5]);
                    pen.CurveTo(pen[6], pen[7], pen[8], pen[9], pen[10], pen[11]);
                    break;
                case 36 when s.Count >= 9:
                    pen.CurveTo(pen[0], pen[1], pen[2], pen[3], pen[4], 0);
                    pen.CurveTo(pen[5], 0, pen[6], pen[7], pen[8], -(pen[1] + pen[3] + pen[7]));
                    break;
                case 37 when s.Count >= 11:
                    {
                        float dx = pen[0] + pen[2] + pen[4] + pen[6] + pen[8];
                        float dy = pen[1] + pen[3] + pen[5] + pen[7] + pen[9];
                        pen.CurveTo(pen[0], pen[1], pen[2], pen[3], pen[4], pen[5]);
                        if (MathF.Abs(dx) > MathF.Abs(dy)) pen.CurveTo(pen[6], pen[7], pen[8], pen[9], pen[10], -dy);
                        else pen.CurveTo(pen[6], pen[7], pen[8], pen[9], -dx, pen[10]);
                        break;
                    }
                default:
                    Log.Warn($"CFF charstring operator 12 {op} is not supported; the glyph stops there.");
                    pen.done = true;
                    break;
            }
        }
        #endregion

        // The ink box of cubic contours, curve extrema included.
        public static (float xMin, float yMin, float xMax, float yMax) Bounds(List<Bezier> contours)
        {
            Vector2 min = new Vector2(float.MaxValue), max = new Vector2(float.MinValue);
            foreach (Bezier contour in contours)
            {
                List<Bezier.Point> p = contour.points;
                for (int i = 0; i < p.Count; i++)
                {
                    if (p[i].isAnchor)
                    {
                        min = Vector2.Min(min, p[i].pos);
                        max = Vector2.Max(max, p[i].pos);
                        continue;
                    }
                    if (!p[i].isCubicControl || i == 0 || !p[i - 1].isAnchor) continue;

                    Vector2 p0 = p[i - 1].pos, c0 = p[i].pos, c1 = p[(i + 1) % p.Count].pos, p1 = p[(i + 2) % p.Count].pos;
                    foreach (float t in Extrema(p0.X, c0.X, c1.X, p1.X).Concat(Extrema(p0.Y, c0.Y, c1.Y, p1.Y)))
                    {
                        Vector2 at = Cubic(p0, c0, c1, p1, t);
                        min = Vector2.Min(min, at);
                        max = Vector2.Max(max, at);
                    }
                }
            }
            return (min.X, min.Y, max.X, max.Y);
        }

        private static Vector2 Cubic(Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1, float t)
        {
            float u = 1 - t;
            return u * u * u * p0 + 3 * u * u * t * c0 + 3 * u * t * t * c1 + t * t * t * p1;
        }

        // Where one coordinate of a cubic turns, inside (0, 1).
        private static IEnumerable<float> Extrema(float p0, float c0, float c1, float p1)
        {
            float a = -p0 + 3 * c0 - 3 * c1 + p1;
            float b = 2 * (p0 - 2 * c0 + c1);
            float c = c0 - p0;
            if (MathF.Abs(a) < 1e-6f)
            {
                if (MathF.Abs(b) > 1e-6f && -c / b is > 0 and < 1) yield return -c / b;
                yield break;
            }
            float d = b * b - 4 * a * c;
            if (d < 0) yield break;
            float r = MathF.Sqrt(d);
            foreach (float t in new[] { (-b + r) / (2 * a), (-b - r) / (2 * a) })
                if (t is > 0 and < 1) yield return t;
        }
    }
}
