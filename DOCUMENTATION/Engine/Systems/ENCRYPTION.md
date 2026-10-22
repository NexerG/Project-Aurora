---
date: 2026-10-16
Status: Current
tags:
  - Engine
  - d_System
  - d_XML
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[ENCRYPTION]]"
Dependencies:
  - "[[SETTINGS]]"
Implementors:
  - "[[Thorium]]"
Namespace: ArctisAurora.Core.Security
SourceFiles: AuroraEngine/Core/Security/
VerifiedAgainst: 2026-10-16
---
## Overview

Aurora encrypts at three levels. A general AES-256-GCM cipher takes bytes in and gives bytes out, and is what server replication will use later. Password-encrypted single files (notes, sheets, planners) open as a locked placeholder tab until the password is given. Password-sealed runs of blocks inside a note show as an [ENCRYPTED] button. Whole-vault encryption sits under a master key that a password, a recovery key or Windows Hello opens.

## The cipher

`Cipher` produces a blob of version, algorithm, a 32-byte salt, the ciphertext and a 16-byte tag. A subkey and a 12-byte nonce are derived from the key and the salt with HKDF-SHA256, so each message has its own nonce and the random-nonce limits of a single key never apply. AES-256-GCM was chosen because it is hardware-accelerated and available on every .NET platform. Version and algorithm bytes leave room to change it.

```
Encrypt(key, input, associated)
	salt = random 32 bytes
	subkey, nonce = HKDF-SHA256(key, salt, "Aurora.Cipher.v1")
	ciphertext, tag = AES-256-GCM(subkey, nonce, input, associated)
	return version | alg | salt | ciphertext | tag

TryDecrypt(key, blob, associated)
	if version or alg is unknown, return false
	salt = blob.salt
	subkey, nonce = HKDF-SHA256(key, salt, "Aurora.Cipher.v1")
	return AES-256-GCM open(subkey, nonce, ciphertext, tag, associated)
```

## Passwords

`PasswordKey` derives a 256-bit key from a password with Argon2id (64 MiB, 3 passes, 4 lanes, 16-byte salt) and zeroes it on dispose. Its parameters are written beside the data as attributes so a file carries what it needs to be reopened. The typed password itself is a .NET string and cannot be wiped; only derived keys are.

## Encrypted files

A file keeps its extension and its content becomes an XML envelope, recognised by its root element. A password file is `<Encrypted Kdf Memory Passes Lanes Salt>` holding base64; a vault file is `<Encrypted Key="Vault">`. A loader that meets a locked file throws `LockedFileException`, and the tab shows a `LockedFileControl` with a masked field, a "Remember password for this session" checkbox and Unlock. Without the checkbox the key lives with the open document and is zeroed when the last view closes. `FileKeys` is the session map that carries a key from the prompt to the load. Encrypted notes are written whole through a temp file and a replace, never as in-place segments, so the crash journal never holds plaintext.

## Locked sheets

A formula or note link into a locked sheet shows `#LOCKED!`. `SheetBook` remembers locked paths by length and write time, so a recalculation costs a stat and not a parse. A rename or shift in one sheet that must rewrite references in other sheets prompts for the password of every encrypted sheet, because the only way to know whether it holds a reference is to decrypt it.

## Sealed blocks

A run of blocks in a note can be sealed under its own password. Locked, it is one `NoteSealed` node that counts as zero blocks, so the caret never enters it, and it shows as a `SealedControl`. Unlocked, it is ordinary blocks sharing a `NoteSeal`, saved as one `<Sealed>` segment, so in-place writes still work. The seal follows splits, inserted fragments, merges and table rebuilds, so no edit inside it writes plaintext.

## Vault lock

Locking a vault creates a random master key and wraps it once per way of opening: password (Argon2id), recovery key (HKDF over its 256 random bits, shown as 13 groups of four base32 characters) and Windows Hello (HKDF over an RSA signature of a fixed string). The slots live in the vault's `.aurora` folder. Changing the password therefore re-encrypts nothing. Converting the files runs on the main thread in 8 ms slices so it serialises with saves, swaps each file in whole, and records its state in the lock file so an interrupted run resumes. `VaultLock` holds one master key per open vault, so several vaults can be open at once: each file is read and written with the key of the vault that contains it, and closing one vault zeroes only its key. Only one vault converts at a time; a second request is skipped and resumes on that vault's next unlock. Any write into an encrypted vault that is not open throws instead of landing in plaintext. File and folder names stay plaintext, and exports such as PDF and CSV stay plain on purpose.

## Post-quantum

The symmetric parts stay secure against Grover's algorithm. The Windows Hello slot is RSA-2048 and is not post-quantum, because TPM 2.0 and Hello have no post-quantum keys. Replication key exchange should later be an ML-KEM-768 hybrid with X25519 or ECDH, over TLS 1.3.

## Not done

Sealing parts of sheets by page, a visual marker on unlocked seals, a command to remove a seal, a "remember" checkbox on the seal prompt, and parenting the Windows Hello prompt to the app window. The full list is in `ClaudeMemory/Decisions/encryption.md`. Verified by the test suite only; no password window, recovery-key window, [ENCRYPTED] click, startup unlock or real Hello prompt was driven in the running app.

## Related
- [[SETTINGS]]
- [[Rich Text Document]]
- [[Sheet Editor]]
