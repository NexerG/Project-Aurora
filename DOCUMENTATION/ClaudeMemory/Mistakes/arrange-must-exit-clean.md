# Mistake — an `Arrange` override that exits with `isArrangeDirty` set kills its whole subtree

**Date:** 2026-08-23
**Found by:** the user, immediately, as "I can't scroll on a note, and keybinds work but the caret
doesn't update".

## The trap

`VulkanControl.InvalidateArrange` bails the moment it meets a control that is already dirty —
**including the control it was called on** — and it is the call that registers the dirty root:

```csharp
public void InvalidateArrange()
{
    if (isArrangeDirty) return;          // <- registers nothing, silently
    isArrangeDirty = true;
    ...
    UILayout.RegisterDirtyRoot(topDirty);
}
```

So a control left permanently dirty is not "scheduled forever", it is **unschedulable**. Every later
invalidate on it, or on anything beneath it, hits that first line and is dropped. The subtree only
ever gets laid out again when something unrelated dirties up to the window root and cascades back
down, which is why the symptom reads as intermittent rather than dead.

## How it happened

`DocumentEditorControl.Arrange` was overridden to perform a deferred caret scroll after
`base.Arrange`. `ScrollableControl.ScrollIntoView` ends in an unconditional `InvalidateArrange()` —
it invalidates whether or not it actually moved the offset — and the override only re-arranged (which
is what clears the flag) when the offset *had* moved. The caret is usually already visible, so the
common path set the flag and left it set.

Calling `InvalidateArrange` from inside `Arrange` also cannot register anything on its own: every
ancestor is mid-pass and still dirty, because a container clears its own flag at the *end* of
`Arrange`, after its children return.

## The rule

**An `Arrange` override must exit with `isArrangeDirty == false`, on every path.** `base.Arrange`
guarantees that; anything done after it that can invalidate must either re-arrange or clear the flag.

And more generally: **calling anything that invalidates layout from inside a layout pass needs
checking.** The invalidate is not what schedules the next pass from in there — a direct re-arrange
is.

Related: [[document-caret-scrolling]]
