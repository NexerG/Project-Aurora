# Loops — rules 38 and 42

Right/wrong pairs for the rules in `SKILL.md` whose form one line doesn't show.

## 38. Loop to the array's own length
```csharp
// WRONG - bound is a separate count
for (int i = 0; i < count; i++)
    sum += values[i];

// WRONG - array read through a field
for (int i = 0; i < this.values.Length; i++)
    sum += this.values[i];

// RIGHT
float[] local = values;
for (int i = 0; i < local.Length; i++)
    sum += local[i];

// RIGHT - a live prefix
ReadOnlySpan<float> live = values.AsSpan(0, count);
for (int i = 0; i < live.Length; i++)
    sum += live[i];
```

## 42. Iteration cap on a data-driven loop
```csharp
// WRONG
while (walker != start)
    walker = NextCorner(walker, ref forward);

// RIGHT
int steps = 0;
while (walker != start)
{
    walker = NextCorner(walker, ref forward);
    if (++steps > maxSteps)
    {
        Log.Once().Warn($"contour walk on layer {layer} stopped after {steps} steps at {walker}");
        break;
    }
}
```
