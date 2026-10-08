# PasswordKeeperUno

Cross-platform password vault (macOS first, then Windows, later iOS/Android) built with Uno Platform.
Each entry: title, URL, username, password, free-text notes. See [docs/SECURITY.md](docs/SECURITY.md).

## Status
- [x] Core library: encrypted vault file, password generator, tests
- [ ] Uno desktop app (macOS / Windows)
- [ ] Touch ID unlock, packaging, mobile

## Build & test
Requires the .NET 8 SDK.

```
dotnet test tests/PasswordKeeper.Core.Tests
```
