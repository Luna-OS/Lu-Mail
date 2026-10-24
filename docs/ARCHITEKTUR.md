# Lu-Mail – Architektur

Lu-Mail besteht aus **zwei nativen Apps** mit identischem Verhalten und gemeinsamem Design:

| Plattform | Sprache / UI | Mail-Protokolle | Geheimnisse |
|-----------|--------------|-----------------|-------------|
| macOS 14+ | Swift 5.9+, SwiftUI | eigener IMAP-/SMTP-Client über `Network.framework` (in `LuMailCore`) | macOS-Schlüsselbund |
| Windows 10/11 | C# / .NET 8, WinUI 3 (Windows App SDK) | MailKit / MimeKit | DPAPI-verschlüsselte Datei pro Benutzer |

```
shared/                 ← Single Source of Truth für beide Apps
  theme/lu-theme.json   Farben, Radien, Abstände (Theme „Luna“)
  providers.json        Anbieter: Server, Ports, OAuth-Endpunkte, Hilfetexte, Ordnerrollen
  categories.json       Kategorien + Regeln für die automatische Sortierung
  login/login.html      lokale Browser-Anmeldeseite (Passwort-Anbieter)
  login/done.html       Abschlussseite im Browser
  oauth.example.json    Vorlage für eigene OAuth-Client-IDs
macos/                  Swift Package (App + LuMailCore + Tests)
windows/                Visual-Studio-Solution (LuMail.App + LuMail.Core + Tests)
scripts/sync-shared.py  erzeugt macos/Sources/LuMailCore/Generated/SharedResources.swift
```

Windows bindet `shared/` direkt als *EmbeddedResource* ein. macOS bekommt die Dateien als generierte
Swift-Strings (`python3 scripts/sync-shared.py`) – so funktionieren sie in `swift run`, im `.app`-Bundle
und in den Linux-Tests gleichermaßen. Die CI prüft, dass die generierte Datei aktuell ist.

## Sidebar-Aufbau (beide Apps)

```
Übersicht
  Alle Posteingänge        (Kategorie „Posteingang“ aller Konten)
  Ungelesen
  Markiert
Gmail                      ← Gruppe pro Anbieter (Reihenfolge wie providers.json), Farbpunkt = Anbieterfarbe
  max@gmail.com            ← Konto
    Posteingang            ← Kategorie primary (INBOX ohne Werbung etc.)
    Werbung                ← Kategorie promotions
    Soziale Netzwerke      ← Kategorie social
    Updates                ← Kategorie updates
    Foren                  ← Kategorie forums
    Gesendet / Entwürfe / Spam / Papierkorb / Archiv   ← Server-Ordner mit Rolle
    weitere Server-Ordner  ← alles andere aus IMAP LIST (ohne \Noselect)
Proton Mail
  …
```

Leere Anbietergruppen werden ausgeblendet. Jede Zeile zeigt die Zahl ungelesener Nachrichten.

## Ordnerrollen

Aus `providers.json → folderRoles`: zuerst über das IMAP-SPECIAL-USE-Attribut (RFC 6154), sonst über den Namen
(Vergleich ohne Groß-/Kleinschreibung, Pfad-Trenner beachten). Ein Server-Ordner bekommt höchstens eine Rolle;
bei Gmail gewinnt `\All` nur für „archive“, wenn kein `\Archive` existiert.

## Kategorien (Werbung, Soziale Netzwerke, …)

Die Kategorien sind virtuelle Unterordner von INBOX.

1. Lu-Mail lädt die neuesten **200** Nachrichten-Header aus INBOX
   (`UID FETCH … (UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE BODY.PEEK[HEADER.FIELDS (LIST-UNSUBSCRIBE LIST-UNSUBSCRIBE-POST LIST-ID LIST-POST PRECEDENCE REFERENCES)])`).
2. **Gmail** (`serverCategories = "gmail"`): für promotions, social, updates, forums je
   `UID SEARCH UID <min>:<max> X-GM-RAW "<gmailQuery>"`. Treffer bekommen die Kategorie, Rest = primary.
3. **Alle anderen**: lokaler Klassifizierer nach `categories.json → algorithm` (identisch in Swift und C#).
4. Nutzer-Regeln („Absender immer als Werbung einsortieren“) überschreiben alles. Sie werden lokal pro
   Absender-Adresse gespeichert (`rules.json` im App-Datenordner) und gelten für alle Konten.

Andere Ordner (Gesendet, Spam, …) laden die neuesten **100** Nachrichten ohne Kategorisierung.

## Anmeldung – immer über den Browser

Jede Anmeldung öffnet den **Standardbrowser** (kein eingebettetes Web-Fenster). Dafür startet die App einen
einmaligen HTTP-Server nur auf der Loopback-Schnittstelle mit zufälligem Port.

### A) OAuth 2.0 (Gmail, Mail.ru – sobald `oauth.json` eine Client-ID enthält)

1. PKCE (RFC 7636, S256, 64-Zeichen-Verifier) + zufälliger `state` (32 Byte, base64url).
2. Browser öffnet `authorizationEndpoint?response_type=code&client_id&redirect_uri&scope&state&code_challenge&code_challenge_method=S256&login_hint=<optional>&<extraParams>`.
3. `redirect_uri` = `redirectUriTemplate` mit `{port}` ersetzt. Der Server beantwortet `GET /callback?code&state`,
   prüft `state`, zeigt `done.html` und schließt.
4. Token-Tausch per `POST tokenEndpoint` (`grant_type=authorization_code`, `code`, `redirect_uri`, `client_id`,
   optional `client_secret`, `code_verifier`).
5. E-Mail-Adresse: `emailSource = idToken` → Claim `email` aus dem JWT `id_token`; `userInfo` →
   `GET userInfoEndpoint?access_token=…` → Feld `email`. Fallback: vom Nutzer eingegebene Adresse.
6. Refresh-Token sicher speichern; Access-Token bei Bedarf über `grant_type=refresh_token` erneuern
   (60 s vor Ablauf).
7. IMAP: `AUTHENTICATE XOAUTH2 base64("user=" + email + "\x01auth=Bearer " + token + "\x01\x01")`,
   SMTP: `AUTH XOAUTH2 <dasselbe>`.

### B) Passwort-Anbieter (Proton Mail via Bridge, Hostinger, sowie Gmail/Mail.ru ohne OAuth-Client)

1. Server wie oben, zufälliger `state`. Browser öffnet `http://127.0.0.1:<port>/login/<state>`.
2. Die Seite ist `shared/login/login.html` mit ersetzten Platzhaltern (alle Werte HTML-escaped):
   `{{PROVIDER_NAME}} {{PROVIDER_COLOR}} {{PASSWORD_LABEL}} {{PASSWORD_HELP}} {{HELP_URL}} {{STATE}} {{EMAIL}} {{ERROR}}`.
3. `POST /login` (`application/x-www-form-urlencoded`: `state`, `email`, `password`). Die App prüft `state`,
   testet die Zugangsdaten sofort per IMAP-LOGIN und zeigt bei Fehler die Seite erneut mit `{{ERROR}}`,
   bei Erfolg `done.html`.
4. Sicherheit: nur 127.0.0.1/::1, `Host`-Header muss `127.0.0.1:<port>` oder `localhost:<port>` sein
   (DNS-Rebinding-Schutz), `state` einmalig, Timeout 10 Minuten, Server stoppt danach.

Die `done.html`-Platzhalter: `{{TITLE}}`, `{{MESSAGE}}`.

## Proton Mail

Proton bietet keinen IMAP-Zugang ohne die **Proton Mail Bridge** (lokal, 127.0.0.1). Lu-Mail verbindet sich mit
der Bridge im Modus „SSL“ (IMAP 1143, SMTP 1025). Das selbstsignierte Bridge-Zertifikat wird **nur** für
Loopback-Adressen akzeptiert (`allowInsecureLoopbackCertificate`).

## E-Mail-Anzeige & Datenschutz

- HTML-Mails werden in einer Web-Ansicht ohne JavaScript angezeigt, mit vorangestellter CSP
  `default-src 'none'; img-src data: cid:; style-src 'unsafe-inline'` → **externe Bilder/Tracking-Pixel blockiert**.
  Ein Button „Externe Inhalte laden“ entfernt die Sperre für diese Nachricht.
- Links öffnen im Standardbrowser.
- Beim Öffnen wird die Nachricht als gelesen markiert (`UID STORE +FLAGS (\Seen)`).

## Aktionen

Gelesen/ungelesen, Markieren (`\Flagged`), Archivieren (Verschieben in Rolle archive), Löschen (Verschieben in
Rolle trash; im Papierkorb endgültig), „Kein Spam“/„Spam“, Absender-Regel setzen, Antworten, Allen antworten,
Weiterleiten, Neue E-Mail. Verschieben per `UID MOVE` wenn `MOVE` angeboten wird, sonst `UID COPY` +
`+FLAGS (\Deleted)` + `UID EXPUNGE`/`EXPUNGE`.

Automatische Aktualisierung alle 3 Minuten und per Button / ⌘R / F5.

## Gespeicherte Daten

| Was | macOS | Windows |
|-----|-------|---------|
| Kontenliste (ohne Geheimnisse) | `~/Library/Application Support/LuMail/accounts.json` | `%APPDATA%\LuMail\accounts.json` |
| Absender-Regeln | `…/LuMail/rules.json` | `%APPDATA%\LuMail\rules.json` |
| OAuth-Client-IDs | `…/LuMail/oauth.json` | `%APPDATA%\LuMail\oauth.json` |
| Passwörter / Refresh-Tokens | Schlüsselbund, Dienst `de.lunaos.lumail` | `%APPDATA%\LuMail\secrets.bin` (DPAPI, CurrentUser) |

`accounts.json`:
```json
[{ "id": "UUID", "providerId": "gmail", "email": "max@gmail.com", "displayName": "Max", "authMethod": "oauth2" }]
```

## Downloads & Auto-Updater

**Pakete** (gebaut von `.github/workflows/ci.yml`, veröffentlicht als GitHub-Release `vX.Y.Z`):

| Datei | Inhalt |
|-------|--------|
| `Lu-Mail-X.Y.Z-macOS.dmg` | Universal-App (Apple Silicon + Intel), Ziehen nach „Programme“ |
| `Lu-Mail-X.Y.Z-Windows-x64-Setup.exe` / `…-arm64-Setup.exe` | Inno-Setup-Installer, Installation pro Benutzer nach `%LOCALAPPDATA%\Programs\Lu-Mail` (keine Admin-Rechte) |
| `SHA256SUMS.txt` | SHA-256 aller Pakete (`sha256sum`-Format: `<hex>  <Dateiname>`) |

Ein Release entsteht über *Actions → CI → Run workflow* mit einer Versionsnummer oder durch Pushen eines Tags `vX.Y.Z`.

**Auto-Updater** (beide Apps, Einstellungen in `shared/update.json`):

1. `initialDelaySeconds` nach dem Start und danach alle `checkIntervalHours` Stunden:
   `GET latestReleaseApi` (Header `Accept: application/vnd.github+json`, `User-Agent: Lu-Mail/<Version>`).
2. `tag_name` ohne führendes `v` als Version `MAJOR.MINOR.PATCH` lesen; nur wenn größer als die eigene Version
   (numerischer Vergleich je Teil) geht es weiter.
3. Paket wählen: Asset, dessen Name auf `assetSuffixes[<plattform>]` endet (Windows: Prozessor-Architektur).
   `SHA256SUMS.txt` laden; fehlt die Zeile für das Paket → Abbruch.
4. Paket in einen temporären Ordner laden, SHA-256 prüfen; bei Abweichung löschen und abbrechen.
5. Modus (Einstellung „Updates“):
   - **Automatisch** (Standard): im Hintergrund laden; Hinweis „Lu-Mail X.Y.Z ist bereit – Jetzt neu starten“.
     Ohne Klick wird das Update beim Beenden von Lu-Mail still installiert.
   - **Nur benachrichtigen**: Hinweis mit „Herunterladen und neu starten“.
   - **Aus**: keine automatische Prüfung (manuell über „Jetzt nach Updates suchen“).
6. Installation:
   - **macOS:** DMG mit `hdiutil attach -nobrowse -readonly` einhängen, `Lu-Mail.app` in einen Temp-Ordner kopieren,
     aushängen. Ein Hilfsskript wartet auf das Ende des laufenden Prozesses, ersetzt das bisherige App-Bundle
     (`Bundle.main.bundleURL`) per `ditto` und startet die App neu (nur bei „Jetzt neu starten“). Ist der Ordner nicht
     beschreibbar oder läuft die App nicht aus einem `.app`-Bundle, öffnet Lu-Mail stattdessen das DMG.
   - **Windows:** `Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1|0` starten und
     Lu-Mail beenden. Das Setup startet Lu-Mail danach neu, wenn `/RELAUNCH=1` (bei „Jetzt neu starten“).
7. Entwickler-Builds (macOS ohne `.app`-Bundle, Windows-Version `0.0.0`) prüfen nicht automatisch.
