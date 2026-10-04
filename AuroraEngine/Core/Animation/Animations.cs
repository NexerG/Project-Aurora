using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Animation
{
    // Starts and steers animations from the main thread, and settles the ones that finish.
    public static class Animations
    {
        private static DataPool? _tracks;
        private static DataPool? _done;
        private static DataPool? _bindings;
        private static DataPool? _awake;

        private static DataPool Tracks => _tracks ??= DataManager.Get("Animations");
        private static DataPool Done => _done ??= DataManager.Get("AnimationDone");
        private static DataPool Bindings => _bindings ??= DataManager.Get("AnimationBindings");
        private static DataPool AwakeList => _awake ??= DataManager.Get("AnimationAwake");

        // track id -> the managed half of its binding, beside its TrackBinding row; ids are reused through free
        private static object[] targets = new object[256];
        private static AnimatableProperty[] properties = new AnimatableProperty[256];
        private static Action?[] doneActions = new Action?[256];
        private static readonly PriorityQueue<int, int> free = new PriorityQueue<int, int>();

        // (target, C# property name) -> the live track id driving it
        private static readonly Dictionary<(object, string), int> byProperty = new();

        // target -> the live track ids animating it
        private static readonly Dictionary<object, List<int>> byTarget = new();

        // signal id -> the live track ids following it
        private static readonly Dictionary<int, HashSet<int>> followers = new();

        // The ids of the tracks the step visits; a row's sleeping flag is false exactly while its id is here.
        internal static Span<int> Awake => MemoryMarshal.Cast<AwakeTrack, int>(AwakeList.GetSpan<AwakeTrack>());

        // Keeps the first count ids of the awake list, where the step moved the ones staying awake.
        internal static void KeepAwake(int count) => AwakeList.Truncate(count);

        // Fades count paint slots from toFirst, starting from the colours shown at fromFirst; onSeeded runs once they are written.
        public static void FadeSlots(uint fromFirst, uint toFirst, int count, float seconds, Curve curve, Action onSeeded)
        {
            Profiling.Zone.Increment("Anim.Request");
            Engine.animationSystem.SeedFade((int)fromFirst, (int)toFirst, count, seconds, curve);
            onSeeded();
        }

        // Eases target's property from its current value to to.
        public static AnimationHandle Tween(object target, string property, Vector4 to, float seconds, Curve curve, Action? onDone = null)
        {
            int id = Bind(target, property);
            doneActions[id] = onDone;
            Vector4 from = properties[id].get(target);
            AnimationHandle handle = Start(id, new AnimationTrack
            {
                driver = AnimationDriver.Tween,
                follow = -1,
                to = to,
                value = from
            }, true);
            Tracks.GetSpan<TweenParams>()[id] = new TweenParams { duration = seconds, from = from, curve = curve };
            return handle;
        }

        // A spring resting at target's current value; Retarget moves it.
        public static AnimationHandle Spring(object target, string property, float frequency, float damping)
            => Spring(target, property, frequency, damping, SignalHandle.None);

        // A spring that chases follow's value whenever it changes.
        public static AnimationHandle Spring(object target, string property, float frequency, float damping, SignalHandle follow)
        {
            int id = Bind(target, property);
            Vector4 from = properties[id].get(target);
            AnimationHandle handle = Start(id, new AnimationTrack
            {
                driver = AnimationDriver.Spring,
                follow = follow.id,
                to = from,
                value = from
            }, Signals.Differs(follow, from));
            Tracks.GetSpan<SpringParams>()[id] = new SpringParams { frequency = frequency, damping = damping };
            return handle;
        }

        // A spring on follow's state, 0 rest, 1 hover, 2 press, writing the value between them into property.
        public static AnimationHandle Spring(object target, string property, float frequency, float damping, SignalHandle follow, Vector4 rest, Vector4 hover, Vector4 press)
        {
            int id = Bind(target, property);
            AnimationHandle handle = Start(id, new AnimationTrack
            {
                driver = AnimationDriver.Spring,
                mapped = true,
                follow = follow.id
            }, Signals.Differs(follow, Vector4.Zero));
            Tracks.GetSpan<SpringParams>()[id] = new SpringParams { frequency = frequency, damping = damping };
            ref TrackCold cold = ref Tracks.GetSpan<TrackCold>()[id];
            cold.rest = rest;
            cold.hover = hover;
            cold.press = press;
            return handle;
        }

        // Plays a clip on target, one handle per clip track; hold keeps each alive at either end.
        public static AnimationHandle[] Play(object target, string clip, bool hold)
        {
            ClipDefinition definition = AnimationLibrary.Clip(clip);
            AnimationHandle[] handles = new AnimationHandle[definition.tracks.Count];
            for (int i = 0; i < handles.Length; i++)
            {
                ClipTrackDefinition track = definition.tracks[i];
                int id = Bind(target, track.property);
                handles[i] = Start(id, new AnimationTrack
                {
                    driver = AnimationDriver.Keyframes,
                    follow = -1,
                    loop = definition.loop,
                    direction = 1,
                    hold = hold
                }, true);
                Tracks.GetSpan<KeyParams>()[id] = new KeyParams { duration = definition.duration, firstKey = track.firstKey, keyCount = track.keyCount };
            }
            return handles;
        }

        // Runs a playing clip forward, or back toward its start.
        public static void Direct(AnimationHandle[] handles, bool forward)
        {
            sbyte direction = (sbyte)(forward ? 1 : -1);
            foreach (AnimationHandle handle in handles)
            {
                if (!IsLive(handle.id, handle.generation)) continue;
                ref AnimationTrack track = ref Row(handle.id);
                if (track.driver != AnimationDriver.Keyframes) continue;
                if (direction < 0 && track.direction > 0)
                    track.elapsed = AnimationLibrary.LocalTime(track.elapsed, Tracks.GetSpan<KeyParams>()[handle.id].duration, track.loop);
                track.direction = direction;
                Wake(handle.id);
            }
        }

        // Does nothing once the animation has finished or been stopped.
        public static void Retarget(AnimationHandle handle, Vector4 to)
        {
            if (!IsLive(handle.id, handle.generation)) return;
            ref AnimationTrack track = ref Row(handle.id);
            Tracks.GetSpan<TweenParams>()[handle.id].from = track.value;
            track.to = to;
            track.elapsed = 0f;
            Wake(handle.id);
        }

        public static void Stop(AnimationHandle handle)
        {
            if (!IsLive(handle.id, handle.generation)) return;
            Row(handle.id).driver = AnimationDriver.None;
            Release(handle.id);
        }

        // Releases the tracks Animation finished last frame and runs their onDone.
        internal static void DrainDone()
        {
            DataPool pool = Done;
            int count = pool.Count;
            if (count == 0) return;

            FinishedTrack[] rows = pool.Backing<FinishedTrack>();
            for (int i = 0; i < count; i++)
            {
                ref readonly FinishedTrack finished = ref rows[i];
                if (!IsLive(finished.track, finished.generation)) continue;

                Action? onDone = doneActions[finished.track];
                Release(finished.track);
                onDone?.Invoke();
            }
            pool.Rewind();
        }

        // Stops every track animating target.
        public static void StopAll(object target)
        {
            if (!byTarget.TryGetValue(target, out List<int>? ids)) return;
            for (int i = ids.Count - 1; i >= 0; i--)
                Stop(new AnimationHandle(ids[i], Bindings.GetSpan<TrackBinding>()[ids[i]].generation));
        }

        // Stores a started track in its row, pointed at the pool field its property lives in; wake puts it on the step's list.
        private static AnimationHandle Start(int id, in AnimationTrack started, bool wake)
        {
            ref AnimationTrack track = ref Row(id);
            bool sleeping = track.sleeping;
            track = started;
            track.sleeping = sleeping;
            ref TrackBinding binding = ref Bindings.GetSpan<TrackBinding>()[id];
            Tracks.GetSpan<TrackCold>()[id].generation = binding.generation;

            Entity entity = (Entity)targets[id];
            AnimatableProperty property = properties[id];
            track.target = entity.dataHandle;
            track.column = entity.Pool.ColumnId(property.column);
            track.offset = property.offset;
            track.width = property.width;
            track.changed = property.changed;

            binding.follow = started.follow;
            if (started.follow >= 0)
            {
                if (!followers.TryGetValue(started.follow, out HashSet<int>? ids)) followers[started.follow] = ids = new HashSet<int>();
                ids.Add(id);
            }

            if (wake) Wake(id);
            return new AnimationHandle(id, binding.generation);
        }

        // Replaces any live track on the same property of the same target.
        private static int Bind(object target, string property)
        {
            AnimatableProperty resolved = AnimatableProperty.Of(target.GetType(), property);
            DataPool bindings = Bindings;
            if (byProperty.TryGetValue((target, resolved.name), out int existing))
                Stop(new AnimationHandle(existing, bindings.GetSpan<TrackBinding>()[existing].generation));

            int id;
            if (free.Count > 0) id = free.Dequeue();
            else
            {
                id = bindings.Append();
                bindings.GetSpan<TrackBinding>()[id] = new TrackBinding { follow = -1 };
                if (id == targets.Length)
                {
                    Array.Resize(ref targets, id * 2);
                    Array.Resize(ref properties, id * 2);
                    Array.Resize(ref doneActions, id * 2);
                }
            }

            ref TrackBinding binding = ref bindings.GetSpan<TrackBinding>()[id];
            binding.generation++;
            binding.live = true;
            targets[id] = target;
            properties[id] = resolved;
            byProperty[(target, resolved.name)] = id;
            if (!byTarget.TryGetValue(target, out List<int>? ids)) byTarget[target] = ids = new List<int>();
            ids.Add(id);
            return id;
        }

        private static void Release(int id)
        {
            Span<TrackBinding> bindings = Bindings.GetSpan<TrackBinding>();
            ref TrackBinding binding = ref bindings[id];
            object target = targets[id];
            byProperty.Remove((target, properties[id].name));
            List<int> ids = byTarget[target];
            ids.Remove(id);
            if (ids.Count == 0) byTarget.Remove(target);
            if (binding.follow >= 0)
            {
                HashSet<int> following = followers[binding.follow];
                following.Remove(id);
                if (following.Count == 0) followers.Remove(binding.follow);
                binding.follow = -1;
            }
            binding.live = false;
            targets[id] = null!;
            doneActions[id] = null;
            free.Enqueue(id, id);

            DataPool tracks = Tracks;
            Span<AnimationTrack> rows = tracks.GetSpan<AnimationTrack>();
            int count = tracks.Count;
            while (count > 0 && !bindings[count - 1].live && rows[count - 1].sleeping)
                count--;
            if (count < tracks.Count) tracks.Truncate(count);
        }

        // False once the animation has finished, been stopped or been replaced.
        public static bool IsLive(AnimationHandle handle) => IsLive(handle.id, handle.generation);

        private static bool IsLive(int id, uint generation)
        {
            Span<TrackBinding> bindings = Bindings.GetSpan<TrackBinding>();
            return id >= 0 && id < bindings.Length && bindings[id].live && bindings[id].generation == generation;
        }

        // The row of track id, appended asleep when the pool has not reached it yet.
        private static ref AnimationTrack Row(int id)
        {
            Profiling.Zone.Increment("Anim.Request");
            DataPool tracks = Tracks;
            while (tracks.Count <= id)
            {
                int row = tracks.Append();
                tracks.GetSpan<AnimationTrack>()[row] = new AnimationTrack { sleeping = true };
            }
            tracks.MarkRangeDirty(id, id);
            return ref tracks.GetSpan<AnimationTrack>()[id];
        }

        // Puts a sleeping track on the list the step visits.
        private static void Wake(int id)
        {
            DataPool tracks = Tracks;
            ref AnimationTrack track = ref tracks.GetSpan<AnimationTrack>()[id];
            if (!track.sleeping) return;

            track.sleeping = false;
            tracks.MarkRangeDirty(id, id);
            DataPool awake = AwakeList;
            int row = awake.Append();
            awake.GetSpan<AwakeTrack>()[row].id = id;
        }

        // Wakes the tracks following a signal whose value just changed.
        internal static void WakeFollowers(int signal)
        {
            if (!followers.TryGetValue(signal, out HashSet<int>? ids)) return;
            foreach (int id in ids) Wake(id);
        }
    }
}
