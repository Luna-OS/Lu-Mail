# Lu-Mail für Windows

Nativer E-Mail-Client für mehrere Konten (Gmail, Proton Mail über die Bridge, Mail.ru, Hostinger) –
C# / .NET 8, **WinUI 3** (Windows App SDK 1.6), IMAP/SMTP über **MailKit**.
Das Verhalten ist in [`docs/ARCHITEKTUR.md`](../docs/ARCHITEKTUR.md) festgelegt und identisch zur macOS-App.

## Projektstruktur

```
windows/
  LuMail.sln
  Directory.Build.props            gemeinsame Einstellungen (C# latest, Nullable, ImplicitUsings)
  src/LuMail.Core/                 plattformneutraler Kern (net8.0, ohne Windows-APIs)
    Models/                        Provider, Account, MailFolder, MessageSummary, MessageCategory …
    Catalog/                       ProviderCatalog, CategoryCatalog, ThemeCatalog (aus shared/)
    Classification/                CategoryClassifier (Schritte 1–8), FolderRoleResolver
    Auth/                          PKCE, OAuth-URL, Token-Tausch/-Refresh, id_token, XOAUTH2, oauth.json
    Login/                         Loopback-HTTP-Server + Browser-Anmeldung (OAuth und Passwort-Seite)
    Storage/                       AppPaths, ISecretStore, accounts.json, rules.json, settings.json
    Mail/                          MailService (MailKit), HTML-Aufbereitung (CSP), Antworten/Weiterleiten
  src/LuMail.App/                  WinUI-3-App (net8.0-windows10.0.19041.0, unpackaged, x64/ARM64)
    App.xaml                       Theme „Luna“ (Farben exakt aus shared/theme/lu-theme.json)
    MainWindow.cs                  Mica, eigene Titelleiste
    Views/                         MainPage, AddAccountDialog, SettingsDialog, ComposeWindow/ComposePage
    ViewModels/                    MainViewModel, SidebarItem, MessageItem (CommunityToolkit.Mvvm)
    Services/                      AppServices (Composition Root), DpapiSecretStore
  tests/LuMail.Core.Tests/         xUnit-Tests für den Kern (laufen auch unter Linux/macOS)
```

Die Dateien aus [`shared/`](../shared) (Anbieter, Kategorien, Theme, Anmeldeseiten) werden **nicht kopiert**,
sondern direkt als *EmbeddedResource* in `LuMail.Core` eingebunden (logischer Name `LuMail.Shared.<Dateiname>`).

## Voraussetzungen

- Windows 10 Version 1809 (Build 17763) oder neuer, Windows 11 empfohlen (Mica-Hintergrund)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.4xx oder neuer)
- Microsoft Edge **WebView2 Runtime** (unter Windows 11 vorinstalliert)
- Optional: Visual Studio 2022 (17.8+) mit der Workload „Windows-Anwendungsentwicklung“

Die Windows App SDK-Laufzeit wird mit ausgeliefert (`WindowsAppSDKSelfContained`), es ist keine
separate Installation nötig. Die App ist *unpackaged* (kein MSIX).

## Bauen und starten

```powershell
cd windows

# Bauen (x64 bzw. ARM64)
dotnet build src/LuMail.App/LuMail.App.csproj -c Release -p:Platform=x64
dotnet build src/LuMail.App/LuMail.App.csproj -c Release -p:Platform=ARM64

# Starten
dotnet run --project src/LuMail.App/LuMail.App.csproj -p:Platform=x64

# Eigenständige Version veröffentlichen (inkl. .NET-Laufzeit)
dotnet publish src/LuMail.App/LuMail.App.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true
```

In Visual Studio `windows/LuMail.sln` öffnen, Plattform **x64** (oder ARM64) wählen und `LuMail.App`
als Startprojekt setzen.

## Tests

Der Kern ist plattformneutral; die Tests laufen unter Windows, Linux und macOS:

```bash
dotnet test windows/tests/LuMail.Core.Tests
```

Getestet werden u. a. der Kategorien-Klassifizierer (alle Regeln, Reihenfolge, Nutzer-Regeln, Subdomains,
russische Stichwörter), die Ordnerrollen (Gmail, Hostinger `INBOX.Sent`, SPECIAL-USE), PKCE, OAuth-URL,
Token-Tausch, XOAUTH2, id_token, HTML-Escaping der Anmeldeseite, der Loopback-Server Ende-zu-Ende
(Host-Header-Prüfung, einmaliger `state`, Fehlermeldung bei falschem Passwort) und dass die Farben in
`App.xaml` exakt `shared/theme/lu-theme.json` entsprechen.

## Konto hinzufügen

„Konto hinzufügen“ → Anbieter wählen → **Im Browser anmelden**. Lu-Mail startet einen einmaligen
HTTP-Server nur auf `127.0.0.1`/`::1` (zufälliger Port, 10 Minuten gültig) und öffnet den Standardbrowser:

- **OAuth 2.0** (Gmail, Mail.ru), sobald `oauth.json` eine Client-ID für den Anbieter enthält.
- Sonst die lokale **Passwort-Seite** (`shared/login/login.html`) – für Proton Mail (Bridge-Passwort),
  Hostinger (Postfach-Passwort) sowie Gmail/Mail.ru mit App-Passwort. Die Zugangsdaten werden sofort per
  IMAP geprüft; bei einem Fehler erscheint die Seite erneut mit Fehlermeldung.

### Eigene OAuth-Client-IDs (optional)

`shared/oauth.example.json` nach `%APPDATA%\LuMail\oauth.json` kopieren und die eigenen Client-Daten
eintragen (Google Cloud Console bzw. oauth.mail.ru). Leere Einträge = Anmeldung mit App-Passwort.
Im Repository liegen bewusst **keine** Client-IDs oder Geheimnisse.

### Proton Mail

Proton funktioniert nur über die **Proton Mail Bridge**. In der Bridge unter
*Einstellungen → Erweitert → Verbindungsmodus* IMAP und SMTP auf **SSL** stellen. Lu-Mail verbindet sich mit
`127.0.0.1:1143` (IMAP) und `127.0.0.1:1025` (SMTP) und akzeptiert das selbstsignierte Bridge-Zertifikat
**nur** für Loopback-Adressen.

## Bedienung

| Aktion | Tastatur / Ort |
|--------|----------------|
| Aktualisieren | **F5**, Button unten in der Sidebar, automatisch alle 3 Minuten (einstellbar) |
| Neue E-Mail | **Strg+N** |
| Senden (im Verfassen-Fenster) | Button „Senden“ oder Strg+Eingabe |
| Antworten, Allen antworten, Weiterleiten, Archivieren, Löschen, Spam/Kein Spam, Markieren, Gelesen/Ungelesen | Aktionsleiste der Leseansicht |
| „Absender immer als … einsortieren“ | Aktionsleiste → „Einsortieren“ |
| Externe Bilder laden | Hinweisleiste „Externe Inhalte laden“ (oder dauerhaft in den Einstellungen) |

## Datenschutz

- HTML-Mails werden in WebView2 **ohne JavaScript** angezeigt. Vorangestellt ist die CSP
  `default-src 'none'; img-src data: cid:; style-src 'unsafe-inline'`; zusätzlich blockiert die App alle
  `http(s)`-Anfragen der Web-Ansicht, bis „Externe Inhalte laden“ gewählt wird (Tracking-Pixel).
- Links öffnen sich (nur nach Klick) im Standardbrowser, `mailto:`-Links im Verfassen-Fenster.
- Beim Öffnen wird eine Nachricht als gelesen markiert.

## Gespeicherte Daten

| Was | Ort |
|-----|-----|
| Kontenliste (ohne Geheimnisse) | `%APPDATA%\LuMail\accounts.json` |
| Absender-Regeln | `%APPDATA%\LuMail\rules.json` (`{ "absender@domain": "promotions" }`) |
| OAuth-Client-IDs | `%APPDATA%\LuMail\oauth.json` |
| Passwörter / Refresh-Tokens | `%APPDATA%\LuMail\secrets.bin` (DPAPI, nur aktueller Windows-Benutzer) |
| App-Einstellungen | `%APPDATA%\LuMail\settings.json` |
| WebView2-Daten | `%LOCALAPPDATA%\LuMail\WebView2` |
