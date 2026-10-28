# Data pools — rules 14–26

Right/wrong pairs for the rules in `SKILL.md` whose form one line doesn't show. The `Sparks` pool is made up;
every call on it is `DataPool`'s.

```xml
<Pool Name="Sparks" Capacity="1024" Ordered="false" Growth="Multiplicative" GrowthValue="2" Handles="false">
    <Component Type="SparkMotion"/>
    <Component Type="SparkLife"/>
</Pool>
```

## 14. Column structs are unmanaged
```csharp
// WRONG
struct SparkLife
{
    public float remaining;
    public string effectName;
    public Control owner;
}

// RIGHT
struct SparkLife
{
    public float remaining;
    public int effectId;
    public DataHandle owner;
}
```

## 16. `Append` writes every field
```csharp
// WRONG - the rest of the row is whatever was there before
int row = sparks.Append();
sparks.GetSpan<SparkMotion>()[row].position = origin;

// RIGHT
int row = sparks.Append();
sparks.GetSpan<SparkMotion>()[row] = new SparkMotion(origin, velocity);
sparks.GetSpan<SparkLife>()[row] = new SparkLife(lifetime, effectId, owner);
```

## 19. Mark what you write
```csharp
// WRONG
pool.GetRef<ArrangeData>(handle).arranged = rect;

// RIGHT
ref ArrangeData row = ref pool.GetRef<ArrangeData>(handle);
row.arranged = rect;
pool.MarkContentDirty(handle);

// RIGHT - one row, assigned whole
pool.Write(handle, in data);
```

## 20. Fetch the span after anything that adds rows
```csharp
// WRONG - child lies past the end of the span fetched before it
Span<SparkMotion> motion = sparks.GetSpan<SparkMotion>();
int child = SpawnChild(parent);
motion[child].velocity = motion[parent].velocity;

// RIGHT
int child = SpawnChild(parent);
Span<SparkMotion> motion = sparks.GetSpan<SparkMotion>();
motion[child].velocity = motion[parent].velocity;
```

## 22. Keep handles across frames, not dense indices
```csharp
// WRONG
private int focusedRow;

// RIGHT
private DataHandle focused;

int dense = pool.DenseOf(focused);
if (dense < 0) return;
```

## 24. Declare what a step touches
```xml
<!-- WRONG -->
<Step Action="Sparks.Step" Writes="Sparks"/>

<!-- RIGHT - the step also reads Signals -->
<Step Action="Sparks.Step" Reads="Signals" Writes="Sparks"/>
```

## 25. Reading from another thread
```csharp
// WRONG - on the render thread
Span<SparkMotion> motion = sparks.GetSpan<SparkMotion>();

// RIGHT - on the render thread
SparkMotion[] motion = sparks.Backing<SparkMotion>();
int count = sparks.Count;
```

## 26. Poll with your own cursor
```csharp
// WRONG
if (sparks.ContentDirty)
{
    Upload(sparks.DirtyMin, sparks.DirtyMax);
    sparks.ClearDirty();
}

// RIGHT
// one cursor per swapchain image
private PoolCursor[] cursors;

if (cursors[image].TryConsumeContent(out int min, out int max))
    Upload(min, max);
```
