# Types — rules 30–35

Right/wrong pairs for the rules in `SKILL.md` whose form one line doesn't show.

## 30. Struct for values, class for identity
```csharp
// WRONG
sealed class Hit
{
    public Vector2 point;
    public int edge;
}

// RIGHT
struct Hit
{
    public Vector2 point;
    public int edge;
}
```

## 31. `readonly`
```csharp
// WRONG - a call through `in` copies the struct first
struct Segment
{
    public Vector2 a;
    public Vector2 b;

    public float Length() => Vector2.Distance(a, b);
}

// RIGHT
readonly struct Segment
{
    public readonly Vector2 a;
    public readonly Vector2 b;

    public Segment(Vector2 a, Vector2 b)
    {
        this.a = a;
        this.b = b;
    }

    public float Length() => Vector2.Distance(a, b);
}

// RIGHT - mutable, with the non-mutating members marked
struct SparkMotion
{
    public Vector2 position;
    public Vector2 velocity;

    public readonly Vector2 Next(float dt) => position + velocity * dt;
}
```

## 32. Several results
```csharp
// WRONG
public Vector2[] Bounds(ReadOnlySpan<Vector2> ring)

// RIGHT
public void Bounds(ReadOnlySpan<Vector2> ring, out Vector2 min, out Vector2 max)
```

## 33. `in` to read, `ref` to write
```csharp
// WRONG
public static float Area(Triangle t)
public static Rect Grow(Rect r, float by)

// RIGHT
public static float Area(in Triangle t)
public static void Grow(ref Rect r, float by)
```

## 34. No interface calls in a data loop
```csharp
// WRONG
IShape[] shapes;
for (int i = 0; i < shapes.Length; i++)
    total += shapes[i].Area();

// RIGHT
static float SumAreas<T>(ReadOnlySpan<T> shapes) where T : struct, IShape
{
    float total = 0;
    for (int i = 0; i < shapes.Length; i++)
        total += shapes[i].Area();
    return total;
}
```

## 35. No boxing
```csharp
// WRONG
static bool Same(IEquatable<CellKey> a, CellKey b) => a.Equals(b);

// RIGHT
static bool Same<T>(T a, T b) where T : struct, IEquatable<T> => a.Equals(b);
```
