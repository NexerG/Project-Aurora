using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Threading;
using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Animation
{
    // Steps every active track and posts its value to the thread that owns the target.
    [A_XSDType("Animation", "Systems")]
    public sealed class AnimationSystem : ThreadedSystem
    {
        // Post kinds
        public const ushort requestKind = 1;
        public const ushort valueKind = 2;
        public const ushort fadeSeededKind = 3;

        // spring rest thresholds
        private const float restDistance = 1e-3f;
        private const float restSpeed = 1e-3f;

        protected override double TargetPeriodMs => 1000.0 / 120.0;

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
        private long _lastTick;

        // paint slots mid-fade, and seeded acknowledgements still to post
        private readonly List<SlotFade> _fades = new List<SlotFade>();
        private readonly List<uint> _unsentSeeded = new List<uint>();

        protected override void OnStart()
        {
            _tracks = DataManager.Get("Animations");
            _signals = DataManager.Get("Signals");
            _paints = DataManager.Get("Paints");
            _keys = DataManager.Get("Keyframes");
        }

        protected override void OnPost(ushort kind, ReadOnlySpan<byte> payload)
        {
            if (kind != requestKind) return;
            Profiling.Zone.Increment("Anim.Request");

            AnimationRequest request = MemoryMarshal.Read<AnimationRequest>(payload);
            if (request.op == AnimationOp.SetSignal)
            {
                while (_signals.Count <= request.track)
                {
                    int slot = _signals.Append();
                    _signals.GetSpan<SignalValue>()[slot] = default;
                }
                _signals.GetSpan<SignalValue>()[request.track].value = request.to;
                _signals.MarkRangeDirty(request.track, request.track);
                return;
            }
            if (request.op == AnimationOp.FadeSlots)
            {
                SeedFade(request);
                return;
            }

            while (_tracks.Count <= request.track)
            {
                int row = _tracks.Append();
                _tracks.GetSpan<AnimationTrack>()[row] = default;
            }

            ref AnimationTrack track = ref _tracks.GetSpan<AnimationTrack>()[request.track];
            switch (request.op)
            {
                case AnimationOp.Tween:
                    track = new AnimationTrack
                    {
                        driver = AnimationDriver.Tween,
                        generation = request.generation,
                        follow = -1,
                        curve = request.curve,
                        from = request.from,
                        to = request.to,
                        value = request.from,
                        duration = request.duration
                    };
                    break;
                case AnimationOp.Spring:
                    track = new AnimationTrack
                    {
                        driver = AnimationDriver.Spring,
                        sleeping = true,
                        generation = request.generation,
                        follow = request.follow,
                        to = request.from,
                        value = request.from,
                        frequency = request.frequency,
                        damping = request.damping
                    };
                    break;
                case AnimationOp.Keyframes:
                    track = new AnimationTrack
                    {
                        driver = AnimationDriver.Keyframes,
                        generation = request.generation,
                        follow = -1,
                        duration = request.duration,
                        firstKey = request.source,
                        keyCount = request.count,
                        loop = request.loop,
                        direction = 1,
                        hold = request.hold
                    };
                    break;
                case AnimationOp.Direction:
                    if (track.generation != request.generation || track.driver != AnimationDriver.Keyframes) return;
                    if (request.direction < 0 && track.direction > 0)
                        track.elapsed = AnimationLibrary.LocalTime(track.elapsed, track.duration, track.loop);
                    track.direction = request.direction;
                    track.sleeping = false;
                    break;
                case AnimationOp.Retarget:
                    if (track.generation != request.generation) return;
                    track.from = track.value;
                    track.to = request.to;
                    track.elapsed = 0f;
                    track.sleeping = false;
                    break;
                case AnimationOp.Stop:
                    if (track.generation == request.generation) track.driver = AnimationDriver.None;
                    break;
            }
            _tracks.MarkRangeDirty(request.track, request.track);
        }

        protected override void Tick()
        {
            long now = Stopwatch.GetTimestamp();
            float dt = _lastTick == 0 ? 0f : (float)((now - _lastTick) / (double)Stopwatch.Frequency);
            _lastTick = now;

            Span<AnimationTrack> tracks = _tracks.GetSpan<AnimationTrack>();
            Span<SignalValue> signals = _signals.GetSpan<SignalValue>();
            ReadOnlySpan<Keyframe> keys = _keys.GetSpan<Keyframe>();
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

                bool finishes = track.driver == AnimationDriver.Tween || (track.driver == AnimationDriver.Keyframes && !track.hold);
                AnimationValue message = new AnimationValue
                {
                    track = i,
                    generation = track.generation,
                    value = track.value,
                    done = done && finishes
                };
                bool posted = Post(Engine.mainSystem, valueKind, message);
                Profiling.Zone.Increment(posted ? "Anim.ValuePosted" : "Anim.ValueRefused");
                if (posted && done)
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
            for (int i = _unsentSeeded.Count - 1; i >= 0; i--)
                if (Post(Engine.mainSystem, fadeSeededKind, new FadeSeeded { request = _unsentSeeded[i] }))
                    _unsentSeeded.RemoveAt(i);
            DataManager.FrameEdge(this);
        }

        // Writes the source slots' shown colours into the target slots and fades each back to where it was headed.
        private void SeedFade(in AnimationRequest request)
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
            _unsentSeeded.Add(request.generation);
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
