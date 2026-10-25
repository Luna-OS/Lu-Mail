#!/bin/bash
# Aktualisiert Lu-Mail auf dem Mac auf die neueste Version – an Ort und Stelle: nichts deinstallieren, Konten,
# Einstellungen und Schlüsselbund-Einträge bleiben (sie liegen in der Library, nicht in der App). Ist Lu-Mail
# noch nicht installiert, landet es in „Programme“. Im Terminal:
#
#   curl -fsSL https://github.com/Luna-OS/Lu-Mail/releases/latest/download/update-mac.sh | bash
#
# Prüft wie die App selbst (docs/FUNKTIONEN.md §23): SHA-256 aus SHA256SUMS.txt, dann Bundle-ID, Version und
# Signatur der neuen App, bevor sie die alte ersetzt.
#
# LUMAIL_APP=/Pfad/Lu-Mail.app wählt die App; LUMAIL_RELEASES eine andere Quelle (Spiegel oder file:// für Tests).

# Alles in einer Funktion, aufgerufen in der letzten Zeile: ein abgebrochener Download führt nichts aus.
main() {
  set -euo pipefail

  local releases="${LUMAIL_RELEASES:-https://github.com/Luna-OS/Lu-Mail/releases}"
  local bundle_id="de.lunaos.lumail"

  say() { printf '%s\n' "$*"; }
  fail() {
    printf 'Lu-Mail-Update: %s\n' "$*" >&2
    exit 1
  }
  info() { /usr/bin/plutil -extract "$2" raw -o - "$1/Contents/Info.plist" 2>/dev/null; }
  # Liegt der Pfad auf einem schreibgeschützten Laufwerk (z. B. dem eingehängten Disk-Image)?
  readonly_volume() {
    local mnt
    mnt="$(df -P "$1" 2>/dev/null | tail -n 1 | sed -E 's/^([^ ]+ +){5}//')"
    [ -n "$mnt" ] && mount | grep -F " on $mnt (" | grep -q "read-only"
  }
  # Ist Version $1 neuer als $2? (X.Y.Z, Teile als Zahlen verglichen)
  newer() {
    local a b i
    IFS=. read -r -a a <<<"$1"
    IFS=. read -r -a b <<<"$2"
    for i in 0 1 2; do
      if [ "${a[i]:-0}" -gt "${b[i]:-0}" ]; then return 0; fi
      if [ "${a[i]:-0}" -lt "${b[i]:-0}" ]; then return 1; fi
    done
    return 1
  }

  [ "$(uname -s)" = Darwin ] || fail "das ist nur für macOS."

  # Die installierte App: am üblichen Ort oder dort, wo Spotlight sie kennt.
  local app="${LUMAIL_APP:-}" candidate fresh=0
  if [ -z "$app" ]; then
    for candidate in "/Applications/Lu-Mail.app" "$HOME/Applications/Lu-Mail.app"; do
      if [ -d "$candidate" ]; then
        app="$candidate"
        break
      fi
    done
  fi
  if [ -z "$app" ]; then
    while IFS= read -r candidate; do
      case "$candidate" in *.app) ;; *) continue ;; esac
      readonly_volume "$candidate" && continue
      app="$candidate"
      break
    done < <(mdfind "kMDItemCFBundleIdentifier == '$bundle_id'" 2>/dev/null || true)
  fi
  if [ -z "$app" ]; then
    app="/Applications/Lu-Mail.app"
    fresh=1
  fi
  local current=""
  if [ "$fresh" = 0 ]; then
    [ -d "$app" ] || fail "$app gibt es nicht."
    app="$(cd "$app" && pwd)"
    [ "$(info "$app" CFBundleIdentifier || true)" = "$bundle_id" ] || fail "$app ist nicht Lu-Mail."
    readonly_volume "$app" && fail "Lu-Mail liegt auf einem schreibgeschützten Laufwerk (Disk-Image?) – bitte zuerst nach „Programme“ ziehen."
    current="$(info "$app" CFBundleShortVersionString || true)"
  fi

  # Neueste Version: steht im Dateinamen des DMG in SHA256SUMS.txt („<sha256>  Lu-Mail-X.Y.Z-macOS.dmg“).
  local sums line sha dmg version
  sums="$(curl -fsSL "$releases/latest/download/SHA256SUMS.txt")" || fail "$releases ist nicht erreichbar."
  line="$(printf '%s\n' "$sums" | grep -E '[[:space:]]\*?Lu-Mail-[0-9]+\.[0-9]+\.[0-9]+-macOS\.dmg$' | head -n 1 || true)"
  [ -n "$line" ] || fail "das neueste Release enthält kein Mac-Disk-Image."
  sha="$(printf '%s\n' "$line" | awk '{ print tolower($1) }')"
  dmg="$(printf '%s\n' "$line" | awk '{ print $NF }')"
  dmg="${dmg#\*}"
  version="${dmg#Lu-Mail-}"
  version="${version%-macOS.dmg}"

  if [ -n "$current" ] && ! newer "$version" "$current"; then
    say "Lu-Mail ${current} ist aktuell ($app)."
    return 0
  fi
  if [ "$fresh" = 1 ]; then
    say "Installiere Lu-Mail $version nach $app …"
  else
    say "Aktualisiere Lu-Mail ${current:-?} auf $version ($app) …"
  fi

  local work
  work="$(mktemp -d "${TMPDIR:-/tmp}/lumail-update.XXXXXX")"
  # shellcheck disable=SC2064 # $work ist lokal: die Falle bekommt den Wert jetzt.
  trap "/usr/bin/hdiutil detach '$work/mnt' -force -quiet >/dev/null 2>&1 || true; rm -rf '$work'" EXIT
  curl -fL --progress-bar -o "$work/$dmg" "$releases/latest/download/$dmg" || fail "der Download ist fehlgeschlagen."
  [ "$(shasum -a 256 "$work/$dmg" | awk '{ print tolower($1) }')" = "$sha" ] ||
    fail "der Download ist beschädigt (SHA-256 passt nicht zu SHA256SUMS.txt)."

  mkdir -p "$work/mnt" "$work/new"
  /usr/bin/hdiutil attach "$work/$dmg" -nobrowse -readonly -noautoopen -quiet -mountpoint "$work/mnt" ||
    fail "das Disk-Image lässt sich nicht öffnen."
  [ -d "$work/mnt/Lu-Mail.app" ] || fail "im Disk-Image ist keine Lu-Mail.app."
  /usr/bin/ditto "$work/mnt/Lu-Mail.app" "$work/new/Lu-Mail.app" || fail "die App lässt sich nicht kopieren."
  /usr/bin/hdiutil detach "$work/mnt" -force -quiet >/dev/null 2>&1 || true

  local new="$work/new/Lu-Mail.app"
  [ "$(info "$new" CFBundleIdentifier || true)" = "$bundle_id" ] || fail "das Update ist nicht Lu-Mail."
  [ "$(info "$new" CFBundleShortVersionString || true)" = "$version" ] ||
    fail "das Update ist nicht die angekündigte Version $version."
  /usr/bin/codesign --verify --deep --strict "$new" 2>/dev/null || fail "die Signatur des Updates ist beschädigt."

  # Läuft Lu-Mail, wird es beendet (wie über „Lu-Mail beenden“) und danach wieder geöffnet.
  local running=0 i
  if [ "$fresh" = 0 ] && pgrep -f "$app/Contents/MacOS/" >/dev/null 2>&1; then
    running=1
    say "Beende Lu-Mail …"
    osascript -e "tell application id \"$bundle_id\" to quit" >/dev/null 2>&1 || true
    for i in $(seq 1 60); do
      pgrep -f "$app/Contents/MacOS/" >/dev/null 2>&1 || break
      sleep 0.5
    done
    pgrep -f "$app/Contents/MacOS/" >/dev/null 2>&1 && fail "Lu-Mail ist noch offen. Bitte beenden und den Befehl erneut ausführen."
    # Lu-Mail spielt ein schon geladenes Update beim Beenden selbst ein – warten, bis dessen Installer fertig ist.
    sleep 1
    for i in $(seq 1 120); do
      pgrep -f "LuMail/Update/install.sh" >/dev/null 2>&1 || break
      sleep 0.5
    done
    pgrep -f "LuMail/Update/install.sh" >/dev/null 2>&1 && fail "Lu-Mail installiert gerade selbst ein Update – bitte in einer Minute erneut ausführen."
    if [ "$(info "$app" CFBundleShortVersionString || true)" = "$version" ]; then
      say "Lu-Mail hat sich selbst auf $version aktualisiert."
      open "$app" 2>/dev/null || true
      return 0
    fi
  fi

  local dir aside sudo=""
  dir="$(dirname "$app")"
  aside="$app.lumail-update-old"
  if [ ! -w "$dir" ] || { [ -e "$app" ] && [ ! -w "$app" ]; }; then
    say "Für $dir wird dein Mac-Passwort benötigt."
    sudo="sudo"
  fi
  if [ -e "$app" ]; then
    $sudo rm -rf "$aside"
    $sudo mv "$app" "$aside" || fail "die alte App lässt sich nicht beiseitelegen."
  fi
  if ! $sudo /usr/bin/ditto "$new" "$app"; then
    $sudo rm -rf "$app"
    [ -e "$aside" ] && $sudo mv "$aside" "$app"
    fail "die neue App ließ sich nicht einsetzen; die alte ist zurück."
  fi
  $sudo rm -rf "$aside"
  $sudo /usr/bin/xattr -dr com.apple.quarantine "$app" 2>/dev/null || true
  /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
    -f "$app" >/dev/null 2>&1 || true

  say "Lu-Mail $version ist installiert: $app"
  if [ "$running" = 1 ] || [ "$fresh" = 1 ]; then open "$app" 2>/dev/null || true; fi
}

main "$@"
