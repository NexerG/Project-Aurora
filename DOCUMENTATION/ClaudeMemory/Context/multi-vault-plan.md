# Multi-vault workspace — stages A and B landed, C to re-plan, D open

**Agreed:** 2026-10-22. **Stage A landed 2026-10-22** (test-verified, no UI), then partly retired. **Stage B landed 2026-10-22**, redesigned by the user the same day (test-verified, NOT GUI-verified). Stage C needs re-planning; D untouched.
**What was built:** `../Decisions/multi-vault-workspace.md`.
**Checklist form:** the multi-vault entry in `DOCUMENTATION/Work in Progress List.md`.

## Stages

| Stage | Content | Status |
|---|---|---|
| A | Foundation: `VaultLock` one key per open vault, home-vault sheet lookups, tear-offs keep `sessionScope`, `RenderWindow.anchor`, `Engine.SetTitle`. Its `SessionLayout.Open`/`Close`/`closedWindows` were removed in B | landed, test-verified; session part retired |
| B | Vault windows on the global model below: `VaultWindows`, `ClosedVaultControl`, `VaultBrowserControl` per vault, `RenderWindow.roots`/`onClose`, window-taking `Background` | landed, redesigned, test-verified, NOT GUI-verified |
| C | Sticky single-colour palettes + stickies browser window | re-plan on the global-sticky model |
| D | Windows taskbar jump list | open |

## The model (user, 2026-10-22)
- Stickies are one global system, separate from vaults. A sticky holds its own files (Stickies folder) and links to vault files; a link to a closed or locked vault shows `ClosedVaultControl` and revives when the vault is open in any window. Stickies never hold a vault key.
- An editor window lists one or more vaults (`RenderWindow.roots`, recorded as `SessionRoot`), one `VaultBrowserControl` per vault under a header. The same vault may be in several windows.
- One global session layout, partition `workspace`; each window record lists its vaults.
- A vault is open while some editor window lists it. When none does: its tabs in non-sticky windows close, sticky tabs of its files become placeholders, an encrypted vault's key is dropped (`VaultLock.Close(vault)`).
- Opens=VaultBrowser (every launch): picker over a hidden empty primary. Opens=LastVault: restore the recorded windows.
- Closing an editor window: another shown editor window exists → notes saved, window closes (the primary is emptied and hidden instead, since the renderer's device is tied to it; the next new window reuses it). The last shown editor window gets Close / Tray / Cancel; Tray hides that window.
- An editor window whose last vault is closed stays open and empty.
- The `Vault` setting is read-only input (upgrade and fallback); switching no longer exists.

## Stage C — stickies (to re-plan)
- Earlier idea: single-colour palettes `sticky-yellow`, `sticky-green`, `sticky-pink`, `sticky-purple`, `sticky-blue`, `sticky-grey`, `sticky-charcoal`; colour in the note's frontmatter `palette`; hidden from Settings › UI. Stickies browser window.
- The old "lists free stickies only" no longer means anything: all stickies are global. Nothing re-designed yet.

## Stage D — Windows taskbar jump list
- Categories Vaults and Stickies. Arguments `--vault`, `--sticky`, `--stickies`, `--new-sticky`.
- Arguments are forwarded to the running copy over WM_COPYDATA. Single instance whenever the tray is enabled.
- A jump-list launch opens only what was asked.

## Retracted from the earlier plan
- Key retained while a sticky shows an encrypted note of the vault; `SessionLayout.Close` with a keep-predicate; `KnownVault.Open`; the picker hiding once a vault opens; opening an already-open vault raising its window; one editor window per vault.

Related: [[multi-vault-workspace]], [[vault-list-and-switching]], [[start-at-login]], [[desktop-stickies]]
