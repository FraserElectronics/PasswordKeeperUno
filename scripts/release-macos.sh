#!/usr/bin/env bash
# Builds, Developer-ID signs, notarizes and staples PasswordKeeper, then produces a .dmg (run on a Mac).
#
# One-off setup:
#   1. A "Developer ID Application" certificate in your login keychain
#      (Xcode > Settings > Accounts > Manage Certificates > + > Developer ID Application).
#   2. Notary credentials stored in the keychain (app-specific password from appleid.apple.com):
#      xcrun notarytool store-credentials PK-NOTARY --apple-id <apple id> --team-id <TEAMID> --password <app-specific-password>
#
# Usage: SIGN_IDENTITY="Developer ID Application: Name (TEAMID)" scripts/release-macos.sh [osx-arm64|osx-x64]
set -euo pipefail

: "${SIGN_IDENTITY:?Set SIGN_IDENTITY to your Developer ID Application identity}"
PROFILE="${NOTARY_PROFILE:-PK-NOTARY}"
RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/macos-$RID"
APP="$OUT/PasswordKeeper.app"
DMG="$ROOT/artifacts/PasswordKeeper-$RID.dmg"

"$ROOT/scripts/package-macos.sh" "$RID"

# Notarize the app (zip is just the upload format), then staple the ticket to it.
ZIP="$OUT/PasswordKeeper.zip"
ditto -c -k --keepParent "$APP" "$ZIP"
xcrun notarytool submit "$ZIP" --keychain-profile "$PROFILE" --wait
xcrun stapler staple "$APP"

# Disk image with an Applications shortcut, signed, notarized and stapled.
STAGE="$OUT/dmg"; rm -rf "$STAGE" "$DMG"; mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"; ln -s /Applications "$STAGE/Applications"
hdiutil create -volname PasswordKeeper -srcfolder "$STAGE" -ov -format UDZO "$DMG"
codesign --force --timestamp --sign "$SIGN_IDENTITY" "$DMG"
xcrun notarytool submit "$DMG" --keychain-profile "$PROFILE" --wait
xcrun stapler staple "$DMG"

spctl --assess --type open --context context:primary-signature -v "$DMG" || true
echo "Done: $DMG"
