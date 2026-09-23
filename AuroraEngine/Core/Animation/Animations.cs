using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork;
using System.Numerics;

namespace ArctisAurora.Core.Animation
{
    // Starts and steers animations from the main thread, and applies the values they step.
    public static class Animations
    {
        private static DataPool? _tracks;
        private static DataPool? _signals;
        private static DataPool? _values;

        private static DataPool Tracks => _tracks ??= DataManager.Get("Animations");
        private static DataPool SignalPool => _signals ??= DataManager.Get("Signals");
        private static DataPool Values => _values ??= DataManager.Get("AnimationValues");

        private sealed class Binding
        {
            public object target = null!;
            public AnimatableProperty property = null!;
            public uint generation;
            public bool live;
            public Action? onDone;
        }

        // track id -> binding; ids are reused through free
        private static readonly List<Binding> bindings = new List<Binding>();
        private static readonly Stack<int> free = new Stack<int>();

        // Fades count paint slots from toFirst, starting from the colours shown at fromFirst; onSeeded runs once they are written.
        public static void FadeSlots(uint fromFirst, uint toFirst, int count, float seconds, Curve curve, Action onSeeded)
        {
            Write(new AnimationRequest
            {
                op = AnimationOp.FadeSlots,
                track = (int)toFirst,
                source = (int)fromFirst,
                count = count,
                duration = seconds,
                curve = curve
            });
            onSeeded();
        }

        // Eases target's property from its current value to to.
        public static AnimationHandle Tween(object target, string property, Vector4 to, float seconds, Curve curve, Action? onDone = null)
        {
            int id = Bind(target, property, out Binding binding);
            binding.onDone = onDone;
            AnimationRequest request = new AnimationRequest
            {
                op = AnimationOp.Tween,
                track = id,
                generation = binding.generation,
                curve = curve,
                from = binding.property.get(target),
                to = to,
                duration = seconds
            };
            return Started(id, request);
        }

        // A spring resting at target's current value; Retarget moves it.
        public static AnimationHandle Spring(object target, string property, float frequency, float damping)
            => Spring(target, property, frequency, damping, SignalHandle.None);

        // A spring that chases follow's value whenever it changes.
        public static AnimationHandle Spring(object target, string property, float frequency, float damping, SignalHandle follow)
        {
            int id = Bind(target, property, out Binding binding);
            AnimationRequest request = new AnimationRequest
            {
                op = AnimationOp.Spring,
                track = id,
                generation = binding.generation,
                follow = follow.id,
                from = binding.property.get(target),
                frequency = frequency,
                damping = damping
            };
            return Started(id, request);
        }

        // Plays a clip on target, one handle per clip track; hold keeps each alive at either end.
        public static AnimationHandle[] Play(object target, string clip, bool hold)
        {
            ClipDefinition definition = AnimationLibrary.Clip(clip);
            AnimationHandle[] handles = new AnimationHandle[definition.tracks.Count];
            for (int i = 0; i < handles.Length; i++)
            {
                ClipTrackDefinition track = definition.tracks[i];
                int id = Bind(target, track.property, out Binding binding);
                AnimationRequest request = new AnimationRequest
                {
                    op = AnimationOp.Keyframes,
                    track = id,
                    generation = binding.generation,
                    source = track.firstKey,
                    count = track.keyCount,
                    duration = definition.duration,
                    loop = definition.loop,
                    hold = hold
                };
                handles[i] = Started(id, request);
            }
            return handles;
        }

        // Runs a playing clip forward, or back toward its start.
        public static void Direct(AnimationHandle[] handles, bool forward)
        {
            foreach (AnimationHandle handle in handles)
            {
                if (!IsLive(handle.id, handle.generation)) continue;
                Write(new AnimationRequest { op = AnimationOp.Direction, track = handle.id, generation = handle.generation, direction = (sbyte)(forward ? 1 : -1) });
            }
        }

        // Does nothing once the animation has finished or been stopped.
        public static void Retarget(AnimationHandle handle, Vector4 to)
        {
            if (!IsLive(handle.id, handle.generation)) return;
            Write(new AnimationRequest { op = AnimationOp.Retarget, track = handle.id, generation = handle.generation, to = to });
        }

        public static void Stop(AnimationHandle handle)
        {
            if (!IsLive(handle.id, handle.generation)) return;
            Write(new AnimationRequest { op = AnimationOp.Stop, track = handle.id, generation = handle.generation });
            Release(handle.id);
        }

        // Applies every value Animation stepped this frame.
        internal static void ApplyValues()
        {
            DataPool pool = Values;
            ReadOnlySpan<AnimationValue> values = pool.Backing<AnimationValue>().AsSpan(0, pool.Count);
            for (int i = 0; i < values.Length; i++)
                OnValue(values[i]);
        }

        // Applies a stepped value when its track is still the one bound to that id.
        private static void OnValue(in AnimationValue value)
        {
            if (!IsLive(value.track, value.generation)) return;
            Profiling.Zone.Increment("Anim.ValueApplied");
            Binding binding = bindings[value.track];

            if (binding.property.changed == null) binding.property.set(binding.target, value.value);
            else binding.property.changed(binding.target);
            if (!value.done) return;

            Action? onDone = binding.onDone;
            Release(value.track);
            onDone?.Invoke();
        }

        // Stops every track animating target.
        public static void StopAll(object target)
        {
            for (int id = 0; id < bindings.Count; id++)
                if (bindings[id].live && ReferenceEquals(bindings[id].target, target))
                    Stop(new AnimationHandle(id, bindings[id].generation));
        }

        private static AnimationHandle Started(int id, in AnimationRequest request)
        {
            Write(request);
            return new AnimationHandle(id, request.generation);
        }

        private static int Bind(object target, string property, out Binding binding)
        {
            int id;
            if (free.Count > 0) id = free.Pop();
            else
            {
                id = bindings.Count;
                bindings.Add(new Binding());
            }

            binding = bindings[id];
            binding.target = target;
            binding.property = AnimatableProperty.Of(target.GetType(), property);
            binding.generation++;
            binding.live = true;
            return id;
        }

        private static void Release(int id)
        {
            Binding binding = bindings[id];
            binding.live = false;
            binding.target = null!;
            binding.onDone = null;
            free.Push(id);
        }

        private static bool IsLive(int id, uint generation)
            => id >= 0 && id < bindings.Count && bindings[id].live && bindings[id].generation == generation;

        // Applies a request to the animation pools on the spot.
        internal static void Write(in AnimationRequest request)
        {
            Profiling.Zone.Increment("Anim.Request");
            if (request.op == AnimationOp.SetSignal)
            {
                DataPool signals = SignalPool;
                while (signals.Count <= request.track)
                {
                    int slot = signals.Append();
                    signals.GetSpan<SignalValue>()[slot] = default;
                }
                signals.GetSpan<SignalValue>()[request.track].value = request.to;
                signals.MarkRangeDirty(request.track, request.track);
                return;
            }
            if (request.op == AnimationOp.FadeSlots)
            {
                Engine.animationSystem.SeedFade(request);
                return;
            }

            DataPool tracks = Tracks;
            while (tracks.Count <= request.track)
            {
                int row = tracks.Append();
                tracks.GetSpan<AnimationTrack>()[row] = default;
            }

            ref AnimationTrack track = ref tracks.GetSpan<AnimationTrack>()[request.track];
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
                    Target(ref track, bindings[request.track]);
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
                    Target(ref track, bindings[request.track]);
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
                    Target(ref track, bindings[request.track]);
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
            tracks.MarkRangeDirty(request.track, request.track);
        }

        // Points a started track at the pool row its property is stored in.
        private static void Target(ref AnimationTrack track, Binding binding)
        {
            AnimatableProperty property = binding.property;
            if (property.column == null) return;

            Entity entity = (Entity)binding.target;
            track.target = entity.dataHandle;
            track.column = entity.Pool.ColumnId(property.column);
            track.offset = property.offset;
            track.width = property.width;
        }
    }
}
