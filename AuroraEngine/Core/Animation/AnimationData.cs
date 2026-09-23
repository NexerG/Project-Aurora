using ArctisAurora.Core.Data;
using ArctisAurora.Core.Registry;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Animation
{
    public enum AnimationDriver : byte
    {
        None, Tween, Spring, Keyframes
    }

    public enum AnimationOp : byte
    {
        Tween, Spring, Retarget, Stop, SetSignal, FadeSlots, Keyframes, Direction
    }

    // One key of a clip track: the value at time, eased toward the next key by curve.
    [StructLayout(LayoutKind.Sequential), A_XSDType("Keyframe", "DataPools")]
    public struct Keyframe
    {
        public float time;
        public Curve curve;
        public Vector4 value;
    }

    // A started animation: its track id and the generation it was started under.
    public readonly record struct AnimationHandle(int id, uint generation)
    {
        public static readonly AnimationHandle None = new AnimationHandle(-1, 0);
    }

    // A signal: its id and the generation it was created under.
    public readonly record struct SignalHandle(int id, uint generation)
    {
        public static readonly SignalHandle None = new SignalHandle(-1, 0);
    }

    // One signal's current value, one row per signal id in the Signals pool.
    [StructLayout(LayoutKind.Sequential), A_XSDType("SignalValue", "DataPools")]
    public struct SignalValue
    {
        public Vector4 value;
    }

    // One track's driver state, one row per track id in the Animations pool.
    [StructLayout(LayoutKind.Sequential), A_XSDType("AnimationTrack", "DataPools")]
    public struct AnimationTrack
    {
        public AnimationDriver driver;
        public bool sleeping;
        public uint generation;
        // signal id a spring takes its target from, -1 for none
        public int follow;
        public Curve curve;
        public Vector4 from;
        public Vector4 to;
        public Vector4 value;
        public Vector4 velocity;
        // tween
        public float duration;
        public float elapsed;
        // spring
        public float frequency;
        public float damping;
        // keyframes
        public int firstKey;
        public int keyCount;
        public ClipLoop loop;
        public sbyte direction;
        public bool hold;
        // pool row the value is written into, width 0 for none
        public DataHandle target;
        public ushort column;
        public ushort offset;
        public byte width;
    }

    // Main to Animation: start, retarget or stop a track; set a signal (track is the signal id); fade
    // count paint slots from track, seeded from source; or play count keys from source, turned later
    // by Direction.
    [StructLayout(LayoutKind.Sequential)]
    public struct AnimationRequest
    {
        public AnimationOp op;
        public int track;
        public uint generation;
        public int follow;
        public int source;
        public int count;
        public Curve curve;
        public Vector4 from;
        public Vector4 to;
        public float duration;
        public float frequency;
        public float damping;
        public ClipLoop loop;
        public sbyte direction;
        public bool hold;
    }

    // Animation to Main: a track's value this tick.
    [StructLayout(LayoutKind.Sequential), A_XSDType("AnimationValue", "DataPools")]
    public struct AnimationValue
    {
        public int track;
        public uint generation;
        public Vector4 value;
        public bool done;
    }
}
