# Packaging

## macOS (run on a Mac)
```
scripts/package-macos.sh            # Apple silicon
scripts/package-macos.sh osx-x64    # Intel
open artifacts/macos-osx-arm64/PasswordKeeper.app
```
The app is ad-hoc signed, which is fine for your own Mac. If macOS blocks a copy that was downloaded or
transferred, right-click > Open once, or run `xattr -dr com.apple.quarantine PasswordKeeper.app`.
Running it on other people's Macs without warnings needs an Apple Developer ID certificate and notarization.

## Windows (run on Windows)
```
dotnet publish src/PasswordKeeper.App/PasswordKeeper.App/PasswordKeeper.App.csproj -c Release -p:OnlyTargetDesktop=true -f net10.0-desktop -r win-x64 --self-contained true -o artifacts/win-x64
```
Run `PasswordKeeper.App.exe` from that folder.

## macOS release (signed + notarized .dmg)
One-off: create a "Developer ID Application" certificate in Xcode (Settings > Accounts > Manage Certificates),
and store notary credentials: `xcrun notarytool store-credentials PK-NOTARY --apple-id <id> --team-id <TEAMID> --password <app-specific-password>`.
Then:
```
SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" scripts/release-macos.sh
```
Output: `artifacts/PasswordKeeper-osx-arm64.dmg`.
