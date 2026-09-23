using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Threading;
using ArctisAurora.Core.UI;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Animation
{
    // Steps every active track into its pool row and the value list Main applies.
    [A_XSDType("Animation", "Systems")]
    public sealed class AnimationSystem : ThreadedSystem
    {
        // spring rest thresholds
        private const float restDistance = 1e-3f;
        private const float restSpeed = 1e-3f;

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
            Span<AnimationTrack> tracks = _tracks.GetSpan<AnimationTrack>();
            ReadOnlySpan<SignalValue> signals = _signals.Backing<SignalValue>().AsSpan(0, _signals.Count);
            ReadOnlySpan<Keyframe> keys = _keys.Backing<Keyframe>().AsSpan(0, _keys.Count);
            int min = int.MaxValue, max = -1;
            Profiling.Zone.Start("Anim.Step");
            for (int i = 0; i < tracks.Length; i++)
            {
                ref AnimationTrack track = ref tracks[i];
                if (track.driver == AnimationDriver.None) continue;
                if (track.follow >= 0 && track.follow < signals.Length && signals[track.follow].value != track.to)
                {
                    track.to = signals[track.follow].value;
                    track.sleeping = false;
                }
                if (track.sleeping) continue;
                Profiling.Zone.Increment("Anim.Stepped");

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

                bool finishes = track.driver == AnimationDriver.Tween || (track.driver == AnimationDriver.Keyframes && !track.hold);
                int row = _values.Append();
                _values.GetSpan<AnimationValue>()[row] = new AnimationValue
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

                if (i < min) min = i;
                max = i;
            }

            Profiling.Zone.End("Anim.Step");

            if (max >= 0) _tracks.MarkRangeDirty(min, max);
            Profiling.Zone.Start("Anim.Fades");
            StepFades(dt);
            Profiling.Zone.End("Anim.Fades");
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
