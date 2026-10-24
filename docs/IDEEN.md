# Ideen für Lu-Mail

Vorschläge für die nächsten Ausbaustufen – grob nach Nutzen sortiert.

## Bald sinnvoll
- **Push statt Abruf (IMAP IDLE):** neue Mails in Sekunden statt alle 3 Minuten.
- **Mitteilungen:** System-Benachrichtigung bei neuen Mails im Posteingang (nicht bei Werbung).
- **Offline-Cache:** Nachrichten lokal (SQLite) speichern → sofortiger Start, Lesen ohne Internet, schnelle Suche.
- **Serversuche:** Suche nicht nur in geladenen, sondern in allen Nachrichten (IMAP `SEARCH`, Gmail `X-GM-RAW`).
- **Anhänge senden** und Drag & Drop in das Verfassen-Fenster; HTML-Signatur pro Konto.
- **Mehrfachauswahl:** viele Mails auf einmal archivieren, löschen, als gelesen markieren.
- **Ungelesen-Zähler für alle Ordner** per IMAP `STATUS`.

## Komfort
- **Newsletter-Abmelden mit einem Klick** (Header `List-Unsubscribe` / RFC 8058) – direkt im Ordner „Werbung“.
- **Werbung automatisch aufräumen:** z. B. alles älter als 30 Tage in „Werbung“ archivieren.
- **Später erinnern (Snooze)** und **Später senden**.
- **Tastaturkürzel wie Gmail** (j/k, e, #, r, a, f).
- **Konversations-Ansicht** (Threads gruppieren).
- **Gemeinsamer Posteingang pro Anbieter** („Alle Gmail-Konten“).
- **Eigene Regeln/Filter** über Absender hinaus (Betreff, Empfänger, Stichwörter) und eigene Kategorien.
- **Kontakte & Autovervollständigung** aus bisherigen Empfängern.

## Datenschutz & Sicherheit
- **Tracking-Bericht:** zeigen, welche Tracker in einer Mail blockiert wurden.
- **Phishing-Warnung,** wenn Anzeigename und Absenderadresse nicht zusammenpassen.
- **App-Sperre** mit Touch ID / Windows Hello.
- **PGP/S-MIME** für verschlüsselte Mails (für Proton-Kontakte besonders praktisch).

## Weitere Anbieter
- **Outlook/Hotmail** und **iCloud** (Outlook ebenfalls mit Browser-OAuth), **GMX/Web.de**, **Yandex**,
  **„Anderer IMAP-Server“** mit frei wählbarem Server.

## Lu-Mail-Welt
- **Theme-Varianten** (z. B. „Neumond“ ganz dunkel, „Vollmond“ hell, Akzentfarbe frei wählbar) – die Tokens liegen
  schon zentral in `shared/theme/lu-theme.json`.
- **Menüleisten-/Taskleisten-Symbol** mit Ungelesen-Zähler.
- **Smarte Zusammenfassung:** lange Newsletter oder Threads in zwei Sätzen zusammenfassen.
- **iOS/iPadOS-App** auf Basis von `LuMailCore` (der Kern ist schon plattformunabhängig).
