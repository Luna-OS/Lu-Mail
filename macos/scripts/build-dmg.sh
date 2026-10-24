#!/usr/bin/env bash
# Packt macos/build/Lu-Mail.app in ein Disk-Image mit Verknüpfung zu „Programme“.
# Aufruf:  ./scripts/build-dmg.sh [Ausgabedatei]   (vorher ./scripts/build-app.sh)
set -euo pipefail
cd "$(dirname "$0")/.."

APP="build/Lu-Mail.app"
OUT="${1:-build/Lu-Mail.dmg}"
[[ -d "$APP" ]] || { echo "Erst ./scripts/build-app.sh ausführen." >&2; exit 1; }

STAGING="build/dmg"
rm -rf "$STAGING" "$OUT"
mkdir -p "$STAGING"
ditto "$APP" "$STAGING/Lu-Mail.app"
ln -s /Applications "$STAGING/Programme"

hdiutil create -volname "Lu-Mail" -srcfolder "$STAGING" -fs HFS+ -format UDZO -ov "$OUT"
rm -rf "$STAGING"
echo "Fertig: macos/$OUT"
