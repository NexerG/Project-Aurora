# Decision — encryption: cipher, password files, sealed blocks, vault lock

**Date:** 2026-10-16
**Scope:** `ArctisAurora.Core.Security` — `Cipher`, `PasswordKey`, `EncryptedFile`, `FileKeys`, `VaultLock`, `WindowsHello`, `LockedFileException`; `ArctisAurora.Core.Filing` — `VaultFiles`; `ArctisAurora.Core.UI` — `PasswordWindow`, `LockedFileControl`, `UnlockPrompts`, `NoteSeal`, `NoteSealed`, `NoteSeals`, `SealedControl`, `SealEdit`, `RecoveryKeyWindow`, `SheetBook`, `SheetFormula`; `Thorium.Editor.CustomControls.VaultBrowserControl`, `Thorium.Editor.VaultsWindow`

## What changed
- Three levels: general cipher (bytes in, bytes out; what server replication will use), password-encrypted single files (notes, sheets, planners) with a locked placeholder tab, password-sealed runs of blocks in a note (`[ENCRYPTED]` button), and whole-vault encryption under a master key opened by password, recovery key or Windows Hello.
- `Cipher`: `keySize`, `overhead`, `NewKey()`, `Encrypt(key, input, associated)`, `TryDecrypt(key, blob, out output, associated)`. Blob = version(1) | alg(1 = AES-256-GCM) | salt(32) | ciphertext | tag(16); subkey and 12-byte nonce from HKDF-SHA256(key, salt, "Aurora.Cipher.v1").
- `PasswordKey` (IDisposable, zeroes key): Argon2id, defaults memoryKiB 65536, passes 3, lanes 4, 16-byte salt. `Create`, `Derive`, `Attributes()`, `FromAttributes(element, password)`.
- `EncryptedFile`: password envelope `<Encrypted Kdf… Salt…>base64</Encrypted>` (AAD "Aurora.File"); vault envelope `<Encrypted Key="Vault">` (AAD "Aurora.VaultFile"). `IsEncrypted(path)` = password envelope only. `LoadXml(path, options, ref key)` takes the vault key via `VaultLock.KeyFor`, or a password key via `key` / `FileKeys.Take`, else throws `LockedFileException`. Writes: `SaveXml`, `Write`, `WriteVault`, `WriteWhole` (temp file beside target + `File.Move` overwrite).
- `FileKeys`: session map of password keys. `Use(path, key, remember)`: remember keeps it for the session, else it is handed to the next load once. `Take`, `Has`, `IsRemembered`, `Renamed`, `Clear`.
- `VaultLock`: `<vault>/.aurora/Vault.lock.xml` = `<VaultLock Version Id State(Locked|Encrypting|Decrypting)>` with `<Slot Kind="Password">`, `<Slot Kind="Recovery">`, `<Slot Kind="WindowsHello">`; each slot is the master key encrypted (AAD "Aurora.VaultSlot") under that way's key. Members: `folderName`, `LockPath`, `HasLock`, `IsOpen`, `KeyFor`, `Covers`, `Refuses`, `Create`, `Open(vault, secret)`, `ChangePassword`, `AddHello`, `OpenWithHello`, `NewRecovery`, `FormatRecovery` (base32, 13 groups of 4), `ParseRecovery`, `Use`, `Remove`, `Close`, `isConverting`. Conversion runs on main in 8 ms slices, re-posted through `Task.Delay(1)` then `Engine.Post`.
- `WindowsHello`: `IsAvailable()`, `Sign(credential, vaultId, create)` (KeyCredentialManager, RSA signature over "Aurora.Vault.Hello."+id), `Delete`.
- `VaultFiles`: `ReadBytes`, `ReadText`, `OpenRead`, `WriteBytes`, `WriteText`, `Copy`, `SaveXml(file, path, PasswordKey?, settings)`.
- UI: `PasswordWindow.Ask(source, title, twice, error, onConfirm, onCancel, alternative)`; `LockedFileControl` (IFileEditor placeholder tab: masked field, "Remember password for this session", Unlock); `UnlockPrompts.Ask(path, reason, use)` (queued); `NoteSeal(key)`; `NoteSealed(element)` : `NoteNode`; `NoteSeals` (`Write`, `Unlock`, `Entries`; `<Sealed Kdf… Salt…>` base64 of a `<Group>` of Block/Table, AAD "Aurora.Seal"); `SealedControl`; `SealEdit` (IEditRecord); `RecoveryKeyWindow.Show`.
- Changed: `TextBoxControl.masked` ('*', refuses copy/cut); `NoteNode.seal`; `BlockSnapshot.seal`; `RichTextDocument.encryption`, `NodeIndexOf`, `SetSeals`, `Load(path, key=null)`; `SheetDocument.encryption`; `PlannerDocument.encryption`; optional key params on `DocumentXml.Load`, `SheetXml.Load`, `PlannerXml.Load`, `SheetDocument.Load`, `PlannerDocument.Load`, `NoteSessions.Open`, `SheetBook.Get`, `PlannerBook.Open`; `DocumentXml.WriteNode` (internal), `ReadEntry`, `Groups` groups seal runs; `NoteSegments.Map` reads `<Sealed>`; `DocumentControl` measure/paginate/arrange/`ShowNodes`/`NodeOf`/`ViewOf` handle `SealedControl`; `DocumentEditorControl.EncryptBlocks`, `UnlockSeal`; `SheetFormula.locked` "#LOCKED!"; `SheetBook.IsLocked`, `Resolve` catches `LockedFileException`, `lockedSeen` cache, `RewriteOnDisk`; `SheetLinks` `Missing()`, `Rewritten()`; `SessionLayout.paused`; `FileBrowserControl.rows` protected, `AcceptsFolder` virtual; `NoteFileSync.Segmented` false for encrypted files and covered/refused vault paths.
- Thorium: `VaultBrowserControl` actions `Notes.Encrypt`, `Notes.Decrypt`, `Vault.Unlock`, `Vault.Lock`, `Vault.ChangePassword`, `Vault.EnableHello`, `Vault.RemoveLock`; `BuildLockedTab`; `AcceptsFolder` hides dot-folders; locked browser shows "Vault locked - Unlock"; `VaultsWindow.Switch` asks a locked vault's key first, `Enter(path, master)`, rows show "(encrypted)"; `Thorium.Main` defers `SessionLayout.Restore` behind the unlock; `TextInputActions.EncryptBlocks` (`Notes.EncryptBlocks`).
- Data: Thorium menus `VaultNoteXml.menu.xml`, `Note.menu.xml`, `Vault.menu.xml`. Tests: AuroraEngine `Crypto.tests.xml` (RoundTrip, Rejects, Argon2idVector, PasswordKey, EncryptedFiles, SealedBlocks), `Vault.tests.xml` (LockRoundTrip, WrongPassword, ConvertResumes, HelloSlot); Thorium `Encryption.tests.xml` (LockedTab, LockedSheets, SealedView).
- NuGet: Konscious.Security.Cryptography.Argon2 1.3.1 on AuroraEngine.

## Why these choices

**AES-256-GCM, not ChaCha20-Poly1305.**
Hardware-accelerated everywhere and supported on every .NET platform (BCL `AesGcm`). A per-message 32-byte salt gives an HKDF subkey and nonce, so random-nonce limits per key never apply. Version and alg bytes leave room for agility.

**Argon2id via Konscious, not PBKDF2, NSec/libsodium or a hand-written Argon2.**
PBKDF2 is the "old tech" ruled out. NSec/libsodium need native binaries per platform. Hand-written Argon2 is rolling crypto. Konscious is pure managed and MIT; parameters follow RFC 9106's second set and the RFC test vector is checked in `Crypto.Argon2idVector`.

**Post-quantum posture.**
The symmetric parts (AES-256, Argon2id, HKDF) stay secure against Grover. The Windows Hello slot is RSA-2048 and is NOT post-quantum (TPM 2.0/Hello have no PQ keys). Replication key exchange later should be an ML-KEM-768 hybrid with X25519/ECDH; transport TLS 1.3.

**Files keep their extension; encryption is an XML envelope.**
XML-for-data rule; the envelope is recognised by its root element.

**Locked files throw and show a placeholder.**
Sync loaders throw `LockedFileException`; tabs show a `LockedFileControl` (the user chose both). The key is kept for the session only when "Remember password for this session" is ticked; otherwise it lives with the open document and is zeroed when the last view closes. Restored sessions bring locked tabs back as placeholders.

**Encrypted notes skip in-place segment writes.**
Whole write via temp + replace, so the crash journal never holds plaintext.

**Cross-file sheet rewrites prompt for a locked sheet's password.**
The user's fork I choice; the alternative was skip + warn. Every encrypted sheet prompts on each rename/shift because there is no way to tell whether it holds a reference without decrypting. Remembered keys skip the prompt. Formulas and note links into a locked sheet show `#LOCKED!`.

**`SheetBook` caches locked paths by stat.**
Length + write time instead of parse + exception per recalc; before it, each formula into a locked sheet cost a full XDocument parse, a thrown exception and a second open per recalc. The cache holds no secret, so a stale entry can only cause an extra `#LOCKED!` or one recheck. `FileKeys.Has` bypasses it.

**Sealed blocks: a locked seal is its own `NoteNode`.**
`NoteSealed` counts as zero flat blocks, so caret and selection never enter it. An unlocked seal is ordinary nodes sharing a `NoteSeal` reference, grouped by `DocumentXml.Groups` like footnote/float runs, so a seal is one segment and in-place segment writes still work. The seal propagates through `SplitAt`, inserted fragments (take the head's seal), merges (`head.seal ??= tail.seal`) and table rebuilds (`PutTable`), so no edit inside a seal writes plaintext. Rejected: a `NoteBlock` subclass placeholder, which needs edit guards at every entry point.

**Vault: one random master key wrapped per slot.**
Password slot is Argon2id, recovery key is HKDF (already 256 random bits), Windows Hello is HKDF over a deterministic RSA signature, so changing the password re-encrypts nothing. Conversion runs on the main thread in 8 ms slices so it serialises with saves (a background thread could write stale plaintext-derived bytes over a newer save); each file is swapped in whole and `State` in the lock file lets an interrupted run resume. `VaultLock.Refuses` makes any write into an encrypted vault that is not the open one throw rather than land in plaintext. File and folder names stay plaintext. Exports (PDF, CSV) stay plain on purpose so other apps can open them.

**`Engine.Post` drains until empty.**
A posted action must not re-post itself directly (it would spin in one drain); `UnlockPrompts` and conversion re-post through `Task.Delay`.

**Windows Hello via WinRT `KeyCredentialManager`, not WebAuthn PRF.**
WebAuthn PRF means webauthn.dll, more interop and unclear platform-authenticator PRF support. Lock actions live in the vault browser's ground menu acting on the current vault (the plan had them on `VaultsWindow` rows; rows only show "(encrypted)").

## Verification
- test-verified: full suite 339 passed, 1 failed (Boot, pre-existing baseline error), 1 skipped. Crypto 6/6, Vault 4/4, Encryption 3/3.
- Not GUI-verified: no password window, recovery-key window, `[ENCRYPTED]` click, startup unlock or real Windows Hello prompt was driven in the running app; tests call the unlock code directly and the Hello slot is tested with a stand-in signature.

## Known gaps
- Unlocking a sealed block uses `PasswordWindow`, which has no "remember" checkbox.
- Unlocked sealed blocks have no visual marker; Enter at the end of a seal continues inside it; no "remove encryption from blocks" command; a locked seal can't be deleted until unlocked.
- Pictures in a sealed run are stored by absolute path; picture files are only encrypted by a vault lock, not by a seal or a per-file password.
- An outside write reloading a note re-locks its unlocked seals.
- Encrypted files always open in the normal editor, never as XML; renaming one doesn't update its stored display name.
- An unlocked sheet stays decrypted in `SheetBook` memory until a vault switch.
- Crash journals (`%LOCALAPPDATA%\Arktis\Journal`) can hold plaintext from segment writes made before a vault was locked, until those notes close.
- A file that fails to convert stays plaintext until the next unlock retries it.
- Changing the vault password doesn't ask for the old one (the vault is already open).
- The Windows Hello prompt is not parented to the app window and may appear behind it.
- The .NET string holding a typed password can't be wiped; only derived keys are zeroed.
- E4 (sealing parts of sheets, by page) not started.
- Not committed.

Related: [[sheets]], [[vault-list-and-switching]], [[vault-browser-and-shell]]
