// AUTOMATISCH ERZEUGT von scripts/sync-shared.py – nicht von Hand bearbeiten.
// Quelle: shared/

public enum SharedResources {
    /// shared/providers.json
    public static let providersJSON = ##"""
{
  "version": 1,
  "providers": [
    {
      "id": "gmail",
      "name": "Gmail",
      "color": "#EA4335",
      "domains": [
        "gmail.com",
        "googlemail.com"
      ],
      "imap": {
        "host": "imap.gmail.com",
        "port": 993,
        "security": "tls"
      },
      "smtp": {
        "host": "smtp.gmail.com",
        "port": 465,
        "security": "tls"
      },
      "authMethods": [
        "oauth2",
        "password"
      ],
      "oauth": {
        "authorizationEndpoint": "https://accounts.google.com/o/oauth2/v2/auth",
        "tokenEndpoint": "https://oauth2.googleapis.com/token",
        "scopes": [
          "https://mail.google.com/",
          "openid",
          "email"
        ],
        "redirectUriTemplate": "http://127.0.0.1:{port}/callback",
        "extraParams": {
          "access_type": "offline",
          "prompt": "consent"
        },
        "emailSource": "idToken",
        "appRegistrationUrl": "https://console.cloud.google.com/apis/credentials"
      },
      "serverCategories": "gmail",
      "passwordLabel": "App-Passwort",
      "passwordHelp": "Gmail erlaubt normale Passwörter nicht für E-Mail-Programme. Erstelle unter „App-Passwörter“ ein eigenes Passwort (2-Schritt-Bestätigung muss aktiv sein).",
      "helpUrl": "https://myaccount.google.com/apppasswords",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": false,
      "customServers": false
    },
    {
      "serverCategories": "none",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": false,
      "id": "outlook",
      "name": "Outlook",
      "color": "#0A64D8",
      "domains": [
        "outlook.com",
        "hotmail.com",
        "live.com",
        "msn.com",
        "outlook.de",
        "hotmail.de",
        "live.de"
      ],
      "imap": {
        "host": "outlook.office365.com",
        "port": 993,
        "security": "tls"
      },
      "smtp": {
        "host": "smtp-mail.outlook.com",
        "port": 587,
        "security": "starttls"
      },
      "authMethods": [
        "oauth2"
      ],
      "oauth": {
        "authorizationEndpoint": "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        "tokenEndpoint": "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        "scopes": [
          "https://outlook.office.com/IMAP.AccessAsUser.All",
          "https://outlook.office.com/SMTP.Send",
          "offline_access",
          "openid",
          "email"
        ],
        "redirectUriTemplate": "http://localhost:{port}/callback",
        "extraParams": {
          "prompt": "select_account"
        },
        "emailSource": "idToken",
        "appRegistrationUrl": "https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade"
      },
      "passwordLabel": "Passwort",
      "passwordHelp": "Microsoft erlaubt für Outlook.com nur noch die Anmeldung über die Microsoft-Seite (OAuth). Trage dafür eine eigene Client-ID in oauth.json ein – siehe docs/ANMELDUNG.md.",
      "helpUrl": "https://support.microsoft.com/de-de/office/pop-imap-und-smtp-einstellungen-f%C3%BCr-outlook-com-d088b986-291d-42b8-9564-9c414e2aa040",
      "customServers": false
    },
    {
      "serverCategories": "none",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": false,
      "id": "icloud",
      "name": "iCloud Mail",
      "color": "#3693F3",
      "domains": [
        "icloud.com",
        "me.com",
        "mac.com"
      ],
      "imap": {
        "host": "imap.mail.me.com",
        "port": 993,
        "security": "tls"
      },
      "smtp": {
        "host": "smtp.mail.me.com",
        "port": 587,
        "security": "starttls"
      },
      "authMethods": [
        "password"
      ],
      "passwordLabel": "App-spezifisches Passwort",
      "passwordHelp": "iCloud verlangt für E-Mail-Programme ein app-spezifisches Passwort. Erstelle es auf account.apple.com unter „Anmelden und Sicherheit → App-spezifische Passwörter“ (2-Faktor-Authentifizierung muss aktiv sein).",
      "helpUrl": "https://support.apple.com/de-de/102654",
      "customServers": false
    },
    {
      "id": "proton",
      "name": "Proton Mail",
      "color": "#6D4AFF",
      "domains": [
        "proton.me",
        "protonmail.com",
        "protonmail.ch",
        "pm.me"
      ],
      "imap": {
        "host": "127.0.0.1",
        "port": 1143,
        "security": "tls"
      },
      "smtp": {
        "host": "127.0.0.1",
        "port": 1025,
        "security": "tls"
      },
      "authMethods": [
        "password"
      ],
      "serverCategories": "none",
      "passwordLabel": "Bridge-Passwort",
      "passwordHelp": "Proton Mail ist Ende-zu-Ende-verschlüsselt und funktioniert in E-Mail-Programmen nur über die Proton Mail Bridge (kostenpflichtiger Tarif). Installiere die Bridge, melde dich dort an, stelle unter Einstellungen → Erweitert → Verbindungsmodus IMAP und SMTP auf „SSL“ und kopiere das angezeigte Bridge-Passwort.",
      "helpUrl": "https://proton.me/mail/bridge",
      "allowInsecureLoopbackCertificate": true,
      "saveSentViaImap": false,
      "customServers": false
    },
    {
      "id": "mailru",
      "name": "Mail.ru",
      "color": "#005FF9",
      "domains": [
        "mail.ru",
        "inbox.ru",
        "list.ru",
        "bk.ru",
        "internet.ru",
        "xmail.ru"
      ],
      "imap": {
        "host": "imap.mail.ru",
        "port": 993,
        "security": "tls"
      },
      "smtp": {
        "host": "smtp.mail.ru",
        "port": 465,
        "security": "tls"
      },
      "authMethods": [
        "oauth2",
        "password"
      ],
      "oauth": {
        "authorizationEndpoint": "https://o2.mail.ru/login",
        "tokenEndpoint": "https://o2.mail.ru/token",
        "userInfoEndpoint": "https://oauth.mail.ru/userinfo",
        "scopes": [
          "mail.imap",
          "userinfo"
        ],
        "redirectUriTemplate": "http://localhost:{port}/callback",
        "extraParams": {
          "prompt_force": "1"
        },
        "emailSource": "userInfo",
        "appRegistrationUrl": "https://oauth.mail.ru/app/"
      },
      "serverCategories": "none",
      "passwordLabel": "Passwort für externe Apps",
      "passwordHelp": "Mail.ru verlangt für E-Mail-Programme ein „Passwort für externe Anwendungen“. Du kannst es in den Sicherheitseinstellungen deines Mail.ru-Kontos erstellen.",
      "helpUrl": "https://account.mail.ru/user/2-step-auth/passwords/",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": false,
      "customServers": false
    },
    {
      "serverCategories": "none",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": false,
      "id": "yandex",
      "name": "Yandex Mail",
      "color": "#FC3F1D",
      "domains": [
        "yandex.ru",
        "yandex.com",
        "ya.ru",
        "yandex.by",
        "yandex.kz",
        "yandex.ua"
      ],
      "imap": {
        "host": "imap.yandex.com",
        "port": 993,
        "security": "tls"
      },
      "smtp": {
        "host": "smtp.yandex.com",
        "port": 465,
        "security": "tls"
      },
      "authMethods": [
        "password"
      ],
      "passwordLabel": "App-Passwort",
      "passwordHelp": "Yandex verlangt für E-Mail-Programme ein App-Passwort (Typ „Mail“). Erstelle es in deinem Yandex-ID-Konto unter Sicherheit → App-Passwörter und erlaube in den Mail-Einstellungen den Zugriff per IMAP.",
      "helpUrl": "https://id.yandex.com/security/app-passwords",
      "customServers": false
    },
    {
      "serverCategories": "none",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": false,
      "id": "gmx",
      "name": "GMX",
      "color": "#1C449B",
      "domains": [
        "gmx.de",
        "gmx.net",
        "gmx.at",
        "gmx.ch",
        "gmx.com"
      ],
      "imap": {
        "host": "imap.gmx.net",
        "port": 993,
        "security": "tls"
      },
      "smtp": {
        "host": "mail.gmx.net",
        "port": 465,
        "security": "tls"
      },
      "authMethods": [
        "password"
      ],
      "passwordLabel": "GMX-Passwort",
      "passwordHelp": "Aktiviere zuerst im GMX-Postfach unter Einstellungen → POP3/IMAP Abruf den Zugriff per IMAP. Danach meldest du dich mit E-Mail-Adresse und GMX-Passwort an (bei 2-Faktor-Schutz mit einem anwendungsspezifischen Passwort).",
      "helpUrl": "https://hilfe.gmx.net/pop-imap/imap/imap-serverdaten.html",
      "customServers": false
    },
    {
      "serverCategories": "none",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": false,
      "id": "webde",
      "name": "WEB.DE",
      "color": "#E5B800",
      "domains": [
        "web.de"
      ],
      "imap": {
        "host": "imap.web.de",
        "port": 993,
        "security": "tls"
      },
      "smtp": {
        "host": "smtp.web.de",
        "port": 587,
        "security": "starttls"
      },
      "authMethods": [
        "password"
      ],
      "passwordLabel": "WEB.DE-Passwort",
      "passwordHelp": "Aktiviere zuerst im WEB.DE-Postfach unter Einstellungen → POP3/IMAP Abruf den Zugriff per IMAP. Danach meldest du dich mit E-Mail-Adresse und WEB.DE-Passwort an (bei 2-Faktor-Schutz mit einem anwendungsspezifischen Passwort).",
      "helpUrl": "https://hilfe.web.de/pop-imap/imap/imap-serverdaten.html",
      "customServers": false
    },
    {
      "id": "hostinger",
      "name": "Hostinger Mail",
      "color": "#673DE6",
      "domains": [],
      "imap": {
        "host": "imap.hostinger.com",
        "port": 993,
        "security": "tls"
      },
      "smtp": {
        "host": "smtp.hostinger.com",
        "port": 465,
        "security": "tls"
      },
      "authMethods": [
        "password"
      ],
      "serverCategories": "none",
      "passwordLabel": "Postfach-Passwort",
      "passwordHelp": "Melde dich mit der vollständigen E-Mail-Adresse und dem Passwort deines Hostinger-Postfachs an. Vergessen? Im hPanel unter E-Mails → Konten kannst du es neu setzen.",
      "helpUrl": "https://hpanel.hostinger.com/emails",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": true,
      "customServers": false
    },
    {
      "serverCategories": "none",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": false,
      "id": "custom",
      "name": "Andere (IMAP)",
      "color": "#8B7CFF",
      "domains": [],
      "imap": {
        "host": "",
        "port": 993,
        "security": "tls"
      },
      "smtp": {
        "host": "",
        "port": 465,
        "security": "tls"
      },
      "authMethods": [
        "password"
      ],
      "passwordLabel": "Passwort",
      "passwordHelp": "Für jeden anderen Anbieter mit IMAP und SMTP. Die Serverdaten findest du in der Hilfe deines Anbieters.",
      "helpUrl": "https://github.com/Luna-OS/Lu-Mail/blob/main/docs/ANMELDUNG.md#andere-imap",
      "customServers": true
    }
  ],
  "folderRoles": {
    "inbox": {
      "specialUse": [],
      "names": [
        "INBOX"
      ]
    },
    "sent": {
      "specialUse": [
        "\\Sent"
      ],
      "names": [
        "Sent",
        "Sent Items",
        "Sent Messages",
        "INBOX.Sent",
        "Gesendet",
        "Gesendete Elemente",
        "Отправленные",
        "[Gmail]/Sent Mail",
        "[Gmail]/Gesendet"
      ]
    },
    "drafts": {
      "specialUse": [
        "\\Drafts"
      ],
      "names": [
        "Drafts",
        "INBOX.Drafts",
        "Entwürfe",
        "Черновики",
        "[Gmail]/Drafts",
        "[Gmail]/Entwürfe"
      ]
    },
    "spam": {
      "specialUse": [
        "\\Junk"
      ],
      "names": [
        "Spam",
        "Junk",
        "INBOX.Spam",
        "INBOX.Junk",
        "Спам",
        "[Gmail]/Spam"
      ]
    },
    "trash": {
      "specialUse": [
        "\\Trash"
      ],
      "names": [
        "Trash",
        "Deleted",
        "Deleted Items",
        "INBOX.Trash",
        "Papierkorb",
        "Корзина",
        "[Gmail]/Trash",
        "[Gmail]/Papierkorb"
      ]
    },
    "archive": {
      "specialUse": [
        "\\Archive",
        "\\All"
      ],
      "names": [
        "Archive",
        "INBOX.Archive",
        "Archiv",
        "Архив",
        "[Gmail]/All Mail",
        "[Gmail]/Alle Nachrichten"
      ]
    }
  }
}
"""##

    /// shared/categories.json
    public static let categoriesJSON = #"""
{
  "version": 1,
  "categories": [
    { "id": "primary", "name": "Posteingang", "icon": "tray", "gmailQuery": "category:primary" },
    { "id": "promotions", "name": "Werbung", "icon": "tag", "gmailQuery": "category:promotions" },
    { "id": "social", "name": "Soziale Netzwerke", "icon": "person.2", "gmailQuery": "category:social" },
    { "id": "updates", "name": "Updates", "icon": "bell", "gmailQuery": "category:updates" },
    { "id": "forums", "name": "Foren", "icon": "bubble.left.and.bubble.right", "gmailQuery": "category:forums" }
  ],
  "rules": {
    "social": {
      "senderDomains": [
        "facebook.com", "facebookmail.com", "instagram.com", "linkedin.com", "x.com", "twitter.com",
        "tiktok.com", "pinterest.com", "reddit.com", "redditmail.com", "snapchat.com", "discord.com",
        "telegram.org", "whatsapp.com", "threads.net", "youtube.com", "twitch.tv", "xing.com",
        "vk.com", "ok.ru", "my.mail.ru", "tumblr.com", "mastodon.social", "bsky.app", "quora.com"
      ]
    },
    "forums": {
      "senderDomains": ["googlegroups.com", "discoursemail.com", "groups.io", "lists.sourceforge.net"],
      "headers": ["List-Post"]
    },
    "updates": {
      "subjectKeywords": [
        "rechnung", "invoice", "receipt", "quittung", "beleg", "bestellung", "bestellbestätigung", "order",
        "versand", "versendet", "shipped", "shipping", "lieferung", "delivery", "zustellung", "sendungsverfolgung",
        "passwort", "password", "anmeldung", "sign-in", "login", "sicherheitswarnung", "security alert",
        "bestätigungscode", "verification code", "verify", "bestätige", "confirm", "kontoauszug", "statement",
        "zahlung", "payment", "abbuchung", "termin", "appointment", "buchung", "booking", "ticket",
        "счёт", "счет", "заказ", "доставка", "пароль", "код подтверждения", "платёж", "платеж"
      ],
      "senderLocalParts": [
        "noreply", "no-reply", "donotreply", "do-not-reply", "notification", "notifications", "notify",
        "alert", "alerts", "account", "accounts", "security", "billing", "invoice", "receipts", "orders",
        "order", "shipping", "service", "support", "info-noreply", "mailer-daemon", "postmaster"
      ]
    },
    "promotions": {
      "headers": ["List-Unsubscribe"],
      "precedence": ["bulk", "list", "junk"],
      "senderLocalParts": [
        "newsletter", "newsletters", "news", "marketing", "promo", "promotions", "offers", "angebote",
        "deals", "sale", "shop", "store", "hello", "hallo", "team", "mailing", "campaign", "email",
        "rassylka", "akcii"
      ],
      "subjectKeywords": [
        "rabatt", "% off", "sale", "angebot", "gutschein", "coupon", "gratis", "kostenlos", "newsletter",
        "nur heute", "black friday", "cyber monday", "deal", "sparen", "jetzt kaufen", "exklusiv",
        "скидк", "акци", "распродаж", "подарок"
      ]
    }
  },
  "algorithm": [
    "1. Nutzer-Regel (Absender-Adresse oder -Domain manuell einer Kategorie zugeordnet) gewinnt immer.",
    "2. Gmail: Kategorie kommt vom Server (X-GM-RAW category:...). Nur wenn der Server keine liefert, geht es lokal weiter.",
    "3. social: Absender-Domain ist (Sub-)Domain von rules.social.senderDomains.",
    "4. forums: Absender-Domain passt zu rules.forums.senderDomains ODER einer der rules.forums.headers ist vorhanden.",
    "5. updates: Betreff enthält eines der rules.updates.subjectKeywords.",
    "6. promotions: einer der rules.promotions.headers ist vorhanden ODER Header Precedence ist in rules.promotions.precedence ODER lokaler Teil der Absender-Adresse ist in rules.promotions.senderLocalParts ODER Betreff enthält eines der rules.promotions.subjectKeywords.",
    "7. updates: lokaler Teil der Absender-Adresse ist in rules.updates.senderLocalParts.",
    "8. Sonst: primary.",
    "Vergleiche sind immer klein geschrieben (case-insensitive). Domain-Treffer: domain == d ODER domain endet auf '.' + d. Stichwort-Treffer: Teilstring im kleingeschriebenen Betreff."
  ]
}
"""#

    /// shared/theme/lu-theme.json
    public static let themeJSON = ##"""
{
  "name": "Luna",
  "version": 1,
  "description": "Lu-Mail Design-Tokens. Gemeinsame Quelle für macOS (SwiftUI) und Windows (WinUI 3).",
  "colors": {
    "dark": {
      "background": "#0F1020",
      "sidebar": "#131430",
      "surface": "#1A1C3A",
      "surfaceHover": "#232547",
      "surfaceSelected": "#2D2A5C",
      "border": "#2A2C4F",
      "textPrimary": "#ECEBFF",
      "textSecondary": "#A6A3C9",
      "textMuted": "#6F6C99",
      "accent": "#8B7CFF",
      "accentHover": "#A094FF",
      "accentText": "#0F1020",
      "moon": "#F5D06F",
      "unreadDot": "#8B7CFF",
      "danger": "#FF6B81",
      "success": "#4ADE80"
    },
    "light": {
      "background": "#F7F6FF",
      "sidebar": "#EEECFB",
      "surface": "#FFFFFF",
      "surfaceHover": "#F1EFFE",
      "surfaceSelected": "#E3DFFD",
      "border": "#DDDAF2",
      "textPrimary": "#1B1A33",
      "textSecondary": "#5E5B80",
      "textMuted": "#9491B5",
      "accent": "#6B5BEF",
      "accentHover": "#5A49E0",
      "accentText": "#FFFFFF",
      "moon": "#C99A1E",
      "unreadDot": "#6B5BEF",
      "danger": "#E11D48",
      "success": "#16A34A"
    }
  },
  "providers": {
    "gmail": "#EA4335",
    "proton": "#6D4AFF",
    "mailru": "#005FF9",
    "hostinger": "#673DE6",
    "outlook": "#0A64D8",
    "icloud": "#3693F3",
    "gmx": "#1C449B",
    "webde": "#E5B800",
    "yandex": "#FC3F1D",
    "custom": "#8B7CFF"
  },
  "categories": {
    "primary": "#8B7CFF",
    "promotions": "#F59E0B",
    "social": "#3B82F6",
    "updates": "#10B981",
    "forums": "#A855F7"
  },
  "radius": {
    "small": 6,
    "medium": 10,
    "large": 16
  },
  "spacing": {
    "xs": 4,
    "s": 8,
    "m": 12,
    "l": 16,
    "xl": 24
  },
  "typography": {
    "fontMac": "SF Pro",
    "fontWindows": "Segoe UI Variable",
    "sizeCaption": 11,
    "sizeBody": 13,
    "sizeSubtitle": 15,
    "sizeTitle": 20
  },
  "variants": {
    "luna": {
      "name": "Luna",
      "description": "Nachtblau mit Lila – das Original",
      "colors": {
        "dark": {
          "background": "#0F1020",
          "sidebar": "#131430",
          "surface": "#1A1C3A",
          "surfaceHover": "#232547",
          "surfaceSelected": "#2D2A5C",
          "border": "#2A2C4F",
          "textPrimary": "#ECEBFF",
          "textSecondary": "#A6A3C9",
          "textMuted": "#6F6C99",
          "accent": "#8B7CFF",
          "accentHover": "#A094FF",
          "accentText": "#0F1020",
          "moon": "#F5D06F",
          "unreadDot": "#8B7CFF",
          "danger": "#FF6B81",
          "success": "#4ADE80"
        },
        "light": {
          "background": "#F7F6FF",
          "sidebar": "#EEECFB",
          "surface": "#FFFFFF",
          "surfaceHover": "#F1EFFE",
          "surfaceSelected": "#E3DFFD",
          "border": "#DDDAF2",
          "textPrimary": "#1B1A33",
          "textSecondary": "#5E5B80",
          "textMuted": "#9491B5",
          "accent": "#6B5BEF",
          "accentHover": "#5A49E0",
          "accentText": "#FFFFFF",
          "moon": "#C99A1E",
          "unreadDot": "#6B5BEF",
          "danger": "#E11D48",
          "success": "#16A34A"
        }
      }
    },
    "neumond": {
      "name": "Neumond",
      "description": "Tiefschwarz, ideal für OLED-Bildschirme",
      "colors": {
        "dark": {
          "background": "#000000",
          "sidebar": "#05050C",
          "surface": "#0B0B16",
          "surfaceHover": "#14142A",
          "surfaceSelected": "#1E1B42",
          "border": "#1C1C33",
          "textPrimary": "#ECEBFF",
          "textSecondary": "#A6A3C9",
          "textMuted": "#6F6C99",
          "accent": "#8B7CFF",
          "accentHover": "#A094FF",
          "accentText": "#0F1020",
          "moon": "#F5D06F",
          "unreadDot": "#8B7CFF",
          "danger": "#FF6B81",
          "success": "#4ADE80"
        },
        "light": {
          "background": "#F7F6FF",
          "sidebar": "#EEECFB",
          "surface": "#FFFFFF",
          "surfaceHover": "#F1EFFE",
          "surfaceSelected": "#E3DFFD",
          "border": "#DDDAF2",
          "textPrimary": "#1B1A33",
          "textSecondary": "#5E5B80",
          "textMuted": "#9491B5",
          "accent": "#6B5BEF",
          "accentHover": "#5A49E0",
          "accentText": "#FFFFFF",
          "moon": "#C99A1E",
          "unreadDot": "#6B5BEF",
          "danger": "#E11D48",
          "success": "#16A34A"
        }
      }
    },
    "vollmond": {
      "name": "Vollmond",
      "description": "Warm und hell wie Mondlicht",
      "colors": {
        "dark": {
          "background": "#17140F",
          "sidebar": "#1D1913",
          "surface": "#241F17",
          "surfaceHover": "#2E281E",
          "surfaceSelected": "#3A3122",
          "border": "#3A3324",
          "textPrimary": "#F5EEDC",
          "textSecondary": "#C2B79E",
          "textMuted": "#8A8069",
          "accent": "#8B7CFF",
          "accentHover": "#A094FF",
          "accentText": "#0F1020",
          "moon": "#F5D06F",
          "unreadDot": "#8B7CFF",
          "danger": "#FF6B81",
          "success": "#4ADE80"
        },
        "light": {
          "background": "#FBF8F1",
          "sidebar": "#F3EEE2",
          "surface": "#FFFDF8",
          "surfaceHover": "#F6F0E2",
          "surfaceSelected": "#EDE3CB",
          "border": "#E6DCC6",
          "textPrimary": "#2A2418",
          "textSecondary": "#6B604B",
          "textMuted": "#A39A86",
          "accent": "#6B5BEF",
          "accentHover": "#5A49E0",
          "accentText": "#FFFFFF",
          "moon": "#B8860B",
          "unreadDot": "#6B5BEF",
          "danger": "#E11D48",
          "success": "#16A34A"
        }
      }
    }
  },
  "accents": {
    "lila": {
      "name": "Lila",
      "dark": "#8B7CFF",
      "light": "#6B5BEF"
    },
    "blau": {
      "name": "Blau",
      "dark": "#60A5FA",
      "light": "#2563EB"
    },
    "tuerkis": {
      "name": "Türkis",
      "dark": "#2DD4BF",
      "light": "#0D9488"
    },
    "gruen": {
      "name": "Grün",
      "dark": "#4ADE80",
      "light": "#16A34A"
    },
    "rosa": {
      "name": "Rosa",
      "dark": "#F472B6",
      "light": "#DB2777"
    },
    "gold": {
      "name": "Gold",
      "dark": "#F5D06F",
      "light": "#B7791F"
    }
  }
}
"""##

    /// shared/login/login.html
    public static let loginHTML = #"""
<!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; img-src data:">
<meta name="referrer" content="no-referrer">
<title>Lu-Mail – Bei {{PROVIDER_NAME}} anmelden</title>
<style>
  :root {
    --bg: #F7F6FF; --surface: #FFFFFF; --border: #DDDAF2; --text: #1B1A33; --text2: #5E5B80;
    --accent: #6B5BEF; --accent-text: #FFFFFF; --danger: #E11D48; --provider: {{PROVIDER_COLOR}};
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg: #0F1020; --surface: #1A1C3A; --border: #2A2C4F; --text: #ECEBFF; --text2: #A6A3C9;
      --accent: #8B7CFF; --accent-text: #0F1020; --danger: #FF6B81;
    }
  }
  * { box-sizing: border-box; }
  body {
    margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px 16px;
    background: radial-gradient(ellipse at top, color-mix(in srgb, var(--accent) 18%, var(--bg)) 0%, var(--bg) 60%);
    color: var(--text); font: 15px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI Variable", "Segoe UI", system-ui, sans-serif;
  }
  .card {
    width: 100%; max-width: 420px; background: var(--surface); border: 1px solid var(--border);
    border-radius: 16px; padding: 28px; box-shadow: 0 20px 60px rgba(15, 16, 32, .18);
  }
  .brand { display: flex; align-items: center; gap: 10px; color: var(--text2); font-size: 13px; margin-bottom: 18px; }
  .moon { width: 22px; height: 22px; border-radius: 50%; background: var(--accent); box-shadow: inset -6px -2px 0 0 var(--surface); }
  h1 { font-size: 21px; margin: 0 0 6px; display: flex; align-items: center; gap: 10px; }
  .dot { width: 12px; height: 12px; border-radius: 50%; background: var(--provider); flex: none; }
  p.help { color: var(--text2); font-size: 13.5px; margin: 0 0 18px; }
  a { color: var(--accent); }
  label { display: block; font-size: 13px; font-weight: 600; margin: 14px 0 6px; }
  input {
    width: 100%; padding: 11px 12px; border-radius: 10px; border: 1px solid var(--border);
    background: var(--bg); color: var(--text); font: inherit;
  }
  input:focus { outline: 2px solid var(--accent); outline-offset: 1px; border-color: transparent; }
  button {
    width: 100%; margin-top: 22px; padding: 12px; border: 0; border-radius: 10px; cursor: pointer;
    background: var(--accent); color: var(--accent-text); font: inherit; font-weight: 600;
  }
  .error { background: color-mix(in srgb, var(--danger) 12%, transparent); color: var(--danger); border-radius: 10px; padding: 10px 12px; font-size: 13.5px; margin-bottom: 8px; }
  .error:empty { display: none; }
  .foot { margin-top: 18px; font-size: 12px; color: var(--text2); text-align: center; }
</style>
</head>
<body>
  <main class="card">
    <div class="brand"><span class="moon" aria-hidden="true"></span>Lu-Mail</div>
    <h1><span class="dot" aria-hidden="true"></span>Bei {{PROVIDER_NAME}} anmelden</h1>
    <p class="help">{{PASSWORD_HELP}} <a href="{{HELP_URL}}" target="_blank" rel="noopener noreferrer">Anleitung öffnen ↗</a></p>
    <div class="error" role="alert">{{ERROR}}</div>
    <form method="post" action="/login" autocomplete="on">
      <input type="hidden" name="state" value="{{STATE}}">
      <label for="email">E-Mail-Adresse</label>
      <input id="email" name="email" type="email" required autofocus autocomplete="username" value="{{EMAIL}}" placeholder="name@beispiel.de">
      <label for="password">{{PASSWORD_LABEL}}</label>
      <input id="password" name="password" type="password" required autocomplete="current-password">
{{EXTRA_FIELDS}}
      <button type="submit">Anmelden und zu Lu-Mail zurückkehren</button>
    </form>
    <p class="foot">Diese Seite läuft lokal auf deinem Rechner (127.0.0.1). Deine Zugangsdaten gehen direkt an Lu-Mail und werden im Schlüsselbund bzw. in der Windows-Anmeldeinformationsverwaltung gespeichert.</p>
  </main>
</body>
</html>
"""#

    /// shared/login/done.html
    public static let doneHTML = #"""
<!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
<title>Lu-Mail – {{TITLE}}</title>
<style>
  :root { --bg: #F7F6FF; --surface: #FFFFFF; --border: #DDDAF2; --text: #1B1A33; --text2: #5E5B80; --accent: #6B5BEF; }
  @media (prefers-color-scheme: dark) {
    :root { --bg: #0F1020; --surface: #1A1C3A; --border: #2A2C4F; --text: #ECEBFF; --text2: #A6A3C9; --accent: #8B7CFF; }
  }
  body {
    margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px 16px;
    background: var(--bg); color: var(--text);
    font: 15px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI Variable", "Segoe UI", system-ui, sans-serif;
  }
  .card { max-width: 420px; width: 100%; text-align: center; background: var(--surface); border: 1px solid var(--border); border-radius: 16px; padding: 32px 28px; }
  .moon { width: 44px; height: 44px; margin: 0 auto 16px; border-radius: 50%; background: var(--accent); box-shadow: inset -12px -4px 0 0 var(--surface); }
  h1 { font-size: 21px; margin: 0 0 8px; }
  p { color: var(--text2); margin: 0; }
</style>
</head>
<body>
  <main class="card">
    <div class="moon" aria-hidden="true"></div>
    <h1>{{TITLE}}</h1>
    <p>{{MESSAGE}}</p>
  </main>
</body>
</html>
"""#

    /// shared/oauth.example.json
    public static let oauthExampleJSON = #"""
{
  "_hinweis": "Kopiere diese Datei als oauth.json nach ~/Library/Application Support/LuMail/ (macOS) bzw. %APPDATA%\\LuMail\\ (Windows) und trage deine eigenen OAuth-Client-Daten ein. Ohne Eintrag nutzt Lu-Mail für diesen Anbieter die Browser-Anmeldung mit App-Passwort. Siehe docs/ANMELDUNG.md.",
  "gmail": {
    "clientId": "",
    "clientSecret": ""
  },
  "mailru": {
    "clientId": "",
    "clientSecret": ""
  }
}
"""#

    /// shared/update.json
    public static let updateJSON = #"""
{
  "version": 1,
  "repository": "Luna-OS/Lu-Mail",
  "latestReleaseApi": "https://api.github.com/repos/Luna-OS/Lu-Mail/releases/latest",
  "checkIntervalHours": 6,
  "initialDelaySeconds": 15,
  "assetSuffixes": {
    "macos": "-macOS.dmg",
    "windows-x64": "-Windows-x64-Setup.exe",
    "windows-arm64": "-Windows-arm64-Setup.exe"
  },
  "checksumsAsset": "SHA256SUMS.txt"
}
"""#

    /// shared/trackers.json
    public static let trackersJSON = #"""
{
  "version": 1,
  "description": "Bekannte Tracking-Dienste in E-Mails. Ein Bild gilt als Tracker, wenn sein Host diese Domain oder eine Subdomain davon ist – oder wenn es höchstens 1×1 Pixel groß ist.",
  "domains": [
    "list-manage.com", "mailchimp.com", "mcusercontent.com", "sendgrid.net", "sendgrid.com", "mailgun.org", "mailgun.net",
    "mandrillapp.com", "mcsv.net", "exacttarget.com", "exct.net", "sfmc-content.com", "pardot.com", "hubspot.com",
    "hubspotemail.net", "hs-analytics.net", "hsforms.com", "marketo.com", "mktoweb.com", "mktdns.com", "mkt.com",
    "constantcontact.com", "rs6.net", "campaign-archive.com", "klaviyo.com", "klaviyomail.com", "sparkpostmail.com",
    "mailjet.com", "mjt.lu", "sendinblue.com", "brevo.com", "sib-mail.com", "sendibt2.com", "cmail19.com", "cmail20.com",
    "createsend.com", "createsend1.com", "awstrack.me", "amazonses.com", "google-analytics.com", "doubleclick.net",
    "googleadservices.com", "facebook.com/tr", "bat.bing.com", "litmus.com", "emltrk.com", "mailtrack.io",
    "getnotify.com", "yesware.com", "bananatag.com", "streak.com", "mixmax.com", "superhuman.com", "intercom.io",
    "intercom-mail.com", "customer.io", "customeriomail.com", "iterable.com", "braze.com", "appboy.com",
    "salesforce.com", "eloqua.com", "en25.com", "returnpath.net", "movable-ink-7158.com", "movableink.com",
    "liveintent.com", "list-unsubscribe.com", "newsletter2go.com", "cleverreach.com", "rapidmail.com",
    "inxmail.com", "episerver.net", "optimizely.com", "emarsys.net", "sailthru.com", "ometria.com"
  ]
}
"""#

    /// shared/phishing.json
    public static let phishingJSON = #"""
{
  "version": 1,
  "description": "Marken, die oft für Phishing missbraucht werden. Nennt der Anzeigename eine Marke, muss die Absender-Domain zu einer der Domains (oder deren Subdomains) gehören.",
  "brands": [
    { "name": "PayPal", "keywords": ["paypal"], "domains": ["paypal.com", "paypal.de", "paypal-communication.com"] },
    { "name": "Amazon", "keywords": ["amazon"], "domains": ["amazon.com", "amazon.de", "amazon.co.uk", "amazonses.com", "marketplace.amazon.de"] },
    { "name": "Apple", "keywords": ["apple", "icloud", "itunes"], "domains": ["apple.com", "icloud.com", "me.com", "insideapple.apple.com"] },
    { "name": "Google", "keywords": ["google", "gmail"], "domains": ["google.com", "gmail.com", "accounts.google.com", "youtube.com"] },
    { "name": "Microsoft", "keywords": ["microsoft", "outlook", "office 365", "office365"], "domains": ["microsoft.com", "outlook.com", "office.com", "live.com", "microsoftonline.com"] },
    { "name": "Netflix", "keywords": ["netflix"], "domains": ["netflix.com", "mailer.netflix.com"] },
    { "name": "DHL", "keywords": ["dhl"], "domains": ["dhl.de", "dhl.com", "deutschepost.de"] },
    { "name": "Deutsche Post", "keywords": ["deutsche post"], "domains": ["deutschepost.de", "dhl.de"] },
    { "name": "Sparkasse", "keywords": ["sparkasse"], "domains": ["sparkasse.de", "s-kundenservice.de"] },
    { "name": "Volksbank", "keywords": ["volksbank", "raiffeisenbank"], "domains": ["vr.de", "volksbank.de", "atruvia.de"] },
    { "name": "Deutsche Bank", "keywords": ["deutsche bank"], "domains": ["db.com", "deutsche-bank.de"] },
    { "name": "Commerzbank", "keywords": ["commerzbank"], "domains": ["commerzbank.de", "commerzbank.com"] },
    { "name": "ING", "keywords": ["ing-diba", "ing diba"], "domains": ["ing.de", "ing.com"] },
    { "name": "N26", "keywords": ["n26"], "domains": ["n26.com"] },
    { "name": "Telekom", "keywords": ["telekom", "t-online"], "domains": ["telekom.de", "t-online.de", "telekom.com"] },
    { "name": "Vodafone", "keywords": ["vodafone"], "domains": ["vodafone.de", "vodafone.com"] },
    { "name": "eBay", "keywords": ["ebay"], "domains": ["ebay.com", "ebay.de", "ebay-kleinanzeigen.de"] },
    { "name": "Facebook", "keywords": ["facebook", "meta"], "domains": ["facebook.com", "facebookmail.com", "meta.com"] },
    { "name": "Instagram", "keywords": ["instagram"], "domains": ["instagram.com", "mail.instagram.com"] },
    { "name": "WhatsApp", "keywords": ["whatsapp"], "domains": ["whatsapp.com"] },
    { "name": "Sberbank", "keywords": ["сбербанк", "sberbank"], "domains": ["sberbank.ru", "sber.ru"] },
    { "name": "Госуслуги", "keywords": ["госуслуги", "gosuslugi"], "domains": ["gosuslugi.ru"] },
    { "name": "Proton", "keywords": ["proton"], "domains": ["proton.me", "protonmail.com"] },
    { "name": "Hostinger", "keywords": ["hostinger"], "domains": ["hostinger.com", "hostinger.de"] },
    { "name": "Mail.ru", "keywords": ["mail.ru"], "domains": ["mail.ru", "corp.mail.ru"] },
    { "name": "Finanzamt / ELSTER", "keywords": ["finanzamt", "elster"], "domains": ["elster.de", "finanzamt.de"] }
  ]
}
"""#

    /// shared/login/custom-servers.html
    public static let customServersHTML = #"""
      <fieldset style="border:1px solid var(--border);border-radius:10px;padding:4px 14px 14px;margin-top:16px">
        <legend style="font-size:13px;font-weight:600;padding:0 6px">Server</legend>
        <label for="imapHost">IMAP-Server</label>
        <input id="imapHost" name="imapHost" required value="{{IMAP_HOST}}" placeholder="imap.beispiel.de">
        <div style="display:flex;gap:10px">
          <div style="flex:1"><label for="imapPort">Port</label><input id="imapPort" name="imapPort" type="number" min="1" max="65535" required value="{{IMAP_PORT}}"></div>
          <div style="flex:1"><label for="imapSecurity">Verschlüsselung</label>
            <select id="imapSecurity" name="imapSecurity" style="width:100%;padding:11px 12px;border-radius:10px;border:1px solid var(--border);background:var(--bg);color:var(--text);font:inherit">
              <option value="tls" {{IMAP_TLS_SELECTED}}>SSL/TLS</option><option value="starttls" {{IMAP_STARTTLS_SELECTED}}>STARTTLS</option>
            </select></div>
        </div>
        <label for="smtpHost">SMTP-Server</label>
        <input id="smtpHost" name="smtpHost" required value="{{SMTP_HOST}}" placeholder="smtp.beispiel.de">
        <div style="display:flex;gap:10px">
          <div style="flex:1"><label for="smtpPort">Port</label><input id="smtpPort" name="smtpPort" type="number" min="1" max="65535" required value="{{SMTP_PORT}}"></div>
          <div style="flex:1"><label for="smtpSecurity">Verschlüsselung</label>
            <select id="smtpSecurity" name="smtpSecurity" style="width:100%;padding:11px 12px;border-radius:10px;border:1px solid var(--border);background:var(--bg);color:var(--text);font:inherit">
              <option value="tls" {{SMTP_TLS_SELECTED}}>SSL/TLS</option><option value="starttls" {{SMTP_STARTTLS_SELECTED}}>STARTTLS</option>
            </select></div>
        </div>
      </fieldset>
"""#
}
