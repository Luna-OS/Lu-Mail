# Ideen für Lu-Mail

## ✅ Umgesetzt (macOS und Windows, Details in [FUNKTIONEN.md](FUNKTIONEN.md))

| Idee | Wo |
|------|----|
| Push statt Abruf (IMAP IDLE) | §1 |
| Mitteilungen, Dock-/Taskleisten-Zähler, Menüleiste/Infobereich | §2 |
| Offline-Cache | §3 |
| Suche auf dem Server (Gmail mit `X-GM-RAW`) | §4 |
| Anhänge senden, Drag & Drop, Signatur pro Konto, Kontakte mit Autovervollständigung | §5 |
| Mehrfachauswahl und Tastenkürzel wie Gmail | §6 |
| Ungelesen-Zähler für alle Ordner (IMAP `STATUS`) | §7 |
| Konversationen | §8 |
| Posteingang pro Anbieter | §9 |
| Eigene Regeln und Kategorien | §10 |
| Newsletter mit einem Klick abbestellen (RFC 8058) | §11 |
| Werbung automatisch aufräumen | §12 |
| Später erinnern und später senden | §13 |
| Tracking-Bericht, Phishing-Warnung, App-Sperre (Touch ID / Windows Hello) | §14 |
| Outlook, iCloud, GMX, WEB.DE, Yandex, „Andere (IMAP)“, STARTTLS | §15 |
| Theme-Varianten Luna / Neumond / Vollmond und Akzentfarben | §16 |
| Downloads (.dmg / Setup.exe) mit Auto-Updater | [ARCHITEKTUR.md](ARCHITEKTUR.md#downloads--auto-updater) |

## ⏳ Bewusst noch offen

- **PGP / S/MIME:** Verschlüsselung muss kryptografisch einwandfrei sein (Schlüsselverwaltung, Signaturprüfung,
  Vertrauensmodell). Das sollte auf einer geprüften Bibliothek aufbauen (z. B. GopenPGP/Sequoia bzw. MimeKit
  `MimeKit.Cryptography`) und gehört in ein eigenes, sorgfältig getestetes Paket.
- **Smarte Zusammenfassungen (KI):** Würde Mail-Inhalte an einen KI-Dienst schicken. Dafür braucht es eine
  bewusste Entscheidung (welcher Anbieter, eigener API-Schlüssel, Opt-in pro Nachricht). Sinnvoll als
  optionale Funktion mit eigenem Schlüssel in den Einstellungen.
- **iOS/iPadOS-App:** `LuMailCore` ist bereits plattformunabhängig; die App braucht aber eine eigene
  Oberfläche (SwiftUI für iPhone/iPad), Hintergrundabruf und App-Store-Signierung – ein eigenes Projekt.
- **Signierte Downloads:** Mit einem Apple-Developer-Zertifikat (Notarisierung) und einem
  Code-Signing-Zertifikat für Windows entfallen die Warnungen beim ersten Start.

## 💡 Neue Ideen

- Gemeinsame Kalender-/Kontakt-Synchronisierung (CalDAV/CardDAV).
- „Später lesen“-Liste und Wochenrückblick („Diese Woche: 42 Werbemails aufgeräumt“).
- Vorlagen (Textbausteine) im Verfassen-Fenster.
- Exportieren/Sichern eines Postfachs als `.mbox`.
