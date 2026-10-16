# Cloud backup with Backblaze B2

PasswordKeeper can copy your vault file to a private Backblaze B2 bucket and fetch it back on
another device. Only the **encrypted** vault file (`PKV1`, AES-256-GCM, Argon2id) is uploaded, so
Backblaze never sees your passwords or your master password. The upload carries a SHA-1 checksum
and downloads are verified against it.

## One-time Backblaze setup

1. Create a Backblaze account and open **B2 Cloud Storage**.
2. **Buckets > Create a Bucket**: choose a unique name, set **Files in Bucket are: Private**.
   Leave Default Encryption on. Keep file versioning (the default) so older uploads stay recoverable.
3. **Application Keys > Add a New Application Key**: restrict it to **that one bucket**,
   access *Read and Write*. Copy the **keyID** and the **applicationKey** straight away (the key is
   shown once). Do not use your master application key.

## Using it in the app

* **Build the master vault on one machine.** Open the vault, press **Cloud backup...**, enter the
  key ID, application key, bucket name and file name (default `vault.pkv`), tick
  *Back up automatically after every save* if wanted, and press **Back up now**.
* The Backblaze settings are stored **inside the encrypted vault**, so they travel with it.
* **Pull it onto another device** (Mac, Windows, iPhone): on the unlock screen press
  **Restore from cloud...**, enter the same key details, then unlock with your master password.
  Once unlocked, the settings are already in the vault, so later syncs need no typing.
* **Restoring over an existing vault** keeps your old file beside it as `vault.pkv.before-restore`.
* **Conflicts:** if the cloud copy changed since this device last synced, a manual backup asks
  whether to overwrite the cloud copy or restore it; an automatic backup just reports it and waits.

## Notes

* This is backup/replace, not a merge: the newest vault you choose wins. Build on one machine and
  pull to the others, as intended, and restore before editing elsewhere.
* Treat the application key like a password. Revoke it in Backblaze if a device is lost.
