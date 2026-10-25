# Anmeldung in Lu-Mail

## Der einfache Weg: Web-Konto (empfohlen)

**Konto hinzufügen → Anbieter wählen → „Bei <Anbieter> anmelden“.** Lu-Mail öffnet die **echte Anmeldeseite**
des Anbieters (Gmail, Outlook, Proton Mail, Mail.ru, Yandex, iCloud, GMX, WEB.DE) in einem eigenen
Fenster. Du meldest dich dort ganz normal mit deinem Passwort an – auch mit Zwei-Faktor oder Bestätigungscode.
**Ausnahme Hostinger:** Dort genügt für IMAP das normale Postfach-Passwort, deshalb verbindet Lu-Mail Hostinger
direkt per IMAP (siehe [Hostinger](#hostinger)).

- **Kein App-Passwort, keine Proton Bridge, keine OAuth-Einrichtung nötig.**
- Lu-Mail speichert dein Passwort nicht; nur die Anmelde-Cookies des Anbieters liegen in einem eigenen Speicher
  pro Konto (mehrere Konten desselben Anbieters gleichzeitig möglich). Konto entfernen löscht ihn.
- Dein Postfach erscheint links unter Anbieter → Konto mit seinen Ordnern (bei Gmail z. B. Posteingang, Werbung,
  Soziale Netzwerke, Updates, Foren). Neue Mails zeigt Lu-Mail als Zahl und Mitteilung an.
- Details: docs/FUNKTIONEN.md §20.

## Erweitert: per IMAP verbinden

Im Assistenten unter **„Erweitert: per IMAP verbinden“** (bei Hostinger der Standardweg). Dann laufen Mails
über IMAP direkt in Lu-Mails eigener Liste (Alle Posteingänge, Regeln, Suche …). Die Anmeldung läuft dabei über
deinen **Standardbrowser**:

| Anbieter | Weg ohne Einrichtung | Direkte Anmeldung beim Anbieter |
|----------|----------------------|---------------------------------|
| Gmail | lokale Lu-Mail-Seite im Browser + **App-Passwort** | ✅ mit eigener Google-OAuth-Client-ID |
| Mail.ru | lokale Lu-Mail-Seite im Browser + **Passwort für externe Apps** | ✅ mit eigener Mail.ru-OAuth-Client-ID |
| Proton Mail | lokale Lu-Mail-Seite im Browser + **Bridge-Passwort** | – (Proton erlaubt das nicht) |
| Hostinger | lokale Lu-Mail-Seite im Browser + **Postfach-Passwort** | – (Hostinger bietet kein OAuth) |

**Lokale Lu-Mail-Seite:** Lu-Mail startet kurz einen Mini-Webserver, der nur auf deinem Rechner erreichbar ist
(`http://127.0.0.1:<zufälliger Port>`), und öffnet die Anmeldeseite im Browser. Nach dem Absenden prüft Lu-Mail
die Zugangsdaten sofort beim Anbieter und speichert sie im macOS-Schlüsselbund bzw. verschlüsselt (DPAPI) unter
Windows. Die Seite ist nur 10 Minuten und nur ein einziges Mal gültig.

**Direkte Anmeldung (OAuth):** Du landest auf der echten Seite von Google bzw. Mail.ru, meldest dich dort an und
bestätigst den Zugriff. Lu-Mail sieht dein Passwort nie und bekommt nur ein widerrufbares Token.

---

## Gmail

### Schnell: App-Passwort
1. Aktiviere die [2-Schritt-Bestätigung](https://myaccount.google.com/signinoptions/twosv).
2. Erstelle unter [App-Passwörter](https://myaccount.google.com/apppasswords) ein Passwort (Name z. B. „Lu-Mail“).
3. In Lu-Mail: **Konto hinzufügen → Gmail → Im Browser anmelden** und das 16-stellige Passwort eingeben.

### Direkt bei Google anmelden (OAuth)
1. In der [Google Cloud Console](https://console.cloud.google.com/) ein Projekt anlegen.
2. **APIs & Dienste → OAuth-Zustimmungsbildschirm**: Typ „Extern“, App-Name „Lu-Mail“, deine Adresse als
   Testnutzer eintragen (im Testmodus bis zu 100 Testnutzer, keine Google-Prüfung nötig).
3. Bereich (Scope) `https://mail.google.com/` hinzufügen.
4. **Anmeldedaten → Anmeldedaten erstellen → OAuth-Client-ID → Desktop-App**.
5. Client-ID und Clientschlüssel in `oauth.json` eintragen (siehe unten).

## Mail.ru

### Schnell: Passwort für externe Apps
1. Mail.ru → **Einstellungen → Sicherheit → Passwörter für externe Anwendungen** (`account.mail.ru`).
2. Neues Passwort mit Zugriff „Lesen und Schreiben (IMAP, SMTP)“ erstellen.
3. In Lu-Mail: **Konto hinzufügen → Mail.ru → Im Browser anmelden**.

### Direkt bei Mail.ru anmelden (OAuth)
1. Auf [oauth.mail.ru/app](https://oauth.mail.ru/app/) eine Anwendung registrieren.
2. Als Redirect-URI `http://localhost` angeben (Lu-Mail hängt Port und `/callback` an – falls Mail.ru einen
   exakten Treffer verlangt, die Redirect-URI in `shared/providers.json → redirectUriTemplate` anpassen).
3. Die Berechtigungen `userinfo` und `mail.imap` aktivieren.
4. Client-ID und Secret in `oauth.json` eintragen.

## Proton Mail

Proton Mail verschlüsselt Ende-zu-Ende und erlaubt E-Mail-Programmen den Zugriff **nur über die
[Proton Mail Bridge](https://proton.me/mail/bridge)** (ab Tarif „Mail Plus“).

1. Bridge installieren und mit deinem Proton-Konto anmelden.
2. In der Bridge: **Einstellungen → Erweitert → Verbindungsmodus** → IMAP **und** SMTP auf **SSL** stellen.
3. In der Bridge auf dein Konto klicken → **Mailbox-Konfiguration** → Bridge-Passwort kopieren.
4. In Lu-Mail: **Konto hinzufügen → Proton Mail → Im Browser anmelden**, Proton-Adresse und Bridge-Passwort eingeben.

Die Bridge muss laufen, solange du Lu-Mail benutzt. Lu-Mail akzeptiert ihr selbstsigniertes Zertifikat
ausschließlich für `127.0.0.1`.

## Hostinger

1. Volle E-Mail-Adresse (z. B. `info@deine-domain.de`) und Postfach-Passwort bereithalten.
   Vergessen? Im [hPanel](https://hpanel.hostinger.com/emails) unter **E-Mails → Konten** neu setzen.
2. In Lu-Mail: **Konto hinzufügen → Hostinger Mail → Im Browser anmelden** und dort Adresse und normales
   Postfach-Passwort eingeben – kein App-Passwort nötig. Lu-Mail zeigt das Postfach danach selbst an (Liste,
   Suche, Senden, Kategorien). Die Webseite bleibt als „Stattdessen auf der Webseite anmelden“ wählbar.
3. Schon als Web-Konto eingerichtet? In dessen Webseiten-Ansicht auf **„Jetzt umstellen“** klicken – das
   IMAP-Konto ersetzt das Web-Konto (dessen Cookies werden gelöscht).

Server: `imap.hostinger.com:993` und `smtp.hostinger.com:465` (SSL). Gesendete Mails legt Lu-Mail zusätzlich
im Ordner „Gesendet“ ab, weil Hostinger das beim Versand per SMTP nicht selbst macht.

---

## oauth.json

Lu-Mail liest die Datei bei jeder Anmeldung neu:

- **macOS:** `~/Library/Application Support/LuMail/oauth.json`
  (Einstellungen → Konten → „oauth.json im Finder zeigen“ legt sie aus der Vorlage an)
- **Windows:** `%APPDATA%\LuMail\oauth.json`

```json
{
  "gmail":  { "clientId": "1234-abc.apps.googleusercontent.com", "clientSecret": "GOCSPX-…" },
  "mailru": { "clientId": "…", "clientSecret": "…" }
}
```

Leere Einträge werden ignoriert – dann nutzt Lu-Mail für diesen Anbieter automatisch die Passwort-Anmeldung.
**Diese Datei gehört nicht ins Git-Repository** (steht in `.gitignore`).
