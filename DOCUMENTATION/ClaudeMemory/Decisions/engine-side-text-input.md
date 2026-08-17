# Decision — text input is the engine's, keybind timings are global settings

**Date:** 2026-08-17
**Status:** LANDED. Boots clean; save round-trip verified. Caret navigation is compile-verified only.
**Scope:** `ArctisAurora.EngineWork` (`InputSettings`, `KeyStateTracker`, `RepeatCondition`),
`ArctisAurora.Core.UISystem.Controls.Text` (`TextInputActions`, `TextControl.Layout`),
`...Text.Document` (`DocumentControl`, `DocumentEditorControl`, `DocumentEditSession`, `DocumentXml`),
`Periodic/Editor/Decorations.cs`, `Periodic/Data/XML/Documents/Inputs/InputMap.xml`.

## The ask

User, 2026-08-17: full input processing on the engine side; P3's saving and special-key keybinds;
double-click timeout and repeat delay as **keybind-global** settings.

## Decisions

### 1. Keybind timings are global and a keybind cannot override them

`RepeatCondition`'s `Delay` and `Rate` attributes are **deleted**; `KeyStateTracker.tapWindow` is
**deleted**. All three now read `InputSettings` (`<Input><DoubleClick Timeout/><KeyRepeat Delay
Rate/></Input>`) at the point of use.

Read live rather than seeded at parse time. Seeding is the obvious alternative and it forces a choice
with no good side: either a settings change reaches nothing already parsed, or an `OnChanged` action
walks every condition and cannot tell a keybind's deliberate override from the default it is about to
clobber. Deleting the override removes the question. The read is a dictionary lookup on a handful of
keys per tick.

**Rejected: setting supplies the default, XML still overrides.** A per-keybind repeat rate is a
different feature from a person's repeat rate, and once both exist a settings screen has no defined
behaviour toward a keybind that named its own.

`Hold` / `MaxHoldTime` / `HoldContinuous` / `MultiTap` keep their thresholds — those say what the
gesture *is*, not how fast the machine repeats.

Named `KeyRepeat`, not `Repeat`: settings resolve against their category so `Repeat` would have been
legal, but `AnyXMLType.FindType` matches on name alone and `RepeatCondition` already owns `Repeat`
globally. See [[settings-categories]] decision 2 for why the collision is survivable and why it is
still not worth having.

**Not touched:** `Engine.doubleClickTime = 250` (Engine.cs:34) is dead — no reader anywhere — and now
duplicates `DoubleClick.Timeout`. Left in place; flagged, not deleted.

### 2. Text actions live in the engine, not the host

`TextInputActions` holds `Text.Write` (moved verbatim out of `Periodic.Decorations.Write`),
`Text.Save` and eight `Text.Caret*` actions. The keybind XML stays the authority over which key
reaches which action, so a host still declares its own map — it just no longer writes the code behind
it.

A keybind action is an `Action` with no arguments, so the target can only come from engine state:
every action starts at `UICollisionHandling.activeControl` and either casts it to `TextControl`
(`Text.Write`, plus `isEditing`) or walks up to the nearest `DocumentEditorControl`.

**`ICharacterInput` still does not exist**, and this did not add it. CLAUDE.md has described it for
months; there is no such type. The cast to `TextControl` is what routes characters.

**Plain `TextInputControl` gets no caret actions.** `MoveCursorLeft`/`Right`/`Home`/`End` exist on it
and are still called by nothing. There is no caret rendered outside a document, so moving one would
be invisible — built when a text box needs a caret, not before.

### 3. Caret movement is two mechanisms, not one

**Left/right walk runs in document order.** A run boundary *inside a block* is one caret slot: the
end of run A and offset 0 of run B resolve to the same point, because `TextBlockControl` hands B's
`firstLineOffset` the value of A's `lastLineEndX`. So right from A's last character lands on `(B, 0)`
and left from `(B, 0)` lands on `(A, len-1)`. Without this every run boundary costs a dead keypress.
A *block* boundary is two slots — different lines, different points.

**Everything else resolves a point.** `DocumentControl.CaretAtPoint` scores every **line of every
run**, not every run, because a visual line spans runs. A line closer in y always wins; x only breaks
ties inside a band. One primitive answers up, down, line start, line end, page up and page down.

Scoring lines rather than run rects is forced: `TextBlockControl.Arrange` gives every run the block's
full inner width, so run rects overlap almost completely and a rect test cannot pick between two runs
sharing a line.

Line start/end are the **visual** line's, not the paragraph's (user, 2026-08-17) — which is the only
reason the point primitive is needed for them.

`SetCaret` repoints `UICollisionHandling.activeControl`. Load-bearing: `Text.Write` drains into
whatever the collision handler last made active, so a caret that arrowed into a new run without this
types into the run that was clicked.

### 4. `DocumentEditSession` is not a working copy

`{ document, path }` + `Save()`. The 2026-07 plan and `Rich Text Document.md` both say "working
copy"; that was written when the model was plain data and the view was separate. L1/L2 left the model
*being* the control tree, so a working copy is a second control tree — one `GlyphControl` per
character, twice — against a ceiling [[text-layout-one-measurer]] already records as leaned on
(~56.7k controls on the 400-block note, past `UIModule`'s 50,000).

So: revert is a reload from disk. Undo, when it comes, is an edit log over the live tree.

**No dirty flag.** Nothing displays one and Ctrl+S rewriting an unchanged file is harmless.

### 5. A value a setter computed is not authored content

`ContentBlock.ApplyLayout` writes the styling scheme's size into every run's `fontSize` at load, and
`fontSize` is a persisted `[A_XSDElementProperty]`. A fresh run holds 16 and a Heading1 run holds 34,
so the fresh-instance default check does not catch it: the **first Ctrl+S would stamp `FontSize` onto
every run in the note** and pin it to whatever the scheme said that day.

`DocumentXml.WriteElement` now takes the document's `DocumentLayout` and skips `fontSize` when it
equals `layout.FontSizeFor(effective type)`. A run that authored the same size the scheme resolves to
loses its explicit attribute — it reloads identically, so that is the cheap half of the trade.

**Rejected: separate authored and drawn sizes** on `TextControl` (user's call). Permanently correct
and it unblocks per-run font sizing, but it churns every `fontSize` reader — `Measure`,
`RepointGlyphs`, `OffsetAt`, `CaretAt`, `SplitAt`, `StyleEquals`. Revisit when per-run sizing lands.

`controlColor`'s setter resolves into `controlColorHex` the same way, so `IsResolved` covers it too
(user, 2026-08-17). **The trap:** the hex may only be skipped when the *enum* is itself being
written. `ControlColor`'s default is `red`, so a control that never named a colour has the enum
omitted by the fresh-instance check — dropping the hex as well would lose an authored `ColorHex`
outright. The guard is `controlColor != default && controlColorHex == EnumColorToHex(controlColor)`.

### 6. Two more writer defects, same slice

`xs:boolean` has no `True`, and `Convert.ToString(bool)` produces one — a saved note failed the
schema its own `schemaLocation` names. It still *loaded*, because the reader converts
case-insensitively, which is why it survived unnoticed until a save was reachable from the UI. A
`Format` helper spells bools the XSD way; everything else still goes through `Convert.ToString`.

A complex member is now written only when it has attributes or elements, matching what
`SettingsRegistry.WriteDiff` already did — `RichTextDocument.layout` is never null, so every save
appended an empty `<DocumentLayout />` that read back as the default it already had.

**`SettingsRegistry.WriteDiff` has the same bool defect** at its own `Convert.ToString` call site, so
`<VSync On="true"/>` saves as `On="True"`. Not touched — it is a different writer and was not in
scope.

## Verified

Running `Periodic`, throwaway harness in `Periodic.Main`, since removed:

- Full bootstrap, no stderr, all three threads start. `InputHandler.LoadInputs` **throws** on an
  unresolved action name, so a clean boot proves all ten `Text.*` actions resolve.
- `SettingsTypeSchema.xsd` and `InputTypeSchema.xsd` regenerated — `Input` category present,
  `Repeat` no longer carries `Delay`/`Rate`.
- Save round-trip of `SampleNote.xml` after `ApplyLayout`: **no run gained a `FontSize`** (Heading1
  would have been 34, Heading2 28, body 18), `ControlColor="gray"` stayed alone with no `ColorHex`
  beside it, `Bold="true"` stayed lowercase, and no `<DocumentLayout />` was appended. The output is
  structurally the hand-authored note it was loaded from.
- **Load → save → load → save is byte-identical between the two saves.** This is the check that
  matters for decision 5's colour guard: had the hex been skipped wrongly, the second pass would
  have dropped `ControlColor` and the two files would differ.

**Not verified:** every caret move. Navigation reads `arrangedRect`, so it only means anything inside
a frame — GUI verification, per the manual-verification decision.

Related: [[settings-categories]], [[settings-registry]], [[text-layout-one-measurer]],
[[xml-save-skips-defaults]], [[text-styling-types]]
