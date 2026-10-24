#!/usr/bin/env bash
# Baut Lu-Mail.app (Universal: Apple Silicon + Intel) nach macos/build/.
# Aufruf:  ./scripts/build-app.sh
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

# App-Symbol aus dem Mond-Logo erzeugen (benötigt nur Bordmittel: Swift + iconutil).
ICONSET="build/AppIcon.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
swift scripts/make-icon.swift "$ICONSET"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

# Ad-hoc-Signatur, damit der Schlüsselbund die App wiedererkennt.
codesign --force --sign - "$APP"
echo "Fertig: macos/$APP"
