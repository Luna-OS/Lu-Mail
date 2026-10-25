# Lu-Mail – Funktionen (Spezifikation für macOS und Windows)

Ergänzt [ARCHITEKTUR.md](ARCHITEKTUR.md). Beide Apps setzen jede Funktion gleich um; gemeinsame Daten liegen
in `shared/`, App-Daten im App-Datenordner (`~/Library/Application Support/LuMail/` bzw. `%APPDATA%\LuMail\`).
Rein lokale Dateien (Offline-Cache, Postausgang) werden nie zwischen den Plattformen ausgetauscht; ihr genaues
internes Format darf sich je App unterscheiden. `rules.json`, `accounts.json`, `contacts.json` und `snoozed.json`
folgen dem hier beschriebenen Format.

## 1. Push für neue Mails (IMAP IDLE)
- Pro Konto eine **zweite** IMAP-Verbindung: `SELECT INBOX`, dann `IDLE` (RFC 2177), wenn der Server `IDLE` kann.
- Jede ungetaggte Antwort `EXISTS`, `EXPUNGE` oder `FETCH` → Posteingang dieses Kontos neu laden (2 s entprellt).
- Alle 25 Minuten `DONE` und neu `IDLE`. Bei Verbindungsabbruch nach 30 s / 60 s / 120 s … (max. 10 min) neu verbinden.
- Ohne IDLE-Fähigkeit bleibt es beim normalen Abruf (Einstellung „Automatisch abrufen“).

## 2. Mitteilungen, Dock-/Taskleisten-Zähler, Menüleiste/Infobereich
- Nach jedem Neuladen eines Posteingangs: Nachrichten, deren UID beim letzten Laden noch nicht bekannt war, die
  ungelesen sind und in Kategorie **Posteingang** oder **Updates** liegen → Systemmitteilung „Absender – Betreff“
  (beim ersten Laden nach App-Start keine Mitteilungen). Klick öffnet die Nachricht. Einstellung „Mitteilungen“ (an).
- Zähler = ungelesene Nachrichten der Kategorie Posteingang über alle Konten.
  macOS: Dock-Badge + Menüleisten-Symbol (Mond, Zahl, die 5 neuesten ungelesenen Mails, „Lu-Mail öffnen“).
  Windows: Taskleisten-Overlay + Symbol im Infobereich (Tooltip mit Zahl, Menü mit den 5 neuesten Mails).

## 3. Offline-Cache
- Nach jedem Laden werden Ordnerliste und Nachrichtenübersichten pro Konto in `cache/<Konto-ID>.json` gespeichert,
  geöffnete Nachrichten als `cache/<Konto-ID>/<Ordner-Hash>/<UID>.eml` (max. 200 MB, älteste zuerst gelöscht).
- Beim Start wird sofort aus dem Cache angezeigt, danach vom Server aktualisiert. Ohne Internet bleiben
  Liste und bereits geöffnete Nachrichten lesbar (Hinweis „Offline“).
- Einstellung „Offline-Cache“ (an) und Button „Cache leeren“. Beim Entfernen eines Kontos wird sein Cache gelöscht.

## 4. Suche auf dem Server
- Lokale Suche filtert sofort. Mit **Enter** im Suchfeld zusätzlich Serversuche im aktuellen Bereich
  (Konto-Ordner bzw. INBOX aller Konten in der Übersicht), höchstens 50 Treffer je Konto:
  - Gmail: `UID SEARCH X-GM-RAW "<text>"`
  - sonst: `UID SEARCH CHARSET UTF-8 OR OR FROM "<text>" SUBJECT "<text>" BODY "<text>"` (Nicht-ASCII als Literal)
- Treffer erscheinen in der Liste unter der Überschrift „Weitere Treffer auf dem Server“.

## 5. Verfassen: Anhänge, Signatur, Kontakte
- Anhänge per Button und Drag & Drop; Hinweis ab 20 MB Gesamtgröße. Nachricht wird `multipart/mixed`
  (Text als erster Teil, Anhänge Base64 mit `Content-Disposition: attachment; filename*=UTF-8''…`).
- Weiterleiten übernimmt die Anhänge der Originalnachricht.
- **Signatur** pro Konto (Text, Einstellungen → Konten). Wird bei neuer Mail, Antwort und Weiterleitung als
  `\n\n-- \n<Signatur>` vor dem Zitat eingefügt.
- **Kontakte**: aus Absendern, Empfängern und gesendeten Mails gesammelt (`contacts.json`:
  `{ "<adresse>": { "name": "…", "count": 3, "lastUsed": "ISO-8601" } }`). Autovervollständigung in An/Cc/Bcc
  (Treffer in Name oder Adresse, sortiert nach count, dann lastUsed).

## 6. Mehrfachauswahl & Tastenkürzel
- In der Liste mehrere Nachrichten auswählen (⌘/Strg-Klick, Shift-Klick, ⌘A/Strg+A) → Leiste mit
  Gelesen, Ungelesen, Markieren, Archivieren, Löschen, Spam.
- Tastenkürzel (wenn kein Textfeld den Fokus hat): `j`/`k` nächste/vorige, `e` Archivieren, `#` Löschen,
  `!` Spam, `r` Antworten, `a` Allen antworten, `f` Weiterleiten, `s` Markieren, `u` Gelesen/Ungelesen,
  `c` Neue E-Mail, `/` Suche.

## 7. Ungelesen-Zähler für alle Ordner
- Beim Neuladen: `STATUS <Ordner> (UNSEEN)` für alle auswählbaren Ordner außer Gesendet, Entwürfe, Papierkorb
  und Archiv. Zähler in der Sidebar.

## 8. Konversationen
- Umschalter in der Liste („Als Konversationen“). Gruppierung pro Konto und Ordner über `Message-ID`,
  `In-Reply-To` und `References` (Header-Felder werden mitgeladen); Nachrichten ohne Bezug werden über den
  normalisierten Betreff (ohne `Re:`, `AW:`, `Fwd:`, `WG:`, `FW:`, `Ответ:`) und gemeinsame Teilnehmer zugeordnet.
- Listeneintrag: neueste Nachricht, Anzahl, Teilnehmer. Lesebereich zeigt alle Nachrichten untereinander
  (ältere eingeklappt).

## 9. Anbieter-Posteingang
- Hat ein Anbieter mehrere Konten, wird seine Überschrift in der Sidebar klickbar: „Alle <Anbieter>-Posteingänge“
  (Kategorie Posteingang aller Konten dieses Anbieters).

## 10. Eigene Regeln und Kategorien
`rules.json` (Version 2; die alte flache Form `{ "<absender>": "<kategorie>" }` wird als `senders` gelesen):
```json
{
  "version": 2,
  "senders": { "news@shop.de": "promotions", "firma.de": "primary" },
  "rules": [ { "field": "subject", "contains": "rechnung", "category": "custom-rechnungen" } ],
  "customCategories": [ { "id": "custom-rechnungen", "name": "Rechnungen", "color": "#22C55E", "icon": "doc.text" } ]
}
```
- `field`: `from` (Name oder Adresse), `to` (alle Empfänger), `subject`. Vergleich: Teilstring, ohne Groß-/Kleinschreibung.
- Reihenfolge im Klassifizierer: **senders** → **rules** (erste passende gewinnt) → Gmail-Server-Kategorie → Regeln
  aus `categories.json`.
- Eigene Kategorien erscheinen bei jedem Konto unter den festen Kategorien. Verwaltung in Einstellungen → Regeln.

## 11. Newsletter mit einem Klick abbestellen
- Header `List-Unsubscribe` und `List-Unsubscribe-Post` werden mitgeladen. Im Lesebereich Button „Abbestellen“:
  1. `https:`-Adresse **und** `List-Unsubscribe-Post: List-Unsubscribe=One-Click` → `POST` mit Body
     `List-Unsubscribe=One-Click` (RFC 8058), ohne Cookies.
  2. sonst `mailto:` → kurze Mail an diese Adresse (Betreff aus `?subject=`, sonst „unsubscribe“) vom Konto aus.
  3. sonst `https:` → im Browser öffnen.
- Vorher Rückfrage; danach Absender-Regel „Werbung“ optional anbieten.

## 12. Werbung automatisch aufräumen
- Einstellung je Kategorie Werbung / Soziale Netzwerke / Updates / Foren: „Aufräumen nach“ Aus (Standard) / 7 / 30 / 90 Tagen.
- Nach jedem Neuladen: geladene Nachrichten dieser Kategorie, älter als die Frist und nicht markiert
  → Archiv (ohne Archiv-Ordner: Papierkorb). Höchstens 100 pro Durchlauf.

## 13. Später erinnern & später senden
- **Später erinnern**: Heute 18:00 / Morgen 08:00 / Montag 08:00 / Datum wählen. Die Nachricht wird lokal
  ausgeblendet (`snoozed.json`: `[{ "accountId", "folder", "messageId", "uid", "until" }]`), zur Zeit wieder als
  ungelesen markiert, oben einsortiert und gemeldet. Übersicht → „Erinnerungen“ zeigt alle.
- **Später senden**: im Verfassen-Fenster „Später senden …“ (Datum/Uhrzeit). Gespeichert in `outbox/<id>.eml`
  plus `outbox/<id>.json` (`accountId`, `sendAt`, Empfänger). Versand, sobald fällig und die App läuft
  (beim Start werden überfällige sofort gesendet). Übersicht → „Geplant“ mit „Jetzt senden“ / „Löschen“.

## 14. Datenschutz & Sicherheit
- **Tracking-Bericht**: im HTML erkannte Tracker = `<img>` mit `width`/`height` ≤ 1, oder deren Host (bzw. eine
  Elternzone) in `shared/trackers.json → domains` steht. Hinweis im Lesebereich: „3 Tracker blockiert (list-manage.com, …)“.
- **Phishing-Warnung** (`shared/phishing.json`), Warnleiste wenn
  1. der Anzeigename eine E-Mail-Adresse mit anderer Domain als der Absender enthält, oder
  2. der Anzeigename eine bekannte Marke nennt, die Absender-Domain aber nicht zu deren Domains gehört, oder
  3. ein Link-Text wie eine Adresse aussieht (`http…` oder `www.…`), aber auf eine andere Domain zeigt.
- **App-Sperre**: Einstellung „Lu-Mail sperren mit Touch ID / Windows Hello“ (aus). Sperrt beim Start und nach
  5 / 15 / 60 Minuten im Hintergrund; Entsperren über das System (Touch ID/Passwort bzw. Windows Hello/PIN).

## 15. Weitere Anbieter
In `shared/providers.json`: **Outlook/Hotmail** (nur OAuth, eigene Microsoft-Client-ID in `oauth.json`),
**iCloud Mail** (app-spezifisches Passwort, SMTP 587 STARTTLS), **GMX**, **WEB.DE**, **Yandex** und
**Andere (IMAP)** mit frei wählbaren Servern. `security` kann jetzt `tls` (sofort TLS) oder `starttls` sein.
Bei „Andere (IMAP)“ zeigt die Browser-Anmeldeseite zusätzlich Felder für IMAP-/SMTP-Server, Port und
Verschlüsselung; die Werte werden pro Konto in `accounts.json → customServers` gespeichert.

## 16. Erscheinungsbild
`shared/theme/lu-theme.json → variants`: **Luna** (Standard), **Neumond** (tiefschwarz, für OLED),
**Vollmond** (warm-hell), jeweils mit eigenen Paletten; dazu Akzentfarben (Lila, Blau, Türkis, Grün, Rosa, Gold).
Einstellungen → Darstellung: Modus System/Hell/Dunkel, Variante, Akzentfarbe.

## 17. Konto hinzufügen – einfach (Assistent)

Ein Dialog/Fenster mit zwei Schritten; der Haupt-Button ist **immer sichtbar** (Fußzeile, nicht im scrollbaren Inhalt).

**Schritt 1 – „Wie lautet deine E-Mail-Adresse?“**
- Großes Eingabefeld oben, darunter eine kompakte Anbieter-Auswahl (3 Spalten, kleine Kacheln, alle 10 ohne
  Scrollen sichtbar). Klick auf eine Kachel → direkt Schritt 2.
- „Weiter“ mit eingegebener Adresse → Anbieter-Erkennung (siehe unten) → Schritt 2. Während der Suche:
  „Suche Einstellungen für <domain> …“ (höchstens `discovery.timeoutSeconds`).

**Anbieter-Erkennung** (`shared/providers.json`):
1. Domain steht in `domains` eines Anbieters → dieser Anbieter.
2. Sonst MX-Abfrage per DNS-over-HTTPS (`discovery.mxLookup`, Header `Accept: application/dns-json`,
   Antwortfeld `Answer[].data` = „10 mx1.example.com.“). Endet ein MX-Host (ohne Punkt am Ende) auf ein
   `mxSuffixes`-Element eines Anbieters (gleich oder `.suffix`) → dieser Anbieter (z. B. Google Workspace → Gmail,
   Hostinger-Domains → Hostinger).
3. Sonst Autoconfig (Thunderbird-Format) der Reihe nach aus `discovery.autoconfig` (`{domain}`, `{email}`
   URL-kodiert ersetzen, nur HTTPS): erster `incomingServer type="imap"` und erster `outgoingServer type="smtp"`
   mit `socketType` `SSL` (→ `tls`) oder `STARTTLS` (→ `starttls`) → Anbieter „Andere (IMAP)“ mit vorausgefüllten Servern.
4. Sonst „Andere (IMAP)“ mit leeren Serverfeldern.

**Schritt 2 – Anbieter-Seite**
- Kopf mit Anbieterfarbe und Name, darunter „So geht's“ als nummerierte Liste aus `setup.oauthSteps` (wenn eine
  OAuth-Client-ID verfügbar ist, §18) bzw. `setup.passwordSteps`. Schritte mit `url` bekommen einen Button
  (`button`), der die Seite im Standardbrowser öffnet.
- `setup.checkLocalPort` (Proton): TCP-Verbindungstest auf 127.0.0.1:<port> → „✓ Bridge läuft“ bzw.
  „✗ Bridge nicht gefunden – bitte starten“ (mit „Erneut prüfen“).
- Fußzeile: „Zurück“ und Haupt-Button „Mit Google anmelden“ / „Mit Microsoft anmelden“ / „Mit Mail.ru anmelden“
  (OAuth) bzw. „Im Browser anmelden“ (Passwort). OAuth-only ohne Client-ID (Outlook): Button deaktiviert, Hinweis auf §18.
- Danach: Warte-Ansicht „Bitte im Browser fortfahren …“ mit „Abbrechen“; bei Erfolg „✓ <adresse> wurde
  hinzugefügt“ und der Dialog schließt nach 1,5 s; bei Fehler eine verständliche Meldung und „Erneut versuchen“.
- Die eingegebene Adresse und erkannte Server werden an die Browser-Anmeldeseite übergeben (vorausgefüllt).

## 18. Eingebaute OAuth-Client-IDs

Damit „Mit Google/Microsoft anmelden“ ohne eigene `oauth.json` funktioniert, baut die CI Client-IDs aus
GitHub-Secrets in die Apps ein: `LUMAIL_GOOGLE_CLIENT_ID`, `LUMAIL_GOOGLE_CLIENT_SECRET`,
`LUMAIL_MICROSOFT_CLIENT_ID`, `LUMAIL_MAILRU_CLIENT_ID`, `LUMAIL_MAILRU_CLIENT_SECRET`.
- Format wie `oauth.json` (`{ "gmail": { "clientId", "clientSecret" }, "outlook": {…}, "mailru": {…} }`).
- macOS: `macos/Sources/LuMailCore/Generated/BuiltInOAuth.swift` (`BuiltInOAuth.json`), Windows:
  `windows/src/LuMail.Core/BuiltInOAuth.json` als EmbeddedResource. Im Repository stehen leere Platzhalter (`{}`);
  `scripts/write-builtin-oauth.py` füllt sie in der CI.
- Reihenfolge: Eintrag in der Nutzer-`oauth.json` → eingebauter Eintrag → keiner (Passwort-Anmeldung).
- Desktop-Client-„Secrets“ sind bei Google/Mail.ru nicht geheim (RFC 8252); Microsoft nutzt einen öffentlichen Client ohne Secret.

## 19. Oberfläche

- Einstellungen als eigenes, in der Größe veränderbares Fenster mit Seitenleiste (Allgemein, Darstellung, Konten,
  Regeln, Datenschutz, Updates) – keine abgeschnittenen Reiter.
- „Neue E-Mail“ ohne Konto öffnet „Konto hinzufügen“.
- Seitenleiste: Anbieter und Konten lassen sich auf- und zuklappen (Pfeil bzw. Klick auf die Kopfzeile); der Zustand
  wird pro Anbieter-ID und Konto-ID gemerkt. Zugeklappt zeigen sie die Zahl ungelesener Mails.
- Hinweise nutzen die passende Art (Info/Erfolg/Warnung/Fehler), z. B. „Bitte zuerst ein Konto hinzufügen“ = Info.

## 20. Web-Konten (Anmeldung auf der Anbieter-Webseite)

Problem: Gmail verlangt für IMAP ein App-Passwort oder eine eigene OAuth-Client-ID, Proton Mail die kostenpflichtige
Bridge, Mail.ru ein App-Passwort. Lösung: Ein **Web-Konto** meldet sich ganz normal auf der echten Webseite des
Anbieters an (normales Passwort, 2FA, Captcha – alles wie im Browser) und zeigt das Webmail-Postfach direkt in Lu-Mail.
Das ist ab sofort der **Standardweg** im Assistenten; IMAP bleibt als „Erweitert“ verfügbar.

**Daten (`shared/providers.json`)**
- `authMethods` enthält `"web"` (alle außer „Andere (IMAP)“). Neuer Wert für `Account.authMethod`: `web`.
- Pro Anbieter `web`:
  - `loginUrl` – Startseite der Anmeldung (passt nie zu `loggedInPrefixes`).
  - `homeUrl` – Postfach nach der Anmeldung.
  - `loggedInPrefixes` – erreicht die Adresse der Seite (auch per JavaScript/Hash-Navigation) einen dieser Anfänge,
    gilt die Anmeldung als abgeschlossen. Leer (iCloud, Hostinger, …) = nur über den Button „Fertig“.
  - `folders` – Webadresse je Ordner: Kategorien `primary`, `promotions`, `social`, `updates`, `forums` und Rollen
    `sent`, `drafts`, `archive`, `spam`, `trash`. Fehlende Schlüssel werden in der Seitenleiste nicht angezeigt.
  - `unreadTitlePattern` – regulärer Ausdruck, erste Gruppe = Anzahl ungelesener Mails im Seitentitel.
- `webUserAgent.macos` – User-Agent für WKWebView (Safari-Kennung, sonst lehnen manche Anbieter die Anmeldung ab).
  Windows nutzt den Standard-User-Agent von WebView2 (Edge).

**IMAP zuerst, wo das normale Passwort genügt:** Beginnt `authMethods` mit `"password"` (Hostinger: normales
Postfach-Passwort, kein App-Passwort), ist IMAP der Standardweg im Assistenten; die Web-Anmeldung steht als
„Stattdessen auf der Webseite anmelden“ bereit. Bestehende Web-Konten solcher Anbieter zeigen in der Webseiten-Ansicht
den Hinweis „<Anbieter> geht auch direkt in Lu-Mail – mit deinem normalen Passwort.“ und den Button **„Jetzt
umstellen“**: Passwort-Anmeldung im Browser mit vorausgefüllter Adresse; klappt sie, ersetzt das IMAP-Konto das
Web-Konto (dessen Cookies werden gelöscht) und sein Posteingang wird ausgewählt.

**Sitzungen**
- Jedes Web-Konto hat einen **eigenen, dauerhaften Cookie-Speicher** (mehrere Gmail-Konten parallel möglich):
  macOS `WKWebsiteDataStore(forIdentifier: account.id)`, Windows WebView2-Profil `account.id` (eigener Profilname,
  gemeinsamer Benutzerdatenordner `%LOCALAPPDATA%\LuMail\WebView2`).
- Es werden **keine Passwörter gespeichert** – nur die Cookies des Anbieters im Profil.
- Konto entfernen löscht den Speicher bzw. das Profil.
- Abgemeldet (z. B. Cookies abgelaufen)? Das Postfach zeigt einfach wieder die Anmeldeseite des Anbieters.

**Assistent „Konto hinzufügen“ (§17, erweitert)**
- Schritt 2 zeigt für Anbieter mit `web` als Hauptbutton **„Bei <Anbieter> anmelden“** und den Hinweis
  „Ganz normal mit deinem Passwort – kein App-Passwort, keine Bridge nötig.“
  Darunter ein kleiner Link „Erweitert: per IMAP verbinden (App-Passwort/OAuth)“, der die bisherige Anleitung zeigt.
- Klick öffnet die Anmeldeseite (`loginUrl`) in einem großen Fenster/Schritt innerhalb von Lu-Mail mit eingebettetem
  Browser, Kopfzeile „Melde dich bei <Anbieter> an“ plus „Abbrechen“ und „Fertig“.
- Abgeschlossen, sobald die Adresse zu `loggedInPrefixes` passt oder „Fertig“ geklickt wird → Konto anlegen
  (`authMethod = web`, E-Mail aus Schritt 1, sonst später aus dem Seitentitel, sonst Anbietername) und in der
  Seitenleiste dessen Posteingang auswählen. Der eingebettete Browser wird dabei weiterverwendet (kein zweiter Login).
- Abbrechen löscht den gerade angelegten Speicher.

**Anzeige**
- Seitenleiste: Anbieter → Konto → Einträge aus `folders` in der Reihenfolge Posteingang, Werbung, Soziale Netzwerke,
  Updates, Foren, Gesendet, Entwürfe, Archiv, Spam, Papierkorb (Namen wie bei IMAP-Konten, Kategorien nur wenn
  „Kategorien getrennt anzeigen“ an ist, sonst nur Posteingang + Rollen).
- Auswahl eines Eintrags ersetzt Liste + Lesebereich durch das Webmail des Kontos, navigiert zur Ordneradresse.
  Kleine Leiste darüber: Zurück, Vor, Neu laden, Kontoadresse.
- Pro Web-Konto bleibt ein Browser im Hintergrund geladen (schnelles Umschalten, Zähler aktuell).
- Ungelesen-Zähler: Seitentitel mit `unreadTitlePattern` auswerten, aber nur solange die Seite im Posteingang ist
  (Adresse beginnt mit `folders.primary` oder `homeUrl`); Wert erscheint am Konto und am Posteingang, zählt in
  Dock-/Taskleisten-Badge mit. Steigt er, kommt eine Benachrichtigung „<Konto>: N neue E-Mail(s)“ (wenn
  Benachrichtigungen an sind); Klick öffnet den Posteingang des Kontos.
- E-Mail-Adresse leer und Seitentitel enthält eine Adresse (z. B. „Inbox (3) - name@gmail.com - Gmail“) → übernehmen.
- Pop-ups/neue Fenster: Ziel-Host gehört zum Anbieter (gleiche registrierbare Domain wie eine der `web`-Adressen,
  vereinfacht: letzte zwei Host-Teile) → im selben eingebetteten Browser öffnen; sonst im Standardbrowser.
  Downloads (Anhänge) landen im Download-Ordner.
- „Alle Posteingänge“, Ungelesen, Markiert, Suche, Regeln, Verfassen usw. betreffen nur IMAP-Konten. „Neue E-Mail“
  ohne IMAP-Konto öffnet den Posteingang des ersten Web-Kontos mit Hinweis „Schreibe direkt im Postfach (Verfassen)“.

## 21. Lu-Ansicht für Web-Konten

Web-Konten (§20) zeigen ihre Mails standardmäßig **in Lu-Mails eigener Oberfläche** (Liste + Lesebereich im
Lu-Theme) statt der Webseite. Die Webseite des Anbieters lädt dafür unsichtbar im Hintergrund (derselbe Browser
mit demselben Cookie-Speicher wie in §20); `shared/webmail/lu-webmail.js` liest Liste und geöffnete Nachricht aus.

**Daten:** `web.reader` in `shared/providers.json` (`gmail`, `proton`, `mailru`). Anbieter ohne `reader` zeigen nur
die Webseite. Das Skript ist auf beiden Plattformen eingebettet (macOS: `SharedResources.webmailJS`, Windows:
EmbeddedResource) und wird vor jedem Aufruf mitgeschickt (es definiert `window.LuWebmail` nur einmal); macOS führt es
in einer eigenen Content-World aus (`WKContentWorld.defaultClient`), Windows per `ExecuteScriptAsync`
(Ergebnis ist ein JSON-String, den WebView2 noch einmal als JSON-String kodiert).

**Aufrufe** (Ergebnis jeweils JSON, siehe Kopf von `lu-webmail.js`):
- `LuWebmail.list(reader)` → `state` `ok` (mit `items`), `loading` (Liste noch nicht da), `login` (Passwortfeld
  sichtbar = abgemeldet), `unsupported`, `error`.
- `LuWebmail.open(reader, id)` → klickt den Eintrag im Webmail an (`ok`/`missing`).
- `LuWebmail.message(reader)` → `subject` und `parts` (`from`, `fromEmail`, `to`, `date`, `html`) der geöffneten
  Nachricht bzw. Unterhaltung.

**Ablauf**
1. Ordner wählen → der Hintergrund-Browser geht zur Ordneradresse (steht er in einer Nachricht dieses Ordners,
   also Adresse beginnt mit der Ordneradresse, genügt `history.back()`), dann alle 0,7 s `list` bis `ok` –
   eine leere Liste gilt erst nach 4 s als „leer“. Nach 20 s ohne `ok` bzw. bei `error`: Hinweis „Die Lu-Ansicht
   kann <Anbieter> gerade nicht lesen“ und automatisch die Webseiten-Ansicht. `login` → Webseiten-Ansicht
   (dort neu anmelden).
2. Liste im Stil der IMAP-Liste: Absender (fett + Punkt wenn ungelesen), Betreff, Vorschau, Datum (Text aus dem
   Webmail), Büroklammer bei Anhang. Neu laden über den Button, beim Zurückkehren zum Ordner und jede Minute,
   solange die Ansicht offen ist.
3. Nachricht wählen → `open` (klickt den Eintrag auch in einer ausgeblendeten Liste an; fehlt er, öffnet der Leser
   die Nachricht über die Adresse: Gmail `#<ordner>/<thread-id>`, Proton `/u/<n>/<ordner>/<ID>` per History-API),
   dann alle 0,5 s `message(reader, id)` bis `ok` (max. 15 s) – mit der ID meldet der Leser „loading“, solange noch
   eine andere Unterhaltung angezeigt wird. Der Lesebereich wird beim Wechsel sofort geleert. Der Lesebereich zeigt Betreff,
   pro Teil Absender, Empfänger, Datum und den Inhalt über dieselbe HTML-Aufbereitung wie IMAP-Mails
   (ohne JavaScript, externe Bilder blockiert mit „Externe Inhalte laden“). Der Eintrag gilt danach als gelesen
   (der Anbieter markiert ihn beim Öffnen selbst).
4. Antworten, Weiterleiten, Löschen usw.: Button „In der Webseite öffnen“ – schaltet auf die Webseiten-Ansicht, in
   der genau diese Nachricht schon offen ist. „Neue E-Mail“ öffnet die Webseite (Verfassen).
- Umschalter in der Leiste oben: **Lu-Ansicht / Webseite** (pro Konto gemerkt; Standard Lu-Ansicht).
- Aufrufe pro Konto laufen nacheinander (eine Warteschlange), damit Liste und Nachricht sich nicht stören.
- **Mitschnitt** (`shared/webmail/lu-capture.js`): läuft im Seitenkontext ab Dokumentbeginn (macOS: `WKUserScript`
  in der Page-World, Windows: `AddScriptToExecuteOnDocumentCreatedAsync`) und merkt sich die Metadaten aus den
  Listen-Antworten der Webmail-API (Proton: `/api/mail/v4/conversations|messages` – Absender, Betreff, Datum,
  ungelesen, Anhänge; verschlüsselte Inhalte werden verworfen) im Attribut `data-lu-capture` von `<html>`; spätere
  Antworten (z. B. Details einer Unterhaltung) werden mit vorhandenen Einträgen zusammengeführt. Der Leser nimmt
  Absender, Betreff, Datum und Anhänge aus diesen Daten, „ungelesen“ aber aus der Seite (Klasse `read`/`unread` des
  Eintrags, die Proton live aktualisiert) und nur ohne diese Klasse aus dem Mitschnitt. Fehlt der Mitschnitt, liest er
  Absender/Betreff aus dem Seitenaufbau bzw. notfalls aus den Textzeilen eines Eintrags. Platzhalter-Einträge, die
  Proton beim Laden zeigt (`placeholder-N`, `.item-is-loading`), gelten als „lädt noch“. Inhalte: pro aufgeklappter
  Nachricht `article[data-message-id]` → `iframe[data-testid="content-iframe"]`, Absender/Datum per ID aus dem Mitschnitt.
- **Qualitätsprüfung:** Hat mindestens die Hälfte der gelesenen Einträge weder Absender noch Betreff, meldet der
  Leser `state: "error"` (`reason: "unreadable"`) – Lu-Mail zeigt dann die Webseite statt einer Liste voller
  „(unbekannt)“.
- Tests: `node scripts/test-webmail.js` (nachgebaute Seiten in Chromium; die CI prüft die Syntax mit `node --check`).

## 22. Nachthimmel-Hintergrund

Ein animierter Nachthimmel mit Mond liegt hinter der Oberfläche (Einstellung *Darstellung → Animierter
Nachthimmel*, Standard an).

- **Himmel:** senkrechter Verlauf `#070A1C` (oben) → `#141638` → `#24205A` (unten), darüber ein schwacher violetter
  Schleier (Akzentfarbe, 8 %) unten links.
- **Sterne:** 160 Stück an festen Zufallspositionen (gleicher Startwert bei jedem Start), 0,8–2,4 px, weiß bis leicht
  bläulich/gelblich; jeder funkelt (Deckkraft 0,25 ↔ 1, eigene Periode 2–6 s, versetzt). Die Sternenebene driftet
  sehr langsam waagerecht (±12 px in 90 s, hin und zurück).
- **Mond:** oben rechts (82 % Breite, 16 % Höhe), Radius 38 px, Farbe `moon` des Themes aufgehellt
  (`#FFF4D6` → `#F5D06F`), drei dezente Krater, weicher Schein (Radius 3×, Deckkraft 0,35 → 0), der Schein
  „atmet“ (0,25 ↔ 0,4 in 6 s) und der Mond schwebt ±3 px in 8 s.
- **Sternschnuppe:** alle 9–18 s (zufällig) eine kurze helle Linie mit Schweif, die in 0,9 s schräg nach unten links
  zieht und ausblendet.
- **Lesbarkeit:** Im dunklen Modus sind Seitenleiste und Nachrichtenliste durchscheinend (Hintergrund 72 % deckend),
  der Lesebereich mit geöffneter Nachricht bleibt voll deckend. Ohne geöffnete Nachricht (leerer Lesebereich
  „Keine Nachricht ausgewählt“) ist er nur 15 % deckend – dort steht der Mond. Im hellen Modus bleiben alle Flächen deckend; der Himmel ist dann nur auf
  dem Willkommens- und dem Sperrbildschirm zu sehen.
- **Leistung:** höchstens 30 Bilder/s, angehalten wenn das Fenster nicht sichtbar ist; bei *Bewegung reduzieren*
  (macOS) bzw. ausgeschalteten Animationen (Windows) steht der Himmel still.

## 23. Updates auf dem Mac (wie Moon Browser)

Die Mac-Version ist nicht mit einer Apple-Developer-ID signiert, deshalb aktualisiert sich Lu-Mail selbst:
1. Alle `checkIntervalHours` (shared/update.json) neueste Version auf GitHub prüfen; im Modus *Automatisch* im
   Hintergrund laden: DMG + SHA-256 aus `SHA256SUMS.txt` prüfen, App ins Cache-Verzeichnis kopieren und dort prüfen:
   Bundle-ID `de.lunaos.lumail`, Version = angekündigte Version, `codesign --verify --deep --strict`.
2. Liegt sie bereit, erscheint in der Symbolleiste ein Button **„Update“** (Akzentfarbe) und oben die Leiste
   „Lu-Mail X ist bereit“. Klick → Lu-Mail merkt sich die aktuelle Auswahl (Ordner/Konto), beendet sich, ein kleines
   Skript tauscht die App aus (alte App erst danach löschen, bei Fehler zurück) und öffnet die neue Version, die die
   Auswahl wiederherstellt. Ohne Klick wird beim Beenden installiert.
3. Kann sich die App nicht selbst ersetzen (gestartet aus dem Disk-Image, App Translocation oder Ordner ohne
   Schreibrecht), öffnet „Update“ die Download-Seite des Releases und erklärt kurz, die App nach *Programme* zu ziehen.
4. **Terminal:** Jedes Release enthält `update-mac.sh` (aus `scripts/update-mac.sh`):
   `curl -fsSL https://github.com/Luna-OS/Lu-Mail/releases/latest/download/update-mac.sh | bash`
   – findet Lu-Mail (`/Applications`, `~/Applications`, Spotlight per Bundle-ID), lädt das neueste DMG, prüft
   SHA-256, Bundle-ID, Version und Signatur, beendet Lu-Mail, tauscht die App aus (bei fehlendem Schreibrecht mit
   `sudo`), entfernt das Quarantäne-Attribut und öffnet sie wieder. Konten, Einstellungen und Schlüsselbund bleiben
   (sie liegen in der Library, nicht in der App).
