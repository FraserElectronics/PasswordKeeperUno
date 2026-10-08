# Security design

## Vault file
One portable file (`.pkv`) that can be copied between machines.

| Item | Choice |
|---|---|
| Encryption | AES-256-GCM (authenticated) |
| Key derivation | Argon2id, default 64 MiB / 3 iterations / 4 lanes, stored in header |
| Salt / nonce | 16 / 12 random bytes, regenerated on **every** save |
| Integrity | Whole header is GCM associated data; any change fails decryption |
| Master password | Unicode NFKC-normalised, so it derives the same key on every OS/keyboard |
| Writes | Temp file then atomic rename, so a crash can't corrupt the vault |

Wrong password and tampering are reported identically on purpose.
Header KDF parameters are bounds-checked before use so a crafted file can't force huge allocations.

## Planned (later steps)
- Idle auto-lock and lock-on-sleep; clear clipboard ~30 s after copying
- Touch ID / Face ID unlock: random wrapping key held in the Keychain (access-controlled, biometry required); master password always remains as fallback
- No plaintext ever written to disk, logs or crash reports
- Cloud sync (e.g. Backblaze) only ever sees the encrypted file

## Known limits
.NET strings are immutable, so a master password held as a `string` can't be reliably wiped from memory. The UI layer will keep it as short-lived as possible; this is a known trade-off of managed runtimes.
