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
        void Clear(int dense);                  // dense[i] = default
        void Permute(int[] destToSrc, int count); // new[i] = old[destToSrc[i]] for i in [0,count)

        int ElementSize { get; }

        // One element as raw bytes, for writing a field at a known offset.
        Span<byte> ElementBytes(int dense);
    }

    public sealed class PoolColumn<T> : IPoolColumn where T : struct
    {
        public T[] data;
        private T[] _scratch;

        public PoolColumn(int capacity)
        {
            data = new T[capacity];
            _scratch = new T[capacity];
        }

        public Type ElementType => typeof(T);

        public void Grow(int newCapacity)
        {
            T[] bigger = new T[newCapacity];
            Array.Copy(data, bigger, data.Length);
            data = bigger;
            _scratch = new T[newCapacity];
        }

        public void Move(int from, int to) => data[to] = data[from];

        public void Clear(int dense) => data[dense] = default;

        public int ElementSize => Unsafe.SizeOf<T>();

        public Span<byte> ElementBytes(int dense) => MemoryMarshal.AsBytes(data.AsSpan(dense, 1));

        public void Permute(int[] destToSrc, int count)
        {
            for (int i = 0; i < count; i++)
                _scratch[i] = data[destToSrc[i]];
            Array.Copy(_scratch, data, count);
        }
    }
}
