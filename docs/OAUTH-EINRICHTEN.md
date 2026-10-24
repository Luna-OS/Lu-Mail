# „Mit Google / Microsoft anmelden“ einrichten (einmalig)

Damit sich alle Lu-Mail-Nutzer mit **einem Klick** bei Gmail und Outlook anmelden können (ohne App-Passwort),
braucht Lu-Mail je eine eigene „OAuth-Client-ID“. Die legst du einmal an und hinterlegst sie als
GitHub-Secret – die CI baut sie dann in jede neue Version ein (docs/FUNKTIONEN.md §18).

Dauer: ca. 15 Minuten. Kosten: keine.

---

## 1. Google (Gmail)

1. Öffne die [Google Cloud Console](https://console.cloud.google.com/) und lege oben links ein **neues Projekt**
   „Lu-Mail“ an.
2. **APIs & Dienste → Bibliothek →** „Gmail API“ suchen → **Aktivieren**.
3. **Google Auth Platform → Branding** (früher „OAuth-Zustimmungsbildschirm“):
   App-Name „Lu-Mail“, deine E-Mail als Support- und Entwickler-Kontakt → Speichern.
4. **Zielgruppe:** Nutzertyp **Extern**. Danach auf **„App veröffentlichen“** (Status „In Produktion“) klicken.
   *Ohne Veröffentlichung laufen Anmeldungen nach 7 Tagen ab.* Solange Google die App nicht geprüft hat,
   sehen Nutzer beim Anmelden „Google hat diese App nicht überprüft“ → **Erweitert → Weiter zu Lu-Mail**
   (bis zu 100 Nutzer möglich; für mehr ist eine Google-Prüfung nötig).
5. **Datenzugriff → Bereiche hinzufügen:** `https://mail.google.com/` (manuell einfügen) sowie `openid` und
   `.../auth/userinfo.email` → Aktualisieren → Speichern.
6. **Clients → Client erstellen →** Anwendungstyp **Desktop-App**, Name „Lu-Mail“ → Erstellen.
7. **Client-ID** und **Clientschlüssel** kopieren.

## 2. Microsoft (Outlook, Hotmail, Live)

1. Öffne [Microsoft Entra → App-Registrierungen](https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
   (mit deinem Microsoft-Konto anmelden) → **Neue Registrierung**.
2. Name „Lu-Mail“, Kontotypen: **„Konten in einem beliebigen Organisationsverzeichnis und persönliche
   Microsoft-Konten“**.
3. Umleitungs-URI: Plattform **„Öffentlicher Client/nativ (mobil und Desktop)“**, URI `http://localhost/callback`
   → Registrieren.
4. **Authentifizierung →** „Öffentliche Clientflows zulassen“ auf **Ja** → Speichern.
5. **API-Berechtigungen → Berechtigung hinzufügen →** „Von meiner Organisation verwendete APIs“ →
   **Office 365 Exchange Online** → Delegierte Berechtigungen: `IMAP.AccessAsUser.All` und `SMTP.Send`.
   Zusätzlich unter **Microsoft Graph**: `offline_access`, `openid`, `email`.
6. Auf der Übersichtsseite die **Anwendungs-ID (Client-ID)** kopieren. (Ein Geheimnis ist nicht nötig.)

## 3. In GitHub hinterlegen

Im Repository **Settings → Secrets and variables → Actions → New repository secret**:

| Name | Wert |
|------|------|
| `LUMAIL_GOOGLE_CLIENT_ID` | Google Client-ID (endet auf `.apps.googleusercontent.com`) |
| `LUMAIL_GOOGLE_CLIENT_SECRET` | Google Clientschlüssel (beginnt mit `GOCSPX-`) |
| `LUMAIL_MICROSOFT_CLIENT_ID` | Microsoft Anwendungs-ID |
| `LUMAIL_MAILRU_CLIENT_ID` / `…_SECRET` | optional, für „Mit Mail.ru anmelden“ ([oauth.mail.ru/app](https://oauth.mail.ru/app/), Redirect `http://localhost/callback`, Rechte `userinfo` + `mail.imap`) |

Danach eine neue Version veröffentlichen (*Actions → CI → Run workflow* → z. B. `0.2.1`). Ab dieser Version zeigt
Lu-Mail „Mit Google anmelden“ bzw. „Mit Microsoft anmelden“, und bestehende Installationen bekommen das per
Auto-Update.

> Desktop-Apps können ihre Client-„Geheimnisse“ technisch nicht verstecken; Google und Microsoft sehen das für
> Desktop-Clients so vor (RFC 8252, PKCE). Echte Passwörter oder Tokens deiner Nutzer landen nie in GitHub.

## Nur für den eigenen Rechner

Statt GitHub-Secrets geht auch eine lokale `oauth.json` (Vorlage `shared/oauth.example.json`) in
`~/Library/Application Support/LuMail/` bzw. `%APPDATA%\LuMail\`. Einträge dort haben Vorrang.
