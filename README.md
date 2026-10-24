# 🌙 Lu-Mail

Ein nativer E-Mail-Client für **macOS** (Swift/SwiftUI) und **Windows** (C#/WinUI 3), der deine Postfächer von
**Gmail, Proton Mail, Mail.ru und Hostinger** an einem Ort zusammenführt – im eigenen Theme **„Luna“**.

## Was Lu-Mail kann

- **Sortiert nach Anbieter → Konto → Fach:** Jedes Konto hat seine eigenen Fächer
  **Posteingang · Werbung · Soziale Netzwerke · Updates · Foren** plus Gesendet, Entwürfe, Archiv, Spam,
  Papierkorb und alle eigenen Ordner vom Server.
- **Übersicht:** Alle Posteingänge, Ungelesen und Markiert über alle Konten hinweg.
- **Anmeldung auf der echten Anbieter-Seite:** „Bei Gmail/Proton/Mail.ru … anmelden“ öffnet die Webseite des
  Anbieters – normales Passwort, kein App-Passwort, keine Bridge (Web-Konto). Wer lieber IMAP nutzt, findet das
  unter „Erweitert“ (Anmeldung im Standardbrowser) – siehe [docs/ANMELDUNG.md](docs/ANMELDUNG.md).
- **Automatische Sortierung:** Gmail liefert die Kategorien selbst, für alle anderen sortiert Lu-Mail nach
  denselben Regeln ([shared/categories.json](shared/categories.json)). Per Rechtsklick „Absender immer als …
  einsortieren“.
- **Datenschutz:** externe Bilder und Tracking-Pixel sind standardmäßig blockiert, kein JavaScript in Mails,
  Links öffnen im Browser, Passwörter/Tokens nur im Schlüsselbund bzw. DPAPI-verschlüsselt.
- **Lesen, Antworten, Allen antworten, Weiterleiten, Neue E-Mail, Archivieren, Löschen, Spam, Markieren.**
- **Kyrillisch & Umlaute:** UTF-8, KOI8-R, Windows-1251, kyrillische Ordnernamen (Mail.ru).

## Download

Die fertigen Apps gibt es unter **[Releases](https://github.com/Luna-OS/Lu-Mail/releases/latest)**:

| System | Datei |
|--------|-------|
| macOS 14+ (Apple Silicon & Intel) | `Lu-Mail-X.Y.Z-macOS.dmg` – öffnen und Lu-Mail in „Programme“ ziehen |
| Windows 10/11 (Intel/AMD) | `Lu-Mail-X.Y.Z-Windows-x64-Setup.exe` |
| Windows 11 auf ARM (z. B. Snapdragon) | `Lu-Mail-X.Y.Z-Windows-arm64-Setup.exe` |

**Updates kommen automatisch:** Lu-Mail prüft regelmäßig auf neue Versionen, lädt sie im Hintergrund,
kontrolliert die SHA-256-Prüfsumme und installiert sie beim nächsten Beenden – oder sofort über
„Jetzt neu starten“. Einstellbar unter *Einstellungen → Updates*.

> **Erster Start:** Die Apps sind (noch) nicht mit einem kostenpflichtigen Apple-/Microsoft-Zertifikat signiert.
> - **macOS:** Beim ersten Öffnen *Systemeinstellungen → Datenschutz & Sicherheit → „Dennoch öffnen“* wählen.
> - **Windows:** Bei „Der Computer wurde durch Windows geschützt“ auf *Weitere Informationen → Trotzdem ausführen* klicken.
>
> Danach startet Lu-Mail normal, und auch die Auto-Updates laufen ohne diese Nachfrage.

**Neue Version veröffentlichen:** *Actions → CI → Run workflow* → Version (z. B. `0.2.0`) eintragen.
Die CI baut beide Apps, legt das Release an und alle installierten Lu-Mail-Apps aktualisieren sich.

## Projektaufbau

```
shared/     Theme, Anbieter, Sortierregeln, Browser-Anmeldeseite  ← gemeinsam für beide Apps
macos/      Swift Package: LuMail (SwiftUI-App) + LuMailCore (IMAP, SMTP, MIME, OAuth) + Tests
windows/    Visual-Studio-Solution: LuMail.App (WinUI 3) + LuMail.Core (MailKit) + Tests
docs/       Architektur, Anmeldung, Ideen
```

Details: [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md)

## macOS

Voraussetzung: macOS 14 (Sonoma) oder neuer, Xcode 15+ (oder die Command Line Tools).

```bash
cd macos
swift run LuMail                 # direkt starten
./scripts/build-app.sh           # Lu-Mail.app nach macos/build/ bauen (Universal)
./scripts/build-dmg.sh           # daraus macos/build/Lu-Mail.dmg erzeugen
swift test                       # Tests
```

Alternativ `macos/Package.swift` in Xcode öffnen und auf ▶︎ klicken.

## Windows

Voraussetzung: Windows 10 (1809+) oder 11, .NET 8 SDK, optional Visual Studio 2022 mit „WinUI-App-Entwicklung“.

```powershell
dotnet build windows/src/LuMail.App/LuMail.App.csproj -c Release -p:Platform=x64
dotnet test  windows/tests/LuMail.Core.Tests
pwsh windows/installer/build-installer.ps1 -Arch x64 -Version 0.1.0   # Setup.exe (benötigt Inno Setup 6)
```

Mehr in [windows/README.md](windows/README.md).

## Gemeinsame Dateien ändern

`shared/` ist die einzige Quelle für Theme, Anbieter und Regeln. Nach einer Änderung:

```bash
python3 scripts/sync-shared.py   # aktualisiert die eingebettete Kopie für macOS
```

Windows bindet `shared/` direkt ein. Die CI prüft beides.

## Ideen

Was als Nächstes kommen könnte (IMAP-Push, Offline-Cache, Newsletter-Abmelden mit einem Klick, Outlook/iCloud,
Touch ID …): [docs/IDEEN.md](docs/IDEEN.md)

## Lizenz

MIT – siehe [LICENSE](LICENSE).
