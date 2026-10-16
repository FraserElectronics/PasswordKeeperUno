#!/usr/bin/env bash
# Builds a self-contained PasswordKeeper.app for macOS (run on a Mac).
# Usage: [CONFIG=Debug] scripts/package-macos.sh [osx-arm64|osx-x64]
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/src/PasswordKeeper.App/PasswordKeeper.App/PasswordKeeper.App.csproj"
OUT="$ROOT/artifacts/macos-$RID"
APP="$OUT/PasswordKeeper.app"

rm -rf "$OUT"
dotnet publish "$PROJ" -c "${CONFIG:-Release}" -p:OnlyTargetDesktop=true -f net10.0-desktop -r "$RID" --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -o "$OUT/publish"

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

# Layout. macOS treats everything in Contents/MacOS (managed .dll files, data folders) as nested code,
# and plain files cannot be signed that way. So only the main executable stays there; everything else
# moves to Contents/Resources/app and is symlinked back. The whole runtime has to move together because
# .NET resolves symlinks and then looks for its native libraries beside the app. Uno also looks for its
# assets (icon and text fonts) in a Resources folder beside the app files, so links are made for that.
RES="$APP/Contents/Resources/app"; mkdir -p "$RES"
while IFS= read -r item; do
  [ "$item" = "$APP/Contents/MacOS/PasswordKeeper.App" ] && continue
  name="$(basename "$item")"
  mv "$item" "$RES/$name"
  ln -s "../Resources/app/$name" "$item"
done < <(find "$APP/Contents/MacOS" -mindepth 1 -maxdepth 1)
ln -s "../Resources/app" "$APP/Contents/MacOS/Resources"
# .NET resolves the symlinks to Resources/app, and Uno then looks in Resources/app/Resources, so give it
# links to the asset files and folders there too.
mkdir -p "$RES/Resources"
for entry in "$RES"/*; do
  name="$(basename "$entry")"
  case "$name" in Resources|*.dll|*.pdb|*.dylib) continue ;; esac
  ln -s "../$name" "$RES/Resources/$name"
done

# Signing. Without SIGN_IDENTITY this is an ad-hoc signature that only runs on this Mac.
# With SIGN_IDENTITY="Developer ID Application: Name (TEAMID)" it is signed for distribution
# (hardened runtime + entitlements), ready for notarization by scripts/release-macos.sh.
if [ -n "${SIGN_IDENTITY:-}" ]; then
  ENT="$ROOT/scripts/macos/entitlements.plist"
  SIGN_ARGS=(--force --timestamp --options runtime --entitlements "$ENT" --sign "$SIGN_IDENTITY")
else
  SIGN_ARGS=(--force --sign -)
fi
# Sign every native binary (anywhere in the bundle) first, the main executable and bundle last.
echo "Signing native code"
while IFS= read -r f; do
  [ "$f" = "$APP/Contents/MacOS/PasswordKeeper.App" ] && continue   # main executable goes last
  if file "$f" | grep -q "Mach-O"; then
    codesign "${SIGN_ARGS[@]}" "$f"
  fi
done < <(find "$APP/Contents" -type f)
echo "Signing bundle"
codesign "${SIGN_ARGS[@]}" "$APP"
echo "Verifying bundle"
codesign --verify --strict --verbose=2 "$APP"

echo "Built: $APP"
echo "Run it with: open \"$APP\""
