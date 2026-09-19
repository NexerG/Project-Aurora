using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Threading;
using ArctisAurora.EngineWork;
using System.Numerics;

namespace ArctisAurora.Core.Animation
{
    // Starts and steers animations from the main thread, and applies the values they post back.
    public static class Animations
    {
        private static readonly LogChannel Log = LogChannel.For("Animation");

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

        // slot fades waiting for their seeded acknowledgement
        private static readonly Dictionary<uint, Action> pendingFades = new Dictionary<uint, Action>();
        private static uint nextFade;

        // Fades count paint slots from toFirst, starting from the colours shown at fromFirst; onSeeded runs once they are written.
        public static void FadeSlots(uint fromFirst, uint toFirst, int count, float seconds, Curve curve, Action onSeeded)
        {
            uint request = ++nextFade;
            pendingFades[request] = onSeeded;
            AnimationRequest fade = new AnimationRequest
            {
                op = AnimationOp.FadeSlots,
                track = (int)toFirst,
                source = (int)fromFirst,
                count = count,
                generation = request,
                duration = seconds,
                curve = curve
            };
            if (Send(fade)) return;

            pendingFades.Remove(request);
            onSeeded();
        }

        internal static void OnFadeSeeded(in FadeSeeded seeded)
        {
            if (pendingFades.Remove(seeded.request, out Action? onSeeded)) onSeeded();
        }

        // Eases target's property from its current value to to. Returns None when the request was refused.
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

        // A spring resting at target's current value; Retarget moves it. Returns None when the request was refused.
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

        // Plays a clip on target, one handle per clip track; hold keeps each alive at either end. Empty when refused.
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
                if (handles[i] != AnimationHandle.None) continue;

                for (int j = 0; j < i; j++) Stop(handles[j]);
                return Array.Empty<AnimationHandle>();
            }
            return handles;
        }

        // Runs a playing clip forward, or back toward its start.
        public static void Direct(AnimationHandle[] handles, bool forward)
        {
            foreach (AnimationHandle handle in handles)
            {
                if (!IsLive(handle.id, handle.generation)) continue;
                Send(new AnimationRequest { op = AnimationOp.Direction, track = handle.id, generation = handle.generation, direction = (sbyte)(forward ? 1 : -1) });
            }
        }

        // Does nothing once the animation has finished or been stopped.
        public static void Retarget(AnimationHandle handle, Vector4 to)
        {
            if (!IsLive(handle.id, handle.generation)) return;
            Send(new AnimationRequest { op = AnimationOp.Retarget, track = handle.id, generation = handle.generation, to = to });
        }

        public static void Stop(AnimationHandle handle)
        {
            if (!IsLive(handle.id, handle.generation)) return;
            Send(new AnimationRequest { op = AnimationOp.Stop, track = handle.id, generation = handle.generation });
            Release(handle.id);
        }

        // Applies a posted value when its track is still the one bound to that id.
        internal static void OnValue(in AnimationValue value)
        {
            if (!IsLive(value.track, value.generation)) return;
            Profiling.Zone.Increment("Anim.ValueApplied");
            Binding binding = bindings[value.track];

            binding.property.set(binding.target, value.value);
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
            if (Send(request)) return new AnimationHandle(id, request.generation);
            Release(id);
            return AnimationHandle.None;
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

        internal static bool Send(in AnimationRequest request)
        {
            if (ThreadedSystem.Post(Engine.animationSystem, AnimationSystem.requestKind, request)) return true;
            Profiling.Zone.Increment("Anim.RequestDropped");
            Log.Warn($"request {request.op} for track {request.track} dropped — the lane to the animation system is full.");
            return false;
        }
    }
}
