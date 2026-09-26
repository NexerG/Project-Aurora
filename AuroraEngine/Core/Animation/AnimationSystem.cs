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

        // per-chunk stats, one cache line apart: min row, max row, stepped, kept, dirty, done
        private const int statStride = 16;

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
        private DirtyLayout[] _dirtyRows = Array.Empty<DirtyLayout>();
        private FinishedTrack[] _doneRows = Array.Empty<FinishedTrack>();
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
            if (_dirtyRows.Length < count)
            {
                _dirtyRows = new DirtyLayout[Math.Max(count, _dirtyRows.Length * 2)];
                _doneRows = new FinishedTrack[_dirtyRows.Length];
            }
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

        // Steps awake list entries [start, end) and their in-place targets, keeping the running ones at the front of the range; Emit joins the chunks.
        void IJobFor.Execute(int start, int end)
        {
            Span<AnimationTrack> tracks = _tracks.GetSpan<AnimationTrack>();
            ReadOnlySpan<TweenParams> tweens = _tracks.GetSpan<TweenParams>();
            ReadOnlySpan<KeyParams> clips = _tracks.GetSpan<KeyParams>();
            Span<SpringParams> springs = _tracks.GetSpan<SpringParams>();
            ReadOnlySpan<TrackCold> cold = _tracks.GetSpan<TrackCold>();
            Span<int> awake = Animations.Awake;
            ReadOnlySpan<SignalValue> signals = _signalRows.AsSpan(0, _signalCount);
            ReadOnlySpan<Keyframe> keys = _keyRows.AsSpan(0, _keyCount);
            float dt = _dt;
            int min = int.MaxValue, max = -1, count = 0, kept = 0, dirtyCount = 0, doneCount = 0;
            for (int k = start; k < end; k++)
            {
                int i = awake[k];
                ref AnimationTrack track = ref tracks[i];
                if (track.driver == AnimationDriver.None)
                {
                    track.sleeping = true;
                    continue;
                }
                if (track.follow >= 0 && track.follow < signals.Length) track.to = signals[track.follow].value;
                count++;

                bool done;
                if (track.driver == AnimationDriver.Tween)
                {
                    ref readonly TweenParams tween = ref tweens[i];
                    track.elapsed += dt;
                    float t = tween.duration > 0f ? track.elapsed / tween.duration : 1f;
                    track.value = Vector4.Lerp(tween.from, track.to, Curve.Evaluate(tween.curve, t));
                    done = t >= 1f;
                }
                else if (track.driver == AnimationDriver.Keyframes)
                {
                    ref readonly KeyParams clip = ref clips[i];
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
                        local = AnimationLibrary.LocalTime(track.elapsed, clip.duration, track.loop);
                        done = track.loop == ClipLoop.Once && track.elapsed >= clip.duration;
                    }
                    track.value = AnimationLibrary.Sample(keys, clip.firstKey, clip.keyCount, local);
                }
                else
                {
                    ref SpringParams spring = ref springs[i];
                    Spring.Step(ref track.value, ref spring.velocity, track.to, spring.frequency, spring.damping, dt);
                    done = (track.value - track.to).Length() < restDistance && spring.velocity.Length() < restSpeed;
                    if (done)
                    {
                        track.value = track.to;
                        spring.velocity = Vector4.Zero;
                    }
                }

                WriteTarget(track, cold, i);

                if (track.changed != LayoutChange.None)
                    _dirtyRows[start + dirtyCount++] = new DirtyLayout { target = track.target, change = track.changed };
                if (!done) awake[start + kept++] = i;
                else
                {
                    if (track.driver == AnimationDriver.Tween || (track.driver == AnimationDriver.Keyframes && !track.hold))
                    {
                        track.driver = AnimationDriver.None;
                        _doneRows[start + doneCount++] = new FinishedTrack { track = i, generation = cold[i].generation };
                    }
                    track.sleeping = true;
                }
                if (i < min) min = i;
                if (i > max) max = i;
            }

            int stat = start / _chunkRows * statStride;
            _stats[stat] = min;
            _stats[stat + 1] = max;
            _stats[stat + 2] = count;
            _stats[stat + 3] = kept;
            _stats[stat + 4] = dirtyCount;
            _stats[stat + 5] = doneCount;
        }

        // Joins the chunks' kept tracks into the awake list and appends their layout changes and finished tracks; returns how many stepped.
        private long Emit(int chunks)
        {
            int total = 0, min = int.MaxValue, max = -1, dirtyCount = 0, doneCount = 0;
            for (int c = 0; c < chunks; c++)
            {
                int stat = c * statStride;
                total += _stats[stat + 2];
                dirtyCount += _stats[stat + 4];
                doneCount += _stats[stat + 5];
                if (_stats[stat + 1] < 0) continue;
                if (_stats[stat] < min) min = _stats[stat];
                if (_stats[stat + 1] > max) max = _stats[stat + 1];
            }

            int dirtyRow = dirtyCount > 0 ? _dirty.Append(dirtyCount) : 0;
            int doneRow = doneCount > 0 ? _done.Append(doneCount) : 0;
            Span<DirtyLayout> dirty = _dirty.GetSpan<DirtyLayout>();
            Span<FinishedTrack> done = _done.GetSpan<FinishedTrack>();
            Span<int> awake = Animations.Awake;
            int kept = 0;
            for (int c = 0; c < chunks; c++)
            {
                int stat = c * statStride;
                int start = c * _chunkRows;
                awake.Slice(start, _stats[stat + 3]).CopyTo(awake.Slice(kept));
                kept += _stats[stat + 3];
                _dirtyRows.AsSpan(start, _stats[stat + 4]).CopyTo(dirty.Slice(dirtyRow));
                dirtyRow += _stats[stat + 4];
                _doneRows.AsSpan(start, _stats[stat + 5]).CopyTo(done.Slice(doneRow));
                doneRow += _stats[stat + 5];
            }
            Animations.KeepAwake(kept);

            if (max >= 0) _tracks.MarkRangeDirty(min, max);
            return total;
        }

        // Writes a track's value into the pool row its property is stored in.
        private static void WriteTarget(in AnimationTrack track, ReadOnlySpan<TrackCold> cold, int i)
        {
            Span<byte> row = DataManager.Get(track.target.PoolId).ElementBytes(track.column, track.target);
            if (row.IsEmpty) return;

            Span<byte> field = row.Slice(track.offset);
            Vector4 value = track.value;
            if (track.mapped)
            {
                ref readonly TrackCold c = ref cold[i];
                value = value.X <= 1f ? Vector4.Lerp(c.rest, c.hover, value.X) : Vector4.Lerp(c.hover, c.press, value.X - 1f);
            }
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
