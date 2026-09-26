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
        public bool mapped;
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
        // mapped spring: what value.X of 0, 1 and 2 writes
        public Vector4 rest;
        public Vector4 hover;
        public Vector4 press;
        // keyframes
        public int firstKey;
        public int keyCount;
        public ClipLoop loop;
        public sbyte direction;
        public bool hold;
        // pool row the value is written into
        public DataHandle target;
        public ushort column;
        public ushort offset;
        public byte width;
        public LayoutChange changed;
    }

    // Animation to layout: a control whose animated write needs a re-measure or re-arrange.
    [StructLayout(LayoutKind.Sequential), A_XSDType("DirtyLayout", "DataPools")]
    public struct DirtyLayout
    {
        public DataHandle target;
        public LayoutChange change;
    }

    // Animation to Main: a track that finished.
    [StructLayout(LayoutKind.Sequential), A_XSDType("FinishedTrack", "DataPools")]
    public struct FinishedTrack
    {
        public int track;
        public uint generation;
    }
}
