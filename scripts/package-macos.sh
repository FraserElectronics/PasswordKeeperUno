#!/usr/bin/env bash
# Builds a self-contained PasswordKeeper.app for macOS (run on a Mac).
# Usage: scripts/package-macos.sh [osx-arm64|osx-x64]
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/src/PasswordKeeper.App/PasswordKeeper.App/PasswordKeeper.App.csproj"
OUT="$ROOT/artifacts/macos-$RID"
APP="$OUT/PasswordKeeper.app"

rm -rf "$OUT"
dotnet publish "$PROJ" -c Release -p:OnlyTargetDesktop=true -f net10.0-desktop -r "$RID" --self-contained true -o "$OUT/publish"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$OUT/publish/." "$APP/Contents/MacOS/"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>PasswordKeeper</string>
  <key>CFBundleDisplayName</key><string>PasswordKeeper</string>
  <key>CFBundleIdentifier</key><string>uk.co.fraserelectronics.passwordkeeper</string>
  <key>CFBundleExecutable</key><string>PasswordKeeper.App</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

chmod +x "$APP/Contents/MacOS/PasswordKeeper.App"

# Ad-hoc signature: lets it run on this Mac. Distribution to other Macs needs a Developer ID
# certificate and notarization (see docs/PACKAGING.md).
codesign --force --deep --sign - "$APP"

echo "Built: $APP"
echo "Run it with: open \"$APP\""
