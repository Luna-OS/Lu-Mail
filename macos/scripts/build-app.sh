#!/usr/bin/env bash
# Baut Lu-Mail.app (Universal: Apple Silicon + Intel) nach macos/build/.
# Aufruf:  ./scripts/build-app.sh            (Version aus Resources/Info.plist)
#          VERSION=0.2.0 ./scripts/build-app.sh
set -euo pipefail
cd "$(dirname "$0")/.."

ARCHS=(--arch arm64 --arch x86_64)
swift build -c release "${ARCHS[@]}"
BIN_DIR="$(swift build -c release "${ARCHS[@]}" --show-bin-path)"

APP="build/Lu-Mail.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/LuMail" "$APP/Contents/MacOS/LuMail"
cp Resources/Info.plist "$APP/Contents/Info.plist"

if [[ -n "${VERSION:-}" ]]; then
  /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$APP/Contents/Info.plist"
fi

# App-Symbol aus shared/icon/lu-mail-1024.png (erzeugt von scripts/make-icon.py).
ICON_SOURCE="../shared/icon/lu-mail-1024.png"
ICONSET="build/AppIcon.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$ICON_SOURCE" --out "$ICONSET/icon_${size}x${size}.png" > /dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$ICON_SOURCE" --out "$ICONSET/icon_${size}x${size}@2x.png" > /dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

# Ad-hoc-Signatur, damit der Schlüsselbund die App wiedererkennt.
codesign --force --sign - "$APP"
echo "Fertig: macos/$APP"
