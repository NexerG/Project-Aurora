# Multi-vault workspace — agreed plan, stage A landed, B–D not started

**Agreed:** 2026-10-22. **Stage A landed 2026-10-22** (test-verified, no UI). Stages B, C, D: nothing built.
**What stage A did:** `../Decisions/multi-vault-workspace.md`.
**Checklist form:** the multi-vault entry in `DOCUMENTATION/Work in Progress List.md`.

## Stages

| Stage | Content | Status |
|---|---|---|
| A | Foundation: `VaultLock` one key per open vault, home-vault sheet lookups, tear-offs keep `sessionScope`, `SessionLayout.Open`/`Close` per scope, `RenderWindow.anchor`, `Engine.SetTitle` | landed, test-verified |
| B | Vault picker as a secondary window; vault editor windows; close rules (see below) | open |
| C | Sticky single-colour palettes + stickies browser window | open |
| D | Windows taskbar jump list | open |

## Stage B — picker, vault windows, close rules
- Thorium opens into a vault picker; each open vault has its own editor window, titled with the vault's name (`Engine.SetTitle`).
- Primary = editor, never the picker. The picker is a secondary window and hides once a vault opens.
- Opening an already-open vault raises its window.
- Closing a vault closes its editor window and tear-offs (`SessionLayout.Close` with a predicate keeping stickies); the primary is emptied and hidden, not destroyed (renderer's device is tied to it), and the next vault opened goes into it.
- Closing the last vault asks Close / Tray / Cancel (existing close prompt). Close quits and the stickies close with the app; Tray keeps the app and the stickies.
- The picker never opens by itself: only at launch and from the Vaults menu. The tray's Open shows the picker when no vault is open.
- Key rule: an encrypted vault's key stays in memory while an open sticky still shows an encrypted note of it; otherwise it is dropped when the vault closes (`VaultLock.Close(vault)`).
- `KnownVault` gains `Open`. The `Vault` setting is kept as a read-once upgrade input. `Startup.Opens` is reused for every launch (default VaultBrowser; LastVault reopens every vault open at quit).
- Supersedes: `vault-list-and-switching` "`ThoriumSettings.vault.path` is the one you are in" — not yet changed.

## Stage C — stickies
- Single-colour palettes `sticky-yellow`, `sticky-green`, `sticky-pink`, `sticky-purple`, `sticky-blue`, `sticky-grey`, `sticky-charcoal`; the colour is the note's frontmatter `palette`. Hidden from Settings › UI.
- Stickies browser window lists free stickies only.

## Stage D — Windows taskbar jump list
- Categories Vaults and Stickies. Arguments `--vault`, `--sticky`, `--stickies`, `--new-sticky`.
- Arguments are forwarded to the running copy over WM_COPYDATA. Single instance whenever the tray is enabled.
- A jump-list launch opens only what was asked.

## Open
- Nothing designed beyond the forks above; no slice of B–D has detail beyond this file.

Related: [[multi-vault-workspace]], [[vault-list-and-switching]], [[start-at-login]], [[desktop-stickies]]
