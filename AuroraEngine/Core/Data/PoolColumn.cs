using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Data
{
    // Type-erased handle to one dense component array. Lets DataPool perform structural
    // work (grow / move / permute) across all columns uniformly without knowing T, while
    // typed access goes through the generic PoolColumn<T> via a cast.
    public interface IPoolColumn
    {
        Type ElementType { get; }
        void Grow(int newCapacity);
        void Move(int from, int to);            // dense[to] = dense[from]
        void Permute(int[] destToSrc, int count); // new[i] = old[destToSrc[i]] for i in [0,count)

        // Byte-level writes, for applying a drained SystemCommand whose payload sits in a producer's
        // arena as raw bytes. The drain loop knows the column id but not T, and bytes keep it that
        // way — a generic dispatch table keyed on column type buys nothing when the payload is
        // already untyped.
        int ElementSize { get; }
        void WriteBytes(int dense, int count, ReadOnlySpan<byte> src); // src holds count elements
        void FillBytes(int dense, int count, ReadOnlySpan<byte> src);  // src holds one element
        void CopyWithin(int destDense, int srcDense, int count);
    }

    public sealed class PoolColumn<T> : IPoolColumn where T : struct
    {
        public T[] data;

        public PoolColumn(int capacity) => data = new T[capacity];

        public Type ElementType => typeof(T);

        public void Grow(int newCapacity)
        {
            T[] bigger = new T[newCapacity];
            Array.Copy(data, bigger, data.Length);
            data = bigger;
        }

        public void Move(int from, int to) => data[to] = data[from];

        public int ElementSize => Unsafe.SizeOf<T>();

        public void WriteBytes(int dense, int count, ReadOnlySpan<byte> src)
            => MemoryMarshal.Cast<byte, T>(src).Slice(0, count).CopyTo(data.AsSpan(dense, count));

        public void FillBytes(int dense, int count, ReadOnlySpan<byte> src)
            => data.AsSpan(dense, count).Fill(MemoryMarshal.Cast<byte, T>(src)[0]);

        public void CopyWithin(int destDense, int srcDense, int count)
            => Array.Copy(data, srcDense, data, destDense, count);

        public void Permute(int[] destToSrc, int count)
        {
            // Gather into a temp then copy back — arbitrary permutation can't be done in
            // place with Array.Copy. Runs only at a frame edge on resequence, so the temp
            // allocation is acceptable.
            T[] tmp = new T[count];
            for (int i = 0; i < count; i++)
                tmp[i] = data[destToSrc[i]];
            Array.Copy(tmp, data, count);
        }
    }
}
