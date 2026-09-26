using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Threading;
using ArctisAurora.Core.UI;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Animation
{
    // Steps every active track into its pool row, and lists what layout and Main settle.
    [A_XSDType("Animation", "Systems")]
    public sealed class AnimationSystem : ThreadedSystem, IJobFor
    {
        // spring rest thresholds
        private const float restDistance = 1e-3f;
        private const float restSpeed = 1e-3f;

        // per-chunk stats, one cache line apart: min row, max row, tracks stepped
        private const int statStride = 16;

        // track outcomes, 0 untouched
        private const byte stepped = 1;
        private const byte finished = 2;

        private struct SlotFade
        {
            public int slot;
            public Vector4 from;
            public Vector4 to;
            public float elapsed;
            public float duration;
            public Curve curve;
        }

        private DataPool _tracks = null!;
        private DataPool _signals = null!;
        private DataPool _paints = null!;
        private DataPool _keys = null!;
        private DataPool _dirty = null!;
        private DataPool _done = null!;
        private long _lastTick;

        // one Advance's parallel pass
        private float _dt;
        private SignalValue[] _signalRows = Array.Empty<SignalValue>();
        private int _signalCount;
        private Keyframe[] _keyRows = Array.Empty<Keyframe>();
        private int _keyCount;
        private int _chunkRows;
        private byte[] _outcome = Array.Empty<byte>();
        private int[] _stats = Array.Empty<int>();

        // paint slots mid-fade
        private readonly List<SlotFade> _fades = new List<SlotFade>();

        protected override void OnStart()
        {
            _tracks = DataManager.Get("Animations");
            _signals = DataManager.Get("Signals");
            _paints = DataManager.Get("Paints");
            _keys = DataManager.Get("Keyframes");
            _dirty = DataManager.Get("LayoutDirty");
            _done = DataManager.Get("AnimationDone");
        }

        [A_XSDActionDependency("Animation.Step", "Frame")]
        private void Advance()
        {
            long now = Stopwatch.GetTimestamp();
            float dt = _lastTick == 0 ? 0f : (float)((now - _lastTick) / (double)Stopwatch.Frequency);
            _lastTick = now;

            _dt = dt;
            _signalRows = _signals.Backing<SignalValue>();
            _signalCount = _signals.Count;
            _keyRows = _keys.Backing<Keyframe>();
            _keyCount = _keys.Count;

            int count = Animations.Awake.Length;
            int rowBytes = Unsafe.SizeOf<AnimationTrack>();
            _chunkRows = Jobs.Chunk(rowBytes);
            int chunks = (count + _chunkRows - 1) / _chunkRows;
            if (_outcome.Length < count) _outcome = new byte[Math.Max(count, _outcome.Length * 2)];
            if (_stats.Length < chunks * statStride) _stats = new int[Math.Max(chunks, _stats.Length / statStride * 2) * statStride];

            Profiling.Zone.Start("Anim.Step");
            Jobs.For(count, rowBytes, this);
            Profiling.Zone.Start("Anim.Emit");
            long steppedCount = Emit(chunks);
            Profiling.Zone.End("Anim.Emit");
            Profiling.Zone.Increment("Anim.Stepped", steppedCount);
            Profiling.Zone.End("Anim.Step");

            Profiling.Zone.Start("Anim.Fades");
            StepFades(dt);
            Profiling.Zone.End("Anim.Fades");
        }

        // Steps awake list entries [start, end) and their in-place targets; Emit reads what each one did.
        void IJobFor.Execute(int start, int end)
        {
            Span<AnimationTrack> tracks = _tracks.GetSpan<AnimationTrack>();
            ReadOnlySpan<int> awake = Animations.Awake;
            ReadOnlySpan<SignalValue> signals = _signalRows.AsSpan(0, _signalCount);
            ReadOnlySpan<Keyframe> keys = _keyRows.AsSpan(0, _keyCount);
            float dt = _dt;
            int min = int.MaxValue, max = -1, count = 0;
            for (int k = start; k < end; k++)
            {
                int i = awake[k];
                ref AnimationTrack track = ref tracks[i];
                if (track.driver == AnimationDriver.None) continue;
                if (track.follow >= 0 && track.follow < signals.Length) track.to = signals[track.follow].value;
                count++;

                bool done;
                if (track.driver == AnimationDriver.Tween)
                {
                    track.elapsed += dt;
                    float t = track.duration > 0f ? track.elapsed / track.duration : 1f;
                    track.value = Vector4.Lerp(track.from, track.to, Curve.Evaluate(track.curve, t));
                    done = t >= 1f;
                }
                else if (track.driver == AnimationDriver.Keyframes)
                {
                    float local;
                    if (track.direction < 0)
                    {
                        track.elapsed = MathF.Max(0f, track.elapsed - dt);
                        local = track.elapsed;
                        done = track.elapsed <= 0f;
                    }
                    else
                    {
                        track.elapsed += dt;
                        local = AnimationLibrary.LocalTime(track.elapsed, track.duration, track.loop);
                        done = track.loop == ClipLoop.Once && track.elapsed >= track.duration;
                    }
                    track.value = AnimationLibrary.Sample(keys, track.firstKey, track.keyCount, local);
                }
                else
                {
                    Spring.Step(ref track.value, ref track.velocity, track.to, track.frequency, track.damping, dt);
                    done = (track.value - track.to).Length() < restDistance && track.velocity.Length() < restSpeed;
                    if (done)
                    {
                        track.value = track.to;
                        track.velocity = Vector4.Zero;
                    }
                }

                WriteTarget(track);

                _outcome[k] = done ? finished : stepped;
                if (i < min) min = i;
                if (i > max) max = i;
            }

            int stat = start / _chunkRows * statStride;
            _stats[stat] = min;
            _stats[stat + 1] = max;
            _stats[stat + 2] = count;
        }

        // Lists layout changes and finished tracks, and drops done tracks from the awake list; returns how many stepped.
        private long Emit(int chunks)
        {
            int total = 0, min = int.MaxValue, max = -1;
            for (int c = 0; c < chunks; c++)
            {
                int stat = c * statStride;
                total += _stats[stat + 2];
                if (_stats[stat + 1] < 0) continue;
                if (_stats[stat] < min) min = _stats[stat];
                if (_stats[stat + 1] > max) max = _stats[stat + 1];
            }

            Span<AnimationTrack> tracks = _tracks.GetSpan<AnimationTrack>();
            Span<int> awake = Animations.Awake;
            int kept = 0;
            for (int k = 0; k < awake.Length; k++)
            {
                int i = awake[k];
                byte outcome = _outcome[k];
                _outcome[k] = 0;

                ref AnimationTrack track = ref tracks[i];
                if (outcome != 0)
                {
                    if (track.changed != LayoutChange.None)
                    {
                        int row = _dirty.Append();
                        _dirty.GetSpan<DirtyLayout>()[row] = new DirtyLayout { target = track.target, change = track.changed };
                    }

                    bool done = outcome == finished;
                    bool finishes = track.driver == AnimationDriver.Tween || (track.driver == AnimationDriver.Keyframes && !track.hold);
                    if (!done)
                    {
                        awake[kept++] = i;
                        continue;
                    }
                    if (finishes)
                    {
                        track.driver = AnimationDriver.None;
                        int row = _done.Append();
                        _done.GetSpan<FinishedTrack>()[row] = new FinishedTrack { track = i, generation = track.generation };
                    }
                }
                track.sleeping = true;
            }
            Animations.KeepAwake(kept);

            if (max >= 0) _tracks.MarkRangeDirty(min, max);
            return total;
        }

        // Writes a track's value into the pool row its property is stored in.
        private static void WriteTarget(in AnimationTrack track)
        {
            Span<byte> row = DataManager.Get(track.target.PoolId).ElementBytes(track.column, track.target);
            if (row.IsEmpty) return;

            Span<byte> field = row.Slice(track.offset);
            Vector4 value = track.value;
            if (track.mapped)
                value = value.X <= 1f ? Vector4.Lerp(track.rest, track.hover, value.X) : Vector4.Lerp(track.hover, track.press, value.X - 1f);
            if (track.width == 1) MemoryMarshal.Write(field, in value.X);
            else if (track.width == 2) MemoryMarshal.Write(field, new Vector2(value.X, value.Y));
            else MemoryMarshal.Write(field, in value);
        }

        // Writes the source slots' shown colours into the target slots and fades each back to where it was headed.
        internal void SeedFade(int source, int first, int count, float duration, Curve curve)
        {
            Span<GpuPaint> paints = _paints.GetSpan<GpuPaint>();
            count = Math.Min(count, Math.Min(paints.Length - first, paints.Length - source));
            for (int i = 0; i < count; i++)
            {
                int slot = first + i;
                Vector4 to = paints[slot].color;
                int existing = _fades.FindIndex(f => f.slot == slot);
                if (existing >= 0)
                {
                    to = _fades[existing].to;
                    _fades.RemoveAt(existing);
                }

                Vector4 from = paints[source + i].color;
                paints[slot].color = from;
                _fades.Add(new SlotFade { slot = slot, from = from, to = to, duration = duration, curve = curve });
            }

            if (count > 0) _paints.MarkRangeDirty(first, first + count - 1);
        }

        private void StepFades(float dt)
        {
            if (_fades.Count == 0) return;

            Span<GpuPaint> paints = _paints.GetSpan<GpuPaint>();
            int min = int.MaxValue, max = -1;
            for (int i = _fades.Count - 1; i >= 0; i--)
            {
                SlotFade fade = _fades[i];
                fade.elapsed += dt;
                float t = fade.duration > 0f ? fade.elapsed / fade.duration : 1f;
                paints[fade.slot].color = Vector4.Lerp(fade.from, fade.to, Curve.Evaluate(fade.curve, t));
                if (fade.slot < min) min = fade.slot;
                if (fade.slot > max) max = fade.slot;

                if (t >= 1f) _fades.RemoveAt(i);
                else _fades[i] = fade;
            }
            _paints.MarkRangeDirty(min, max);
        }
    }
}
