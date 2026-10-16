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
  <key>CFBundleIconFile</key><string>AppIcon</string>
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

# App icon: render the SVG with Quick Look, then build an .icns. Non-fatal if it fails.
make_icon() {
  local tmp="$OUT/icon"; mkdir -p "$tmp/AppIcon.iconset"
  qlmanage -t -s 1024 -o "$tmp" "$ROOT/scripts/macos/AppIcon.svg" >/dev/null 2>&1
  local png="$tmp/AppIcon.svg.png"
  [ -f "$png" ] || return 1
  for size in 16 32 128 256 512; do
    sips -z $size $size "$png" --out "$tmp/AppIcon.iconset/icon_${size}x${size}.png" >/dev/null
    sips -z $((size*2)) $((size*2)) "$png" --out "$tmp/AppIcon.iconset/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$tmp/AppIcon.iconset" -o "$APP/Contents/Resources/AppIcon.icns"
}
make_icon || echo "Warning: could not build the app icon; continuing without it."

# Signing. Without SIGN_IDENTITY this is an ad-hoc signature that only runs on this Mac.
# With SIGN_IDENTITY="Developer ID Application: Name (TEAMID)" it is signed for distribution
# (hardened runtime + entitlements), ready for notarization by scripts/release-macos.sh.
if [ -n "${SIGN_IDENTITY:-}" ]; then
  ENT="$ROOT/scripts/macos/entitlements.plist"
  # Sign every Mach-O file inside-out first (managed .dll files are sealed as resources by the
  # bundle signature), then the bundle itself.
  while IFS= read -r f; do
    if file "$f" | grep -q "Mach-O"; then
      codesign --force --timestamp --options runtime --entitlements "$ENT" --sign "$SIGN_IDENTITY" "$f"
    fi
  done < <(find "$APP/Contents" -type f)
  codesign --force --timestamp --options runtime --entitlements "$ENT" --sign "$SIGN_IDENTITY" "$APP"
  codesign --verify --strict --verbose=2 "$APP"
else
  codesign --force --deep --sign - "$APP"
fi

echo "Built: $APP"
echo "Run it with: open \"$APP\""
