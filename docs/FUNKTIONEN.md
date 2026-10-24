# Lu-Mail – Funktionen (Spezifikation für macOS und Windows)

Ergänzt [ARCHITEKTUR.md](ARCHITEKTUR.md). Beide Apps setzen jede Funktion gleich um; gemeinsame Daten liegen
in `shared/`, App-Daten im App-Datenordner (`~/Library/Application Support/LuMail/` bzw. `%APPDATA%\LuMail\`).

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
