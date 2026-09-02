using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text;
using Silk.NET.Maths;

namespace Carbon.Editor.CustomControls
{
    // One lane per thread, one bar per frame, height against that lane's longest frame. More frames
    // than bars means a bar covers a run of them and stands for the longest one in it, so a spike
    // never averages away.
    [A_XSDType("FrameStrip", "UI")]
    public class FrameStripControl : AbstractContainerControl
    {
        #region properties
        // lane metrics
        [A_XSDElementProperty("MaxBars", "UI", "Most bars a lane draws; beyond it one bar covers several frames.")]
        public int maxBars = 200;

        [A_XSDElementProperty("LaneSpacing", "UI", "Space between lanes in pixels.")]
        public float laneSpacing = 6f;

        [A_XSDElementProperty("LabelWidth", "UI", "Width of the thread name column in pixels.")]
        public float labelWidth = 64f;

        [A_XSDElementProperty("BarGap", "UI", "Space between bars in pixels.")]
        public float barGap = 1f;

        [A_XSDElementProperty("LabelFontSize", "UI", "Font size of a lane's thread name.")]
        public int labelFontSize = 12;

        // palette
        [A_XSDElementProperty("BarColorHex", "UI", "Ground of a frame bar.")]
        public string barColorHex = "#C9C6BC";

        [A_XSDElementProperty("SelectedBarColorHex", "UI", "Ground of the selected frame's bar.")]
        public string selectedBarColorHex = "#C4622B";

        [A_XSDElementProperty("LabelColorHex", "UI", "Text color of a lane's thread name.")]
        public string labelColorHex = "#918F87";
        #endregion

        // A thread's row: its bars, and which frame each bar stands for.
        private sealed class Lane
        {
            public CapturedThread thread = null!;
            public LabelControl label = null!;
            public PanelControl[] bars = Array.Empty<PanelControl>();
            public int[] frames = Array.Empty<int>();
            public long[] durations = Array.Empty<long>();
            public long longest = 1;
        }

        private readonly List<Lane> _lanes = new List<Lane>();
        private int _selectedLane = -1;
        private int _selectedBar = -1;

        public Action<CapturedThread, int>? onFrameSelected;

        public CapturedThread? SelectedThread =>
            _selectedLane >= 0 && _selectedLane < _lanes.Count ? _lanes[_selectedLane].thread : null;

        // Replaces every lane, then selects the longest frame of the first thread so the views
        // below open on something worth looking at.
        public void SetSession(CaptureSession session)
        {
            foreach (Entity child in children.ToArray())
                child.Destroy();

            _lanes.Clear();
            _selectedLane = -1;
            _selectedBar = -1;

            foreach (CapturedThread thread in session.threads)
                if (thread.frames.Count > 0) _lanes.Add(BuildLane(thread));

            InvalidateLayout();

            if (_lanes.Count > 0) Select(0, Peak(_lanes[0]));
        }

        private Lane BuildLane(CapturedThread thread)
        {
            int count = Math.Min(thread.frames.Count, Math.Max(1, maxBars));

            Lane lane = new Lane
            {
                thread = thread,
                bars = new PanelControl[count],
                frames = new int[count],
                durations = new long[count],
                label = new LabelControl
                {
                    text = thread.thread,
                    fontSize = labelFontSize,
                    controlColorHex = labelColorHex,
                    horizontalPosition = 0f,
                    hitTestable = false
                }
            };

            // one bucket per bar, standing for its longest frame
            for (int bar = 0; bar < count; bar++)
            {
                int from = (int)((long)bar * thread.frames.Count / count);
                int to = (int)((long)(bar + 1) * thread.frames.Count / count);
                if (to <= from) to = from + 1;

                int peak = from;
                for (int i = from; i < to && i < thread.frames.Count; i++)
                    if (thread.frames[i].duration > thread.frames[peak].duration) peak = i;

                lane.frames[bar] = peak;
                lane.durations[bar] = thread.frames[peak].duration;
                if (lane.durations[bar] > lane.longest) lane.longest = lane.durations[bar];

                PanelControl panel = new PanelControl { controlColorHex = barColorHex, hitTestable = false };
                lane.bars[bar] = panel;
                AddChild(panel);
            }

            AddChild(lane.label);
            return lane;
        }

        private static int Peak(Lane lane)
        {
            int bar = 0;
            for (int i = 1; i < lane.durations.Length; i++)
                if (lane.durations[i] > lane.durations[bar]) bar = i;
            return bar;
        }

        public void Select(int laneIndex, int bar)
        {
            if (laneIndex < 0 || laneIndex >= _lanes.Count) return;

            Lane lane = _lanes[laneIndex];
            if (bar < 0 || bar >= lane.bars.Length) return;

            if (_selectedLane >= 0 && _selectedBar >= 0)
                _lanes[_selectedLane].bars[_selectedBar].controlColorHex = barColorHex;

            _selectedLane = laneIndex;
            _selectedBar = bar;
            lane.bars[bar].controlColorHex = selectedBarColorHex;

            onFrameSelected?.Invoke(lane.thread, lane.frames[bar]);
        }

        // The bars are not hit-testable, so the strip maps the point itself.
        public override void ResolveOnClick(Vector2D<float> point, Vector2D<float> delta)
        {
            if (_lanes.Count == 0) return;

            LayoutRect inner = arrangedRect.Shrink(padding);
            float laneHeight = LaneHeight(inner);
            if (laneHeight <= 0) return;

            int laneIndex = (int)((point.Y - inner.y) / (laneHeight + laneSpacing));
            if (laneIndex < 0 || laneIndex >= _lanes.Count) return;

            Lane lane = _lanes[laneIndex];
            float plotX = inner.x + labelWidth;
            float plotWidth = MathF.Max(1, inner.Right - plotX);

            int bar = (int)((point.X - plotX) / plotWidth * lane.bars.Length);
            Select(laneIndex, Math.Clamp(bar, 0, lane.bars.Length - 1));
        }

        private float LaneHeight(LayoutRect inner) =>
            (inner.height - laneSpacing * MathF.Max(0, _lanes.Count - 1)) / MathF.Max(1, _lanes.Count);

        // base owns the transform, the clip and the dirty flags — they are the engine assembly's.
        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            Vector2D<float> size = base.Measure(availableSize);

            foreach (Entity child in children)
                if (child is VulkanControl control) control.Measure(size);

            return size;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            base.Arrange(finalRect);

            LayoutRect inner = finalRect.Shrink(padding);
            float laneHeight = LaneHeight(inner);
            float plotX = inner.x + labelWidth;
            float plotWidth = MathF.Max(1, inner.Right - plotX);

            for (int i = 0; i < _lanes.Count; i++)
            {
                Lane lane = _lanes[i];
                float top = inner.y + i * (laneHeight + laneSpacing);

                lane.label.Arrange(new LayoutRect(inner.x, top, labelWidth, labelFontSize + 2));

                float slot = plotWidth / lane.bars.Length;
                for (int bar = 0; bar < lane.bars.Length; bar++)
                {
                    float height = MathF.Max(1, laneHeight * lane.durations[bar] / lane.longest);
                    lane.bars[bar].Arrange(new LayoutRect(
                        plotX + bar * slot,
                        top + laneHeight - height,
                        MathF.Max(1, slot - barGap),
                        height));
                }
            }
        }
    }
}
