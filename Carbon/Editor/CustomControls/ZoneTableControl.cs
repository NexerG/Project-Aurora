using System.Text;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork.Registry;

namespace Carbon.Editor.CustomControls
{
    // Every zone of a capture, per thread, longest first — the numbers the one-second report prints,
    // but over the whole file. Rolled up from the span stream, which records every instance, so a
    // zone that re-enters itself totals more here than the report's outermost-only rule gives it.
    [A_XSDType("ZoneTable", "UI")]
    public class ZoneTableControl : ScrollableControl
    {
        #region properties
        [A_XSDElementProperty("RowSpacing", "UI", "Space between rows in pixels.")]
        public float rowSpacing = 2f;

        [A_XSDElementProperty("ZoneFontSize", "UI", "Font size of a zone's name and total.")]
        public int zoneFontSize = 13;

        [A_XSDElementProperty("DetailFontSize", "UI", "Font size of a zone's counts line.")]
        public int detailFontSize = 11;

        // Fixed columns, because a star-width Label is measured at zero width and wraps to one
        // glyph per line — LabelControl does not override TextControl.WrapWidth.
        [A_XSDElementProperty("NameWidth", "UI", "Width of the zone name column in pixels.")]
        public float nameWidth = 210f;

        [A_XSDElementProperty("TotalWidth", "UI", "Width of the total-milliseconds column in pixels.")]
        public float totalWidth = 74f;

        [A_XSDElementProperty("Indent", "UI", "Left offset a zone gains per level of nesting, in pixels.")]
        public float indent = 14f;

        [A_XSDElementProperty("SwatchWidth", "UI", "Width of the depth colour bar ahead of a zone's name, in pixels.")]
        public float swatchWidth = 3f;

        // palette
        [A_XSDElementProperty("DepthColorsHex", "UI", "Comma separated colours, one per nesting level, cycled. Matches the span chart.")]
        public string depthColorsHex = "#B8A48C,#C4A882,#A8B49C,#B0A8BC,#C0B098";

        [A_XSDElementProperty("SeparatorColorHex", "UI", "Rule drawn between one thread and the next.")]
        public string separatorColorHex = "#DCDAD3";

        [A_XSDElementProperty("HeaderColorHex", "UI", "Text color of a thread's heading.")]
        public string headerColorHex = "#3A3833";

        [A_XSDElementProperty("ZoneColorHex", "UI", "Text color of a zone's name and total.")]
        public string zoneColorHex = "#23221E";

        [A_XSDElementProperty("DetailColorHex", "UI", "Text color of a zone's counts line.")]
        public string detailColorHex = "#918F87";
        #endregion

        // one zone over a whole capture, plus where it sits in the nesting
        private struct Rolled
        {
            public long calls;
            public long total;
            public long bytes;
            public long min;
            public long max;
            public int depth;
            public string parent;
        }

        private readonly StackPanelControl rows = new StackPanelControl();
        private string[] _depthColors = Array.Empty<string>();

        public ZoneTableControl()
        {
            scrollDirection = ScrollDirection.Vertical;

            rows.maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
            rows.orientation = StackPanelControl.Orientation.Vertical;
            AddChild(rows);
        }

        public void SetSession(CaptureSession session)
        {
            foreach (Entity row in rows.children.ToArray())
                row.Destroy();

            rows.Spacing = rowSpacing;

            _depthColors = depthColorsHex.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (_depthColors.Length == 0) _depthColors = new[] { "#B8A48C" };

            bool first = true;
            foreach (CapturedThread thread in session.threads)
            {
                if (!first) rows.AddChild(Separator());
                first = false;
                Fill(thread);
            }

            InvalidateLayout();
        }

        private VulkanControl Separator() => new PanelControl
        {
            preferredHeight = 1,
            horizontalAlignment = HorizontalAlignment.Stretch,
            controlColorHex = separatorColorHex,
            margin = new Thickness(6, 0, 6, 0),
            hitTestable = false
        };

        private void Fill(CapturedThread thread)
        {
            Dictionary<string, Rolled> zones = new Dictionary<string, Rolled>();
            Dictionary<(string zone, string name), long> counters = new Dictionary<(string, string), long>();

            // Spans arrive in pre-order, so whatever is open one level up is the parent.
            string[] open = new string[32];
            long threadBytes = 0;

            foreach (CapturedFrame frame in thread.frames)
            {
                threadBytes += frame.bytes;

                for (int i = 0; i < frame.spanCount; i++)
                {
                    CapturedSpan span = thread.spans[frame.firstSpan + i];
                    long elapsed = span.end - span.begin;
                    string name = thread.NameOf(span.name);

                    while (span.depth >= open.Length) Array.Resize(ref open, open.Length * 2);
                    open[span.depth] = name;

                    zones.TryGetValue(name, out Rolled rolled);
                    if (rolled.calls == 0)
                    {
                        rolled.min = elapsed;
                        rolled.max = elapsed;
                        rolled.depth = span.depth;
                        rolled.parent = span.depth > 0 ? open[span.depth - 1] : string.Empty;
                    }
                    rolled.calls++;
                    rolled.total += elapsed;
                    rolled.bytes += span.bytes;
                    if (elapsed < rolled.min) rolled.min = elapsed;
                    if (elapsed > rolled.max) rolled.max = elapsed;
                    zones[name] = rolled;
                }

                for (int i = 0; i < frame.counterCount; i++)
                {
                    CapturedCounter counter = thread.counters[frame.firstCounter + i];
                    string zone = counter.span >= 0 ? thread.NameOf(thread.spans[counter.span].name) : "(frame)";
                    counters.TryGetValue((zone, thread.NameOf(counter.name)), out long tally);
                    counters[(zone, thread.NameOf(counter.name))] = tally + counter.value;
                }
            }

            rows.AddChild(new LabelControl
            {
                text = $"{thread.thread} — {thread.frames.Count} frames, {CapturedThread.Bytes(threadBytes)} allocated{(thread.dropped > 0 ? $", {thread.dropped} dropped" : "")}{(thread.truncated ? ", truncated" : "")}",
                fontSize = zoneFontSize,
                controlColorHex = headerColorHex,
                preferredHeight = 22,
                horizontalPosition = 0f
            });

            HashSet<string> emitted = new HashSet<string>();
            Emit(thread, zones, counters, string.Empty, 0, emitted);

            // A zone whose parent never became a row would otherwise vanish from a diagnostic table.
            foreach (KeyValuePair<string, Rolled> zone in zones.OrderByDescending(z => z.Value.total))
                if (emitted.Add(zone.Key))
                    rows.AddChild(Row(thread, zone.Key, zone.Value, counters, 0));
        }

        // Children under their parent, and siblings by cost.
        private void Emit(CapturedThread thread, Dictionary<string, Rolled> zones,
            Dictionary<(string zone, string name), long> counters, string parent, int depth, HashSet<string> emitted)
        {
            foreach (KeyValuePair<string, Rolled> zone in zones
                .Where(z => z.Value.depth == depth && z.Value.parent == parent)
                .OrderByDescending(z => z.Value.total)
                .ToList())
            {
                if (!emitted.Add(zone.Key)) continue;

                rows.AddChild(Row(thread, zone.Key, zone.Value, counters, depth));
                Emit(thread, zones, counters, zone.Key, depth + 1, emitted);
            }
        }

        private VulkanControl Row(CapturedThread thread, string name, Rolled rolled,
            Dictionary<(string zone, string name), long> counters, int depth)
        {
            float inset = indent * depth;
            float gutter = swatchWidth + 6;

            StackPanelControl head = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Horizontal,
                maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible"),
                preferredHeight = 17
            };
            head.BubbleAll();

            head.AddChild(new PanelControl
            {
                preferredWidth = (int)swatchWidth,
                preferredHeight = 17,
                controlColorHex = _depthColors[depth % _depthColors.Length],
                margin = new Thickness(0, 6, 0, 0),
                hitTestable = false
            });

            head.AddChild(new LabelControl
            {
                text = name,
                fontSize = zoneFontSize,
                controlColorHex = zoneColorHex,
                preferredWidth = (int)MathF.Max(40, nameWidth - inset - gutter),
                preferredHeight = 17,
                horizontalPosition = 0f
            });

            head.AddChild(new LabelControl
            {
                text = $"{thread.Ms(rolled.total):F2}ms",
                fontSize = zoneFontSize,
                controlColorHex = zoneColorHex,
                preferredWidth = (int)totalWidth,
                preferredHeight = 17,
                horizontalPosition = 1f
            });

            StackPanelControl row = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Vertical,
                maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible"),
                preferredHeight = 34,
                margin = new Thickness(0, 0, 0, inset),
                horizontalPosition = 0f
            };
            row.BubbleAll();
            row.AddChild(head);

            row.AddChild(new LabelControl
            {
                text = $"x{rolled.calls}  min {thread.Ms(rolled.min):F3}  max {thread.Ms(rolled.max):F3}  {CapturedThread.Bytes(rolled.bytes)}{Counters(name, counters)}",
                fontSize = detailFontSize,
                controlColorHex = detailColorHex,
                preferredHeight = 15,
                margin = new Thickness(0, 0, 0, gutter),
                horizontalPosition = 0f
            });

            return row;
        }

        private static string Counters(string zone, Dictionary<(string zone, string name), long> counters)
        {
            StringBuilder? line = null;
            foreach (KeyValuePair<(string zone, string name), long> entry in counters)
            {
                if (entry.Key.zone != zone) continue;
                line ??= new StringBuilder("  —");
                line.Append(' ').Append(entry.Key.name).Append(' ').Append(entry.Value);
            }
            return line?.ToString() ?? string.Empty;
        }
    }
}
