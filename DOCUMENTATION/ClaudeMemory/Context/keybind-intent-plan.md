# Keybinds bind to a declared intent, not straight to an action

**Agreed in principle:** user, 2026-08-31. **Nothing built**; two questions still open.
**Checklist form:** the keybind-intent item in `DOCUMENTATION/Work in Progress List.md`.

## Shape

`<AbstractKeybind Name="…" Action="…"/>` names an intent once and holds the action. A
`<Keybind Trigger="…" Bind="…">` then points at the name instead of repeating `Action=`, so **N physical
triggers serve one intent.**

## What it falls out of

Numpad Enter (2026-08-31), which today is a **second** `<Keybind>` copying `Action="Text.NewBlock"` and
its `<Repeat/>`. That duplication is not cosmetic:

- `GestureMatcher.Rebind` moves **every** bind matching an action name.
- So rebinding `Text.NewBlock` lands both definitions on one trigger.
- `IsShadowed` only suppresses fewer-modifier binds, so it does not catch that.
- **The action then fires twice per press.**

## What an intent buys

One thing to address instead of a set, for all three of:

| | |
|---|---|
| the rebind | moves the intent, not every matching action string |
| the conflict check | compares intents |
| "what is this bound to" | displays one row with N triggers |

## Open questions

- Whether **conditions** live on the abstract declaration or per trigger.
- Whether **`Action=` stays legal** as the unnamed shorthand.
