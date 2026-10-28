# Allocation and collections — rules 2–11

Right/wrong pairs for the rules in `SKILL.md` whose form one line doesn't show.

## 2. Count, then allocate once
```csharp
// WRONG
List<int> indices = new List<int>();
for (int r = 0; r < ringCount; r++)
    AddRingIndices(r, indices);

// RIGHT
int total = 0;
for (int r = 0; r < ringCount; r++)
    total += (ringStarts[r + 1] - ringStarts[r] - 2) * 3;
int[] indices = new int[total];
```

## 3. Reuse a per-frame buffer
```csharp
// WRONG
public void Step()
{
    int[] visible = new int[pool.Count];
}

// RIGHT
// per-frame scratch
private int[] visible = new int[256];
private int visibleCount;

public void Step()
{
    if (visible.Length < pool.Count)
        Array.Resize(ref visible, pool.Count * 2);
    visibleCount = 0;
}
```

## 4. Scratch space
```csharp
// WRONG
for (int i = 0; i < count; i++)
{
    Span<int> corners = stackalloc int[8];
}

// RIGHT - up to 1 KB, once, outside the loop
Span<int> corners = stackalloc int[8];
for (int i = 0; i < count; i++)
{
}

// RIGHT - over 1 KB
float[] samples = ArrayPool<float>.Shared.Rent(sampleCount);
try
{
}
finally
{
    ArrayPool<float>.Shared.Return(samples);
}
```

## 5. Results go into the caller's buffer
```csharp
// WRONG
public List<int> Triangulate(ReadOnlySpan<Vector2> ring)

// RIGHT
public void Triangulate(ReadOnlySpan<Vector2> ring, Span<int> indices, ref int written)
```

## 6. GPU-mapped memory is written whole
```csharp
// WRONG
GpuStats* stats = (GpuStats*)mapped[image];
stats->frameIndex = frame;
stats->totalTime += dt;

// RIGHT
GpuStats stats = new GpuStats(frame, totalTime);
Unsafe.Write((void*)mapped[image], stats);

// RIGHT - a range of rows
AVulkanBufferHandler.WriteMappedRange(mirrorMapped[image], 0, rows, first, count);
```

## 7. `List<T>` only with a capacity
```csharp
// WRONG
List<Hit> hits = new List<Hit>();

// RIGHT - count unknown until the walk ends
List<Hit> hits = new List<Hit>(expectedHits);

// RIGHT - child elements of an [A_XSDType]
[A_XSDElementProperty("Item", "Data")]
public List<ItemDefinition> items = new List<ItemDefinition>();
```

## 8. Flat runs, not nested collections
```csharp
// WRONG
List<List<Vector2>> rings = new List<List<Vector2>>();

// RIGHT - ring k is points[ringStarts[k]..ringStarts[k + 1]]
Vector2[] points = new Vector2[pointCount];
int[] ringStarts = new int[ringCount + 1];

ReadOnlySpan<Vector2> ring = points.AsSpan(ringStarts[k], ringStarts[k + 1] - ringStarts[k]);
```

## 9. Flat grids
```csharp
// WRONG
bool[,] visited = new bool[width, height];
bool[][] visited = new bool[height][];

// RIGHT
bool[] visited = new bool[width * height];
visited[y * width + x] = true;
```

## 10. Struct keys
```csharp
// WRONG
struct CellKey
{
    public int x;
    public int y;
}

// RIGHT
readonly struct CellKey : IEquatable<CellKey>
{
    public readonly int x;
    public readonly int y;

    public CellKey(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public bool Equals(CellKey other) => x == other.x && y == other.y;
    public override bool Equals(object obj) => obj is CellKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(x, y);
}
```

## 11. Spans for a run of items
```csharp
// WRONG
public float Perimeter(IEnumerable<Vector2> ring)

// RIGHT
public float Perimeter(ReadOnlySpan<Vector2> ring)
```
