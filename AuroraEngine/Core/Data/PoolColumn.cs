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
