using System.Numerics;
using System.Globalization;
using System.Xml.Linq;

namespace ArctisAurora.Core.Filing.Serialization
{
    public static class SvgPath
    {
        private static readonly HashSet<string> unrendered = new HashSet<string>
            { "defs", "clipPath", "mask", "symbol", "pattern", "marker" };

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
            float scale = MathF.Max(boxW, boxH);
            Matrix3x2 normalize = new Matrix3x2(1f / scale, 0f, 0f, -1f / scale, -minX / scale, (boxH + minY) / scale);

            List<Bezier> contours = new List<Bezier>();
            List<StrokeRun> runs = new List<StrokeRun>();
            bool? evenOdd = null;
            foreach (XElement element in root.Descendants())
            {
                if (element.Name.Namespace != ns) continue;
                string data = ShapeData(element);
                if (string.IsNullOrEmpty(data)) continue;
                if (element.Ancestors().Any(a => unrendered.Contains(a.Name.LocalName))) continue;

                bool filled = Property(element, "fill") != "none";
                string paint = Property(element, "stroke");
                float width = Number(Property(element, "stroke-width"), 1f);
                bool stroked = paint != null && paint != "none" && width > 0f;
                if (!filled && !stroked) continue;

                if (filled)
                {
                    bool rule = Property(element, "fill-rule") == "evenodd";
                    if (evenOdd != null && evenOdd != rule)
                    { reason = "mixes nonzero and evenodd fill rules"; return false; }
                    evenOdd = rule;
                }
                if (stroked && Property(element, "vector-effect") == "non-scaling-stroke")
                { reason = "vector-effect=\"non-scaling-stroke\" is not supported"; return false; }

                if (!TryTransform(element, out Matrix3x2 transform, out reason)) return false;
                int first = contours.Count, firstRun = runs.Count;
                if (!ParseData(data, filled ? contours : null, stroked ? runs : null, out reason)) return false;
                for (int c = first; c < contours.Count; c++)
                    foreach (Bezier.Point point in contours[c].points)
                        point.pos = Vector2.Transform(point.pos, transform);

                for (int r = firstRun; r < runs.Count; r++)
                {
                    StrokeRun run = runs[r];
                    run.halfWidth = width / 2f;
                    run.cap = Property(element, "stroke-linecap") switch
                    {
                        "round" => StrokeCap.Round,
                        "square" => StrokeCap.Square,
                        _ => StrokeCap.Butt,
                    };
                    run.join = Property(element, "stroke-linejoin") switch
                    {
                        "round" => StrokeJoin.Round,
                        "bevel" => StrokeJoin.Bevel,
                        _ => StrokeJoin.Miter,
                    };
                    run.miterLimit = MathF.Max(1f, Number(Property(element, "stroke-miterlimit"), 4f));
                    run.toGlyph = transform * normalize;
                }
            }

            if (contours.Count == 0 && runs.Count == 0) { reason = "no shape produced any contour or stroke"; return false; }

            foreach (Bezier contour in contours)
                foreach (Bezier.Point point in contour.points)
                {
                    point.SetX((point.pos.X - minX) / scale);
                    point.SetY((boxH - (point.pos.Y - minY)) / scale);
                }

            glyph.contours = contours;
            glyph.evenOdd = evenOdd == true;
            glyph.strokes = runs;
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

        // Presentation attribute, or the same name inside style="", inherited from the nearest ancestor that sets it.
        private static string Property(XElement element, string name)
        {
            foreach (XElement owner in element.AncestorsAndSelf())
            {
                string direct = (string)owner.Attribute(name);
                if (direct != null) return direct.Trim();

                string style = (string)owner.Attribute("style") ?? string.Empty;
                foreach (string entry in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = entry.IndexOf(':');
                    if (colon > 0 && entry.AsSpan(0, colon).Trim().SequenceEqual(name))
                        return entry.Substring(colon + 1).Trim();
                }
            }
            return null;
        }

        // Path data for a shape element, or null when it is not one or encloses nothing.
        private static string ShapeData(XElement element)
        {
            switch (element.Name.LocalName)
            {
                case "path": return (string)element.Attribute("d");

                case "rect":
                {
                    float x = Length(element, "x"), y = Length(element, "y");
                    float w = Length(element, "width"), h = Length(element, "height");
                    if (w <= 0f || h <= 0f) return null;
                    float rx = Length(element, "rx", float.NaN), ry = Length(element, "ry", float.NaN);
                    if (float.IsNaN(rx)) rx = float.IsNaN(ry) ? 0f : ry;
                    if (float.IsNaN(ry)) ry = rx;
                    rx = MathF.Min(rx, w / 2f);
                    ry = MathF.Min(ry, h / 2f);
                    float r = x + w, b = y + h;
                    if (rx <= 0f || ry <= 0f) return FormattableString.Invariant($"M{x} {y}H{r}V{b}H{x}Z");

                    string top = w > 2f * rx ? FormattableString.Invariant($"H{r - rx}") : string.Empty;
                    string right = h > 2f * ry ? FormattableString.Invariant($"V{b - ry}") : string.Empty;
                    string bottom = w > 2f * rx ? FormattableString.Invariant($"H{x + rx}") : string.Empty;
                    string left = h > 2f * ry ? FormattableString.Invariant($"V{y + ry}") : string.Empty;
                    string corner = FormattableString.Invariant($"A{rx} {ry} 0 0 1 ");
                    return FormattableString.Invariant($"M{x + rx} {y}{top}{corner}{r} {y + ry}{right}{corner}{r - rx} {b}")
                        + FormattableString.Invariant($"{bottom}{corner}{x} {b - ry}{left}{corner}{x + rx} {y}Z");
                }

                case "circle":
                case "ellipse":
                {
                    float cx = Length(element, "cx"), cy = Length(element, "cy");
                    float rx, ry;
                    if (element.Name.LocalName == "circle") rx = ry = Length(element, "r");
                    else
                    {
                        rx = Length(element, "rx", float.NaN);
                        ry = Length(element, "ry", float.NaN);
                        if (float.IsNaN(rx)) rx = ry;
                        if (float.IsNaN(ry)) ry = rx;
                    }
                    if (!(rx > 0f) || !(ry > 0f)) return null;
                    return FormattableString.Invariant(
                        $"M{cx - rx} {cy}A{rx} {ry} 0 0 1 {cx + rx} {cy}A{rx} {ry} 0 0 1 {cx - rx} {cy}Z");
                }

                case "polygon":
                case "polyline":
                {
                    string points = (string)element.Attribute("points");
                    if (string.IsNullOrWhiteSpace(points)) return null;
                    return "M" + points + (element.Name.LocalName == "polygon" ? "Z" : string.Empty);
                }

                default: return null;
            }
        }

        private static float Length(XElement element, string name, float fallback = 0f) =>
            Number((string)element.Attribute(name), fallback);

        private static float Number(string text, float fallback) =>
            float.TryParse(Trim(text), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

        // The element's transform composed with every ancestor's, innermost first.
        private static bool TryTransform(XElement element, out Matrix3x2 matrix, out string reason)
        {
            matrix = Matrix3x2.Identity;
            reason = string.Empty;
            foreach (XElement owner in element.AncestorsAndSelf())
            {
                string text = (string)owner.Attribute("transform");
                if (text == null) continue;
                if (!ParseTransform(text, out Matrix3x2 own, out reason)) return false;
                matrix *= own;
            }
            return true;
        }

        private static bool ParseTransform(string text, out Matrix3x2 matrix, out string reason)
        {
            matrix = Matrix3x2.Identity;
            reason = string.Empty;
            Span<float> args = stackalloc float[6];
            int i = 0;
            while (true)
            {
                SkipSeparators(text, ref i);
                if (i >= text.Length) return true;

                int nameStart = i;
                while (i < text.Length && char.IsLetter(text[i])) i++;
                string name = text.Substring(nameStart, i - nameStart);
                SkipSeparators(text, ref i);
                if (i >= text.Length || text[i] != '(') { reason = $"malformed transform '{text}'"; return false; }
                i++;

                int count = 0;
                while (true)
                {
                    SkipSeparators(text, ref i);
                    if (i < text.Length && text[i] == ')') { i++; break; }
                    int before = i;
                    if (count == args.Length) { reason = $"malformed transform '{text}'"; return false; }
                    args[count++] = ReadNumber(text, ref i);
                    if (i == before) { reason = $"malformed transform '{text}'"; return false; }
                }

                const float degrees = MathF.PI / 180f;
                Matrix3x2 item;
                switch (name)
                {
                    case "matrix" when count == 6: item = new Matrix3x2(args[0], args[1], args[2], args[3], args[4], args[5]); break;
                    case "translate" when count is 1 or 2: item = Matrix3x2.CreateTranslation(args[0], count == 2 ? args[1] : 0f); break;
                    case "scale" when count is 1 or 2: item = Matrix3x2.CreateScale(args[0], count == 2 ? args[1] : args[0]); break;
                    case "rotate" when count == 1: item = Matrix3x2.CreateRotation(args[0] * degrees); break;
                    case "rotate" when count == 3: item = Matrix3x2.CreateRotation(args[0] * degrees, new Vector2(args[1], args[2])); break;
                    case "skewX" when count == 1: item = Matrix3x2.CreateSkew(args[0] * degrees, 0f); break;
                    case "skewY" when count == 1: item = Matrix3x2.CreateSkew(0f, args[0] * degrees); break;
                    default: reason = $"unsupported transform '{name}' with {count} arguments"; return false;
                }
                matrix = item * matrix;
            }
        }

        // Emits a point run of anchor, c0, c1, anchor, c0, c1 ... The closing segment contributes
        // only its two controls, because BuildEdges wraps the last edge back to point zero.
        // Either output may be null; runs receive each subpath as written, unclosed unless it said Z.
        private static bool ParseData(string data, List<Bezier> contours, List<StrokeRun> runs, out string reason)
        {
            reason = string.Empty;
            int i = 0;
            char command = '\0';
            Vector2 current = default, start = default, lastCubic = default, lastQuad = default;
            bool wasCubic = false, wasQuad = false;
            Bezier contour = null;

            while (true)
            {
                SkipSeparators(data, ref i);
                if (i >= data.Length) break;
                int before = i;

                if (char.IsLetter(data[i]))
                {
                    command = data[i];
                    i++;
                }
                else if (command == 'M') command = 'L';
                else if (command == 'm') command = 'l';
                else if (command == '\0') { reason = "path data does not start with a command"; return false; }

                bool relative = char.IsLower(command);
                Vector2 origin = relative ? current : Vector2.Zero;

                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        Close(contours, runs, ref contour, current, start, false);
                        current = start = origin + ReadPoint(data, ref i);
                        contour = new Bezier();
                        contour.points.Add(new Bezier.Point(current, true));
                        wasCubic = wasQuad = false;
                        break;

                    case 'Z':
                        Close(contours, runs, ref contour, current, start, true);
                        current = start;
                        wasCubic = wasQuad = false;
                        break;

                    case 'L':
                    case 'H':
                    case 'V':
                    {
                        if (contour == null) { reason = "line before any moveto"; return false; }
                        Vector2 to = char.ToUpperInvariant(command) switch
                        {
                            'H' => new Vector2(origin.X + ReadNumber(data, ref i), current.Y),
                            'V' => new Vector2(current.X, origin.Y + ReadNumber(data, ref i)),
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
                        Vector2 c0 = char.ToUpperInvariant(command) == 'S'
                            ? (wasCubic ? current * 2f - lastCubic : current)
                            : origin + ReadPoint(data, ref i);
                        Vector2 c1 = origin + ReadPoint(data, ref i);
                        Vector2 to = origin + ReadPoint(data, ref i);
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
                        Vector2 control = char.ToUpperInvariant(command) == 'T'
                            ? (wasQuad ? current * 2f - lastQuad : current)
                            : origin + ReadPoint(data, ref i);
                        Vector2 to = origin + ReadPoint(data, ref i);
                        AddCubic(contour, current + (control - current) * (2f / 3f), to + (control - to) * (2f / 3f), to);
                        current = to;
                        lastQuad = control;
                        wasQuad = true;
                        wasCubic = false;
                        break;
                    }

                    case 'A':
                    {
                        if (contour == null) { reason = "arc before any moveto"; return false; }
                        float rx = ReadNumber(data, ref i);
                        float ry = ReadNumber(data, ref i);
                        float rotation = ReadNumber(data, ref i);
                        if (!ReadFlag(data, ref i, out bool large) || !ReadFlag(data, ref i, out bool sweep))
                        { reason = $"malformed arc flag at {i}"; return false; }
                        Vector2 to = origin + ReadPoint(data, ref i);
                        AddArc(contour, current, rx, ry, rotation, large, sweep, to);
                        current = to;
                        wasCubic = wasQuad = false;
                        break;
                    }

                    default:
                        reason = $"unknown path command '{command}'";
                        return false;
                }

                if (i == before) { reason = $"malformed path data at {before}"; return false; }
            }

            Close(contours, runs, ref contour, current, start, false);
            return true;
        }

        private static void AddCubic(Bezier contour, Vector2 c0, Vector2 c1, Vector2 to)
        {
            contour.points.Add(new Bezier.Point(c0, false) { isCubicControl = true });
            contour.points.Add(new Bezier.Point(c1, false) { isCubicControl = true });
            contour.points.Add(new Bezier.Point(to, true));
        }

        // Endpoint arc to centre form (SVG 1.1 F.6.5), emitted as one cubic per quarter turn or less.
        private static void AddArc(Bezier contour, Vector2 from, float rx, float ry, float rotation, bool large, bool sweep, Vector2 to)
        {
            if (Vector2.DistanceSquared(from, to) < 1e-12f) return;
            rx = MathF.Abs(rx);
            ry = MathF.Abs(ry);
            if (rx == 0f || ry == 0f)
            {
                AddCubic(contour, from + (to - from) / 3f, from + (to - from) * (2f / 3f), to);
                return;
            }

            float phi = rotation * (MathF.PI / 180f);
            float cos = MathF.Cos(phi), sin = MathF.Sin(phi);
            Vector2 half = (from - to) / 2f;
            float x1 = cos * half.X + sin * half.Y;
            float y1 = -sin * half.X + cos * half.Y;

            float lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1f)
            {
                rx *= MathF.Sqrt(lambda);
                ry *= MathF.Sqrt(lambda);
            }

            float rx2 = rx * rx, ry2 = ry * ry;
            float coef = MathF.Sqrt(MathF.Max(0f, (rx2 * ry2 - rx2 * y1 * y1 - ry2 * x1 * x1) / (rx2 * y1 * y1 + ry2 * x1 * x1)));
            if (large == sweep) coef = -coef;
            float cx1 = coef * rx * y1 / ry;
            float cy1 = -coef * ry * x1 / rx;
            Vector2 centre = new Vector2(cos * cx1 - sin * cy1, sin * cx1 + cos * cy1) + (from + to) / 2f;

            float theta = MathF.Atan2((y1 - cy1) / ry, (x1 - cx1) / rx);
            float end = MathF.Atan2((-y1 - cy1) / ry, (-x1 - cx1) / rx);
            float delta = end - theta;
            if (sweep && delta < 0f) delta += 2f * MathF.PI;
            else if (!sweep && delta > 0f) delta -= 2f * MathF.PI;

            int segments = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(delta) / (MathF.PI / 2f) - 1e-3f));
            float step = delta / segments;
            float k = 4f / 3f * MathF.Tan(step / 4f);
            Vector2 Map(float ux, float uy) => centre + new Vector2(cos * rx * ux - sin * ry * uy, sin * rx * ux + cos * ry * uy);

            for (int s = 0; s < segments; s++)
            {
                float a0 = theta + step * s, a1 = a0 + step;
                float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0), c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                AddCubic(contour, Map(c0 - k * s0, s0 + k * c0), Map(c1 + k * s1, s1 - k * c1),
                    s == segments - 1 ? to : Map(c1, s1));
            }
        }

        // A fill closes whether the data said Z or not. An already-closed run ends on a duplicate of
        // the start anchor, which has to go so the wrap does not become a zero-length segment.
        private static void Close(List<Bezier> contours, List<StrokeRun> runs, ref Bezier contour, Vector2 current, Vector2 start, bool closed)
        {
            if (contour == null) return;
            if (runs != null && contour.points.Count == 1 && closed)
                runs.Add(new StrokeRun { edges = { new Edge { p0 = start, c0 = start, c1 = start, p1 = start } } });
            if (runs != null && contour.points.Count >= 4)
                runs.Add(Run(contour.points, closed, current, start));
            if (contours != null && contour.points.Count >= 4)
            {
                if (Vector2.DistanceSquared(current, start) < 1e-12f)
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

        // A subpath's segments as edges; Z adds the closing line unless the data already returned to the start.
        private static StrokeRun Run(List<Bezier.Point> points, bool closed, Vector2 current, Vector2 start)
        {
            StrokeRun run = new StrokeRun { closed = closed };
            for (int i = 0; i + 3 < points.Count; i += 3)
                run.edges.Add(new Edge { p0 = points[i].pos, c0 = points[i + 1].pos, c1 = points[i + 2].pos, p1 = points[i + 3].pos });
            if (closed && Vector2.DistanceSquared(current, start) >= 1e-12f)
                run.edges.Add(new Edge { p0 = current, c0 = current + (start - current) / 3f, c1 = current + (start - current) * (2f / 3f), p1 = start });
            return run;
        }

        private static void SkipSeparators(string data, ref int i)
        {
            while (i < data.Length && (data[i] == ' ' || data[i] == ',' || data[i] == '\t'
                || data[i] == '\n' || data[i] == '\r')) i++;
        }

        // Arc flags are one digit each and may be written without separators.
        private static bool ReadFlag(string data, ref int i, out bool flag)
        {
            SkipSeparators(data, ref i);
            flag = i < data.Length && data[i] == '1';
            if (i >= data.Length || (data[i] != '0' && data[i] != '1')) return false;
            i++;
            return true;
        }

        private static Vector2 ReadPoint(string data, ref int i) =>
            new Vector2(ReadNumber(data, ref i), ReadNumber(data, ref i));

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
