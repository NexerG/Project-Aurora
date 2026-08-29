using ArctisAurora.Core.UISystem;
using Silk.NET.Maths;
using System.Globalization;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing.Serialization
{
    public static class SvgPath
    {
        private static readonly Diagnostics.LogChannel Log = Diagnostics.LogChannel.For("Assets");

        // Reads one icon into contours the MTSDF generator can rasterize: every segment a cubic,
        // Y up, normalized against the viewBox rather than the ink so a set shares scale and centring.
        public static bool TryLoad(string file, out Glyph glyph, out string reason)
        {
            glyph = new Glyph();
            reason = string.Empty;

            XElement root;
            try { root = XElement.Load(file); }
            catch (Exception e) { reason = $"unreadable ({e.Message})"; return false; }

            if (!TryViewBox(root, out float minX, out float minY, out float vbW, out float vbH))
            { reason = "no viewBox and no width/height"; return false; }

            short boxW = (short)MathF.Round(vbW);
            short boxH = (short)MathF.Round(vbH);
            if (boxW <= 0 || boxH <= 0) { reason = $"degenerate viewBox {vbW}x{vbH}"; return false; }

            XNamespace ns = root.GetDefaultNamespace();
            List<Bezier> contours = new List<Bezier>();
            foreach (XElement element in root.Descendants(ns + "path"))
            {
                if (Property(element, "fill") == "none")
                { reason = "stroked path (fill:none) — stroke-to-outline is not implemented"; return false; }

                if (Property(element, "fill-rule") == "evenodd")
                    Log.Warn($"icon '{Path.GetFileName(file)}': fill-rule=\"evenodd\" is filled as nonzero.");

                string data = (string)element.Attribute("d") ?? string.Empty;
                if (data.Length == 0) continue;
                if (!ParseData(data, contours, out reason)) return false;
            }

            if (contours.Count == 0) { reason = "no filled <path> produced any contour"; return false; }

            float scale = MathF.Max(boxW, boxH);
            foreach (Bezier contour in contours)
                foreach (Bezier.Point point in contour.points)
                {
                    point.SetX((point.pos.X - minX) / scale);
                    point.SetY((boxH - (point.pos.Y - minY)) / scale);
                }

            glyph.contours = contours;
            glyph.SetParams(0, boxW, 0, boxH, scale);
            glyph.BuildEdges();
            return true;
        }

        private static bool TryViewBox(XElement root, out float minX, out float minY, out float w, out float h)
        {
            minX = minY = w = h = 0f;
            string box = (string)root.Attribute("viewBox") ?? string.Empty;
            if (box.Length > 0)
            {
                string[] parts = box.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4
                    && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out minX)
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out minY)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out w)
                    && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out h))
                    return true;
            }
            return float.TryParse(Trim((string)root.Attribute("width")), NumberStyles.Float, CultureInfo.InvariantCulture, out w)
                && float.TryParse(Trim((string)root.Attribute("height")), NumberStyles.Float, CultureInfo.InvariantCulture, out h);
        }

        private static string Trim(string value) => (value ?? string.Empty).TrimEnd('p', 'x', 'e', 'm', ' ');

        // Presentation attribute, or the same name inside style="".
        private static string Property(XElement element, string name)
        {
            string direct = (string)element.Attribute(name);
            if (direct != null) return direct.Trim();

            string style = (string)element.Attribute("style") ?? string.Empty;
            foreach (string entry in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = entry.IndexOf(':');
                if (colon > 0 && entry.AsSpan(0, colon).Trim().SequenceEqual(name))
                    return entry.Substring(colon + 1).Trim();
            }
            return null;
        }

        // Emits a point run of anchor, c0, c1, anchor, c0, c1 ... The closing segment contributes
        // only its two controls, because BuildEdges wraps the last edge back to point zero.
        private static bool ParseData(string data, List<Bezier> contours, out string reason)
        {
            reason = string.Empty;
            int i = 0;
            char command = '\0';
            Vector2D<float> current = default, start = default, lastCubic = default, lastQuad = default;
            bool wasCubic = false, wasQuad = false;
            Bezier contour = null;

            while (true)
            {
                SkipSeparators(data, ref i);
                if (i >= data.Length) break;

                if (char.IsLetter(data[i]))
                {
                    command = data[i];
                    i++;
                }
                else if (command == 'M') command = 'L';
                else if (command == 'm') command = 'l';
                else if (command == '\0') { reason = "path data does not start with a command"; return false; }

                bool relative = char.IsLower(command);
                Vector2D<float> origin = relative ? current : Vector2D<float>.Zero;

                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        Close(contours, ref contour, current, start);
                        current = start = origin + ReadPoint(data, ref i);
                        contour = new Bezier();
                        contour.points.Add(new Bezier.Point(current, true));
                        wasCubic = wasQuad = false;
                        break;

                    case 'Z':
                        Close(contours, ref contour, current, start);
                        current = start;
                        wasCubic = wasQuad = false;
                        break;

                    case 'L':
                    case 'H':
                    case 'V':
                    {
                        if (contour == null) { reason = "line before any moveto"; return false; }
                        Vector2D<float> to = char.ToUpperInvariant(command) switch
                        {
                            'H' => new Vector2D<float>(origin.X + ReadNumber(data, ref i), current.Y),
                            'V' => new Vector2D<float>(current.X, origin.Y + ReadNumber(data, ref i)),
                            _ => origin + ReadPoint(data, ref i),
                        };
                        AddCubic(contour, current + (to - current) / 3f, current + (to - current) * (2f / 3f), to);
                        current = to;
                        wasCubic = wasQuad = false;
                        break;
                    }

                    case 'C':
                    case 'S':
                    {
                        if (contour == null) { reason = "curve before any moveto"; return false; }
                        Vector2D<float> c0 = char.ToUpperInvariant(command) == 'S'
                            ? (wasCubic ? current * 2f - lastCubic : current)
                            : origin + ReadPoint(data, ref i);
                        Vector2D<float> c1 = origin + ReadPoint(data, ref i);
                        Vector2D<float> to = origin + ReadPoint(data, ref i);
                        AddCubic(contour, c0, c1, to);
                        current = to;
                        lastCubic = c1;
                        wasCubic = true;
                        wasQuad = false;
                        break;
                    }

                    case 'Q':
                    case 'T':
                    {
                        if (contour == null) { reason = "curve before any moveto"; return false; }
                        Vector2D<float> control = char.ToUpperInvariant(command) == 'T'
                            ? (wasQuad ? current * 2f - lastQuad : current)
                            : origin + ReadPoint(data, ref i);
                        Vector2D<float> to = origin + ReadPoint(data, ref i);
                        AddCubic(contour, current + (control - current) * (2f / 3f), to + (control - to) * (2f / 3f), to);
                        current = to;
                        lastQuad = control;
                        wasQuad = true;
                        wasCubic = false;
                        break;
                    }

                    case 'A':
                        reason = "elliptical arc ('A') is not supported";
                        return false;

                    default:
                        reason = $"unknown path command '{command}'";
                        return false;
                }
            }

            Close(contours, ref contour, current, start);
            return true;
        }

        private static void AddCubic(Bezier contour, Vector2D<float> c0, Vector2D<float> c1, Vector2D<float> to)
        {
            contour.points.Add(new Bezier.Point(c0, false) { isCubicControl = true });
            contour.points.Add(new Bezier.Point(c1, false) { isCubicControl = true });
            contour.points.Add(new Bezier.Point(to, true));
        }

        // A fill closes whether the data said Z or not. An already-closed run ends on a duplicate of
        // the start anchor, which has to go so the wrap does not become a zero-length segment.
        private static void Close(List<Bezier> contours, ref Bezier contour, Vector2D<float> current, Vector2D<float> start)
        {
            if (contour == null) return;
            if (contour.points.Count >= 4)
            {
                if (Vector2D.DistanceSquared(current, start) < 1e-12f)
                    contour.points.RemoveAt(contour.points.Count - 1);
                else
                {
                    contour.points.Add(new Bezier.Point(current + (start - current) / 3f, false) { isCubicControl = true });
                    contour.points.Add(new Bezier.Point(current + (start - current) * (2f / 3f), false) { isCubicControl = true });
                }
                contours.Add(contour);
            }
            contour = null;
        }

        private static void SkipSeparators(string data, ref int i)
        {
            while (i < data.Length && (data[i] == ' ' || data[i] == ',' || data[i] == '\t'
                || data[i] == '\n' || data[i] == '\r')) i++;
        }

        private static Vector2D<float> ReadPoint(string data, ref int i) =>
            new Vector2D<float>(ReadNumber(data, ref i), ReadNumber(data, ref i));

        private static float ReadNumber(string data, ref int i)
        {
            SkipSeparators(data, ref i);
            int begin = i;
            if (i < data.Length && (data[i] == '-' || data[i] == '+')) i++;
            // One decimal point per number: minified data writes two numbers as ".5.5".
            bool dot = false;
            while (i < data.Length && (char.IsDigit(data[i]) || (data[i] == '.' && !dot)))
            {
                if (data[i] == '.') dot = true;
                i++;
            }
            if (i < data.Length && (data[i] == 'e' || data[i] == 'E'))
            {
                i++;
                if (i < data.Length && (data[i] == '-' || data[i] == '+')) i++;
                while (i < data.Length && char.IsDigit(data[i])) i++;
            }
            return float.TryParse(data.AsSpan(begin, i - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : 0f;
        }
    }
}
