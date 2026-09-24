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
    // Steps every active track into its pool row and the value list Main applies.
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
        private DataPool _values = null!;
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
            _values = DataManager.Get("AnimationValues");
        }

        [A_XSDActionDependency("Animation.Step", "Frame")]
        private void Advance()
        {
            long now = Stopwatch.GetTimestamp();
            float dt = _lastTick == 0 ? 0f : (float)((now - _lastTick) / (double)Stopwatch.Frequency);
            _lastTick = now;

            _values.Rewind();
            _dt = dt;
            _signalRows = _signals.Backing<SignalValue>();
            _signalCount = _signals.Count;
            _keyRows = _keys.Backing<Keyframe>();
            _keyCount = _keys.Count;

            int count = _tracks.Count;
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

        // Steps the tracks in [start, end) and their in-place targets; Emit reads what each one did.
        void IJobFor.Execute(int start, int end)
        {
            Span<AnimationTrack> tracks = _tracks.GetSpan<AnimationTrack>();
            ReadOnlySpan<SignalValue> signals = _signalRows.AsSpan(0, _signalCount);
            ReadOnlySpan<Keyframe> keys = _keyRows.AsSpan(0, _keyCount);
            float dt = _dt;
            int min = int.MaxValue, max = -1, count = 0;
            for (int i = start; i < end; i++)
            {
                ref AnimationTrack track = ref tracks[i];
                if (track.driver == AnimationDriver.None) continue;
                if (track.follow >= 0 && track.follow < signals.Length && signals[track.follow].value != track.to)
                {
                    track.to = signals[track.follow].value;
                    track.sleeping = false;
                }
                if (track.sleeping) continue;
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

                if (track.width > 0) WriteTarget(track);

                _outcome[i] = done ? finished : stepped;
                if (i < min) min = i;
                max = i;
            }

            int stat = start / _chunkRows * statStride;
            _stats[stat] = min;
            _stats[stat + 1] = max;
            _stats[stat + 2] = count;
        }

        // Appends the stepped values in track order and retires finished tracks; returns how many stepped.
        private long Emit(int chunks)
        {
            int total = 0;
            for (int c = 0; c < chunks; c++)
                total += _stats[c * statStride + 2];
            for (int n = 0; n < total; n++)
                _values.Append();

            Span<AnimationTrack> tracks = _tracks.GetSpan<AnimationTrack>();
            Span<AnimationValue> values = _values.GetSpan<AnimationValue>();
            int row = 0, min = int.MaxValue, max = -1;
            for (int c = 0; c < chunks; c++)
            {
                int stat = c * statStride;
                int last = _stats[stat + 1];
                if (last < 0) continue;

                int first = _stats[stat];
                if (first < min) min = first;
                max = last;
                for (int i = first; i <= last; i++)
                {
                    byte outcome = _outcome[i];
                    if (outcome == 0) continue;
                    _outcome[i] = 0;

                    ref AnimationTrack track = ref tracks[i];
                    bool done = outcome == finished;
                    bool finishes = track.driver == AnimationDriver.Tween || (track.driver == AnimationDriver.Keyframes && !track.hold);
                    values[row++] = new AnimationValue
                    {
                        track = i,
                        generation = track.generation,
                        value = track.value,
                        done = done && finishes
                    };
                    if (done)
                    {
                        if (finishes) track.driver = AnimationDriver.None;
                        else track.sleeping = true;
                    }
                }
            }

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
            if (track.width == 1) MemoryMarshal.Write(field, in value.X);
            else if (track.width == 2) MemoryMarshal.Write(field, new Vector2(value.X, value.Y));
            else MemoryMarshal.Write(field, in value);
        }

        // Writes the source slots' shown colours into the target slots and fades each back to where it was headed.
        internal void SeedFade(in AnimationRequest request)
        {
            Span<GpuPaint> paints = _paints.GetSpan<GpuPaint>();
            int count = Math.Min(request.count, Math.Min(paints.Length - request.track, paints.Length - request.source));
            for (int i = 0; i < count; i++)
            {
                int slot = request.track + i;
                Vector4 to = paints[slot].color;
                int existing = _fades.FindIndex(f => f.slot == slot);
                if (existing >= 0)
                {
                    to = _fades[existing].to;
                    _fades.RemoveAt(existing);
                }

                Vector4 from = paints[request.source + i].color;
                paints[slot].color = from;
                _fades.Add(new SlotFade { slot = slot, from = from, to = to, duration = request.duration, curve = request.curve });
            }

            if (count > 0) _paints.MarkRangeDirty(request.track, request.track + count - 1);
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
