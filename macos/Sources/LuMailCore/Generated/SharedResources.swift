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
        "web",
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
      "customServers": false,
      "mxSuffixes": [
        "google.com",
        "googlemail.com"
      ],
      "setup": {
        "passwordSteps": [
          {
            "text": "2-Schritt-Bestätigung in deinem Google-Konto aktivieren (falls noch nicht geschehen).",
            "url": "https://myaccount.google.com/signinoptions/twosv",
            "button": "Öffnen"
          },
          {
            "text": "Ein App-Passwort für „Lu-Mail“ erstellen und kopieren.",
            "url": "https://myaccount.google.com/apppasswords",
            "button": "App-Passwort erstellen"
          },
          {
            "text": "Auf „Im Browser anmelden“ klicken und das App-Passwort einfügen."
          }
        ],
        "oauthSteps": [
          {
            "text": "Auf „Mit Google anmelden“ klicken und im Browser dein Google-Konto wählen."
          },
          {
            "text": "Lu-Mail den Zugriff erlauben – fertig."
          }
        ]
      },
      "web": {
        "loginUrl": "https://accounts.google.com/ServiceLogin?service=mail&continue=https%3A%2F%2Fmail.google.com%2Fmail%2Fu%2F0%2F",
        "homeUrl": "https://mail.google.com/mail/u/0/#inbox",
        "reader": "gmail",
        "loggedInPrefixes": [
          "https://mail.google.com/mail/"
        ],
        "folders": {
          "primary": "https://mail.google.com/mail/u/0/#inbox",
          "promotions": "https://mail.google.com/mail/u/0/#category/promotions",
          "social": "https://mail.google.com/mail/u/0/#category/social",
          "updates": "https://mail.google.com/mail/u/0/#category/updates",
          "forums": "https://mail.google.com/mail/u/0/#category/forums",
          "sent": "https://mail.google.com/mail/u/0/#sent",
          "drafts": "https://mail.google.com/mail/u/0/#drafts",
          "spam": "https://mail.google.com/mail/u/0/#spam",
          "trash": "https://mail.google.com/mail/u/0/#trash",
          "archive": "https://mail.google.com/mail/u/0/#all"
        },
        "composeUrl": "https://mail.google.com/mail/u/0/#inbox?compose=new",
        "unreadTitlePattern": "\\((\\d+)\\)"
      }
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
        "web",
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
      "passwordHelp": "Microsoft erlaubt für Outlook.com nur noch die Anmeldung über die Microsoft-Seite (OAuth). Dafür muss Lu-Mail einmalig eine Microsoft-Client-ID bekommen – siehe docs/OAUTH-EINRICHTEN.md.",
      "helpUrl": "https://support.microsoft.com/de-de/office/pop-imap-und-smtp-einstellungen-f%C3%BCr-outlook-com-d088b986-291d-42b8-9564-9c414e2aa040",
      "customServers": false,
      "mxSuffixes": [
        "protection.outlook.com",
        "outlook.com",
        "hotmail.com"
      ],
      "setup": {
        "passwordSteps": [
          {
            "text": "Auf „Mit Microsoft anmelden“ klicken und im Browser zustimmen."
          }
        ],
        "oauthSteps": [
          {
            "text": "Auf „Mit Microsoft anmelden“ klicken und im Browser dein Microsoft-Konto wählen."
          },
          {
            "text": "Lu-Mail den Zugriff erlauben – fertig."
          }
        ]
      },
      "web": {
        "loginUrl": "https://outlook.live.com/owa/?nlp=1",
        "homeUrl": "https://outlook.live.com/mail/0/inbox",
        "loggedInPrefixes": [
          "https://outlook.live.com/mail/"
        ],
        "folders": {
          "primary": "https://outlook.live.com/mail/0/inbox",
          "sent": "https://outlook.live.com/mail/0/sentitems",
          "drafts": "https://outlook.live.com/mail/0/drafts",
          "spam": "https://outlook.live.com/mail/0/junkemail",
          "trash": "https://outlook.live.com/mail/0/deleteditems",
          "archive": "https://outlook.live.com/mail/0/archive"
        },
        "composeUrl": "https://outlook.live.com/mail/0/deeplink/compose",
        "unreadTitlePattern": "\\((\\d+)\\)"
      }
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
        "web",
        "password"
      ],
      "passwordLabel": "App-spezifisches Passwort",
      "passwordHelp": "iCloud verlangt für E-Mail-Programme ein app-spezifisches Passwort. Erstelle es auf account.apple.com unter „Anmelden und Sicherheit → App-spezifische Passwörter“ (2-Faktor-Authentifizierung muss aktiv sein).",
      "helpUrl": "https://support.apple.com/de-de/102654",
      "customServers": false,
      "mxSuffixes": [
        "icloud.com",
        "mail.me.com"
      ],
      "setup": {
        "passwordSteps": [
          {
            "text": "Auf account.apple.com unter „Anmelden und Sicherheit“ ein app-spezifisches Passwort erstellen.",
            "url": "https://account.apple.com/account/manage",
            "button": "Apple-Account öffnen"
          },
          {
            "text": "Auf „Im Browser anmelden“ klicken und das Passwort einfügen."
          }
        ]
      },
      "web": {
        "loginUrl": "https://www.icloud.com/mail/",
        "homeUrl": "https://www.icloud.com/mail/",
        "loggedInPrefixes": [],
        "folders": {
          "primary": "https://www.icloud.com/mail/"
        },
        "unreadTitlePattern": "\\((\\d+)\\)"
      }
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
        "web",
        "password"
      ],
      "serverCategories": "none",
      "passwordLabel": "Bridge-Passwort",
      "passwordHelp": "Proton Mail ist Ende-zu-Ende-verschlüsselt und funktioniert in E-Mail-Programmen nur über die Proton Mail Bridge (kostenpflichtiger Tarif). Installiere die Bridge, melde dich dort an, stelle unter Einstellungen → Erweitert → Verbindungsmodus IMAP und SMTP auf „SSL“ und kopiere das angezeigte Bridge-Passwort.",
      "helpUrl": "https://proton.me/mail/bridge",
      "allowInsecureLoopbackCertificate": true,
      "saveSentViaImap": false,
      "customServers": false,
      "mxSuffixes": [
        "protonmail.ch",
        "proton.me"
      ],
      "setup": {
        "passwordSteps": [
          {
            "text": "Proton Mail Bridge installieren und mit deinem Proton-Konto anmelden (ab Tarif „Mail Plus“).",
            "url": "https://proton.me/mail/bridge",
            "button": "Bridge herunterladen"
          },
          {
            "text": "In der Bridge unter Einstellungen → Erweitert → Verbindungsmodus IMAP und SMTP auf „SSL“ stellen."
          },
          {
            "text": "In der Bridge das Bridge-Passwort kopieren und auf „Im Browser anmelden“ klicken."
          }
        ],
        "checkLocalPort": 1143
      },
      "web": {
        "loginUrl": "https://account.proton.me/mail",
        "homeUrl": "https://mail.proton.me/u/0/inbox",
        "reader": "proton",
        "loggedInPrefixes": [
          "https://mail.proton.me/"
        ],
        "folders": {
          "primary": "https://mail.proton.me/u/0/inbox",
          "sent": "https://mail.proton.me/u/0/sent",
          "drafts": "https://mail.proton.me/u/0/drafts",
          "spam": "https://mail.proton.me/u/0/spam",
          "trash": "https://mail.proton.me/u/0/trash",
          "archive": "https://mail.proton.me/u/0/archive"
        },
        "unreadTitlePattern": "\\((\\d+)\\)"
      }
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
        "web",
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
      "customServers": false,
      "mxSuffixes": [
        "mail.ru"
      ],
      "setup": {
        "passwordSteps": [
          {
            "text": "In den Mail.ru-Sicherheitseinstellungen ein „Passwort für externe Anwendungen“ erstellen.",
            "url": "https://account.mail.ru/user/2-step-auth/passwords/",
            "button": "Mail.ru öffnen"
          },
          {
            "text": "Auf „Im Browser anmelden“ klicken und das Passwort einfügen."
          }
        ],
        "oauthSteps": [
          {
            "text": "Auf „Mit Mail.ru anmelden“ klicken und im Browser zustimmen."
          }
        ]
      },
      "web": {
        "loginUrl": "https://account.mail.ru/login?page=https%3A%2F%2Fe.mail.ru%2Finbox%2F",
        "homeUrl": "https://e.mail.ru/inbox/",
        "reader": "mailru",
        "loggedInPrefixes": [
          "https://e.mail.ru/"
        ],
        "folders": {
          "primary": "https://e.mail.ru/inbox/",
          "sent": "https://e.mail.ru/sent/",
          "drafts": "https://e.mail.ru/drafts/",
          "spam": "https://e.mail.ru/spam/",
          "trash": "https://e.mail.ru/trash/"
        },
        "composeUrl": "https://e.mail.ru/compose/",
        "unreadTitlePattern": "\\((\\d+)\\)"
      }
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
        "web",
        "password"
      ],
      "passwordLabel": "App-Passwort",
      "passwordHelp": "Yandex verlangt für E-Mail-Programme ein App-Passwort (Typ „Mail“). Erstelle es in deinem Yandex-ID-Konto unter Sicherheit → App-Passwörter und erlaube in den Mail-Einstellungen den Zugriff per IMAP.",
      "helpUrl": "https://id.yandex.com/security/app-passwords",
      "customServers": false,
      "mxSuffixes": [
        "yandex.net",
        "yandex.ru"
      ],
      "setup": {
        "passwordSteps": [
          {
            "text": "Im Yandex-ID-Konto ein App-Passwort vom Typ „Mail“ erstellen.",
            "url": "https://id.yandex.com/security/app-passwords",
            "button": "Yandex öffnen"
          },
          {
            "text": "In den Mail-Einstellungen den Zugriff per IMAP erlauben.",
            "url": "https://mail.yandex.com/#setup/client",
            "button": "Einstellungen öffnen"
          },
          {
            "text": "Auf „Im Browser anmelden“ klicken."
          }
        ]
      },
      "web": {
        "loginUrl": "https://passport.yandex.com/auth?retpath=https%3A%2F%2Fmail.yandex.com%2F",
        "homeUrl": "https://mail.yandex.com/#inbox",
        "loggedInPrefixes": [
          "https://mail.yandex.com/",
          "https://mail.yandex.ru/"
        ],
        "folders": {
          "primary": "https://mail.yandex.com/#inbox",
          "sent": "https://mail.yandex.com/#sent",
          "drafts": "https://mail.yandex.com/#draft",
          "spam": "https://mail.yandex.com/#spam",
          "trash": "https://mail.yandex.com/#trash"
        },
        "composeUrl": "https://mail.yandex.com/#compose",
        "unreadTitlePattern": "\\((\\d+)\\)"
      }
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
        "web",
        "password"
      ],
      "passwordLabel": "GMX-Passwort",
      "passwordHelp": "Aktiviere zuerst im GMX-Postfach unter Einstellungen → POP3/IMAP Abruf den Zugriff per IMAP. Danach meldest du dich mit E-Mail-Adresse und GMX-Passwort an (bei 2-Faktor-Schutz mit einem anwendungsspezifischen Passwort).",
      "helpUrl": "https://hilfe.gmx.net/pop-imap/imap/imap-serverdaten.html",
      "customServers": false,
      "mxSuffixes": [
        "gmx.net",
        "gmx.de"
      ],
      "setup": {
        "passwordSteps": [
          {
            "text": "Im GMX-Postfach unter Einstellungen → POP3/IMAP Abruf den IMAP-Zugriff aktivieren.",
            "url": "https://www.gmx.net/",
            "button": "GMX öffnen"
          },
          {
            "text": "Auf „Im Browser anmelden“ klicken und dein GMX-Passwort eingeben."
          }
        ]
      },
      "web": {
        "loginUrl": "https://www.gmx.net/",
        "homeUrl": "https://www.gmx.net/",
        "loggedInPrefixes": [
          "https://navigator-bs.gmx.net/",
          "https://navigator.gmx.net/"
        ],
        "folders": {
          "primary": "https://www.gmx.net/"
        },
        "unreadTitlePattern": "\\((\\d+)\\)"
      }
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
        "web",
        "password"
      ],
      "passwordLabel": "WEB.DE-Passwort",
      "passwordHelp": "Aktiviere zuerst im WEB.DE-Postfach unter Einstellungen → POP3/IMAP Abruf den Zugriff per IMAP. Danach meldest du dich mit E-Mail-Adresse und WEB.DE-Passwort an (bei 2-Faktor-Schutz mit einem anwendungsspezifischen Passwort).",
      "helpUrl": "https://hilfe.web.de/pop-imap/imap/imap-serverdaten.html",
      "customServers": false,
      "mxSuffixes": [
        "web.de"
      ],
      "setup": {
        "passwordSteps": [
          {
            "text": "Im WEB.DE-Postfach unter Einstellungen → POP3/IMAP Abruf den IMAP-Zugriff aktivieren.",
            "url": "https://web.de/",
            "button": "WEB.DE öffnen"
          },
          {
            "text": "Auf „Im Browser anmelden“ klicken und dein WEB.DE-Passwort eingeben."
          }
        ]
      },
      "web": {
        "loginUrl": "https://web.de/",
        "homeUrl": "https://web.de/",
        "loggedInPrefixes": [
          "https://navigator.web.de/"
        ],
        "folders": {
          "primary": "https://web.de/"
        },
        "unreadTitlePattern": "\\((\\d+)\\)"
      }
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
        "password",
        "web"
      ],
      "serverCategories": "none",
      "passwordLabel": "Postfach-Passwort",
      "passwordHelp": "Melde dich mit der vollständigen E-Mail-Adresse und dem Passwort deines Hostinger-Postfachs an. Vergessen? Im hPanel unter E-Mails → Konten kannst du es neu setzen.",
      "helpUrl": "https://hpanel.hostinger.com/emails",
      "allowInsecureLoopbackCertificate": false,
      "saveSentViaImap": true,
      "customServers": false,
      "mxSuffixes": [
        "hostinger.com",
        "hostinger.io",
        "titan.email"
      ],
      "setup": {
        "passwordSteps": [
          {
            "text": "E-Mail-Adresse und Postfach-Passwort bereithalten (vergessen? im hPanel neu setzen).",
            "url": "https://hpanel.hostinger.com/emails",
            "button": "hPanel öffnen"
          },
          {
            "text": "Auf „Im Browser anmelden“ klicken und dein normales Hostinger-Postfach-Passwort eingeben – kein App-Passwort nötig. Danach zeigt Lu-Mail dein Postfach direkt an (mit Senden, Suche und Kategorien)."
          }
        ]
      },
      "web": {
        "loginUrl": "https://mail.hostinger.com/",
        "homeUrl": "https://mail.hostinger.com/",
        "loggedInPrefixes": [],
        "folders": {
          "primary": "https://mail.hostinger.com/"
        },
        "unreadTitlePattern": "\\((\\d+)\\)"
      }
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
      "customServers": true,
      "mxSuffixes": [],
      "setup": {
        "passwordSteps": [
          {
            "text": "Lu-Mail sucht die Servereinstellungen automatisch. Auf der Anmeldeseite kannst du sie prüfen und ändern."
          },
          {
            "text": "Auf „Im Browser anmelden“ klicken und dein Passwort eingeben."
          }
        ]
      }
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
  },
  "discovery": {
    "mxLookup": "https://cloudflare-dns.com/dns-query?name={domain}&type=MX",
    "autoconfig": [
      "https://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={email}",
      "https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml?emailaddress={email}",
      "https://autoconfig.thunderbird.net/v1.1/{domain}"
    ],
    "timeoutSeconds": 6
  },
  "webUserAgent": {
    "macos": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15"
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

    /// shared/webmail/lu-webmail.js
    public static let webmailJS = ##"""
// Lu-Mail Web-Leser (docs/FUNKTIONEN.md §21)
//
// Läuft im unsichtbaren Browser eines Web-Kontos (macOS: eigene Content-World, Windows: WebView2) und liest
// Nachrichtenliste und geöffnete Nachricht aus dem Webmail des Anbieters, damit Lu-Mail sie in der eigenen
// Oberfläche zeigen kann. Jeder Aufruf liefert einen JSON-String:
//
//   LuWebmail.list("gmail")        → {"state":"ok"|"loading"|"login"|"error", "items":[{id, from, fromEmail, subject,
//                                      snippet, date, dateTitle, unread, attachment}]}
//                                      ("error" mit reason "unreadable": die Seite hat sich geändert)
//   LuWebmail.open("gmail", id)    → {"state":"ok"|"missing"}   (öffnet die Nachricht im Webmail)
//   LuWebmail.message("gmail", id) → {"state":"ok"|"loading", "subject", "parts":[{from, fromEmail, to, date, html}]}
//                                      (mit id: "loading", solange noch eine andere Nachricht angezeigt wird)
//
// Webseiten ändern sich: Jeder Anbieter hat mehrere Selektoren, zuerst stabile Attribute (role, data-*, email),
// danach Klassennamen. Erkennt der Leser nichts, schaltet Lu-Mail auf die Webseiten-Ansicht um.
(function () {
  "use strict";
  if (window.LuWebmail) return;

  // MARK: Hilfen

  function q(root, selector) {
    try { return root ? root.querySelector(selector) : null; } catch (e) { return null; }
  }

  function qa(root, selector) {
    try { return root ? Array.prototype.slice.call(root.querySelectorAll(selector)) : []; } catch (e) { return []; }
  }

  function first(root, selectors) {
    for (var i = 0; i < selectors.length; i++) {
      var found = q(root, selectors[i]);
      if (found) return found;
    }
    return null;
  }

  function text(el) {
    if (!el) return "";
    return String(el.innerText || el.textContent || "").replace(/\s+/g, " ").trim();
  }

  function attr(el, name) {
    return el && el.getAttribute ? (el.getAttribute(name) || "") : "";
  }

  function emailIn(value) {
    var match = String(value || "").match(/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/);
    return match ? match[0] : "";
  }

  function isBold(el) {
    if (!el) return false;
    try {
      var weight = window.getComputedStyle(el).fontWeight;
      return weight === "bold" || parseInt(weight, 10) >= 600;
    } catch (e) {
      return false;
    }
  }

  function visible(el) {
    return !!(el && (el.offsetWidth || el.offsetHeight || (el.getClientRects && el.getClientRects().length)));
  }

  function hasPasswordField() {
    return qa(document, 'input[type="password"]').some(visible);
  }

  // Echte Maus-Ereignisse – manche Webmails reagieren nicht auf ein bloßes click().
  function press(el) {
    if (!el) return false;
    ["mousedown", "mouseup", "click"].forEach(function (type) {
      el.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, view: window, button: 0 }));
    });
    return true;
  }

  // HTML ohne Skripte, Ereignis-Attribute und Formulare (Lu-Mail zeigt es ohnehin ohne JavaScript an).
  function cleanHTML(el) {
    if (!el) return "";
    var copy = el.cloneNode(true);
    qa(copy, "script, noscript, form, button, input, textarea, select").forEach(function (node) {
      node.parentNode && node.parentNode.removeChild(node);
    });
    qa(copy, "*").forEach(function (node) {
      Array.prototype.slice.call(node.attributes || []).forEach(function (a) {
        if (/^on/i.test(a.name)) node.removeAttribute(a.name);
      });
    });
    return copy.innerHTML;
  }

  // Inhalt eines (gleich-originigen) iframes, z. B. bei Proton.
  function frameHTML(frame) {
    try {
      var doc = frame && (frame.contentDocument || (frame.contentWindow && frame.contentWindow.document));
      return doc && doc.body ? cleanHTML(doc.body) : "";
    } catch (e) {
      return "";
    }
  }

  function largestTextBlock(root) {
    var best = null;
    var bestLength = 0;
    qa(root, "div").forEach(function (div) {
      var length = text(div).length;
      if (length > bestLength && qa(div, "div").length < 400) {
        best = div;
        bestLength = length;
      }
    });
    return best;
  }

  function result(object) {
    return JSON.stringify(object);
  }

  // SPA-Navigation ohne Neuladen: Adresse setzen und dem Router (History-API) Bescheid geben.
  function routeTo(path) {
    try {
      history.pushState(history.state, "", path);
      window.dispatchEvent(new PopStateEvent("popstate", { state: history.state }));
      return true;
    } catch (e) {
      return false;
    }
  }

  // Zeilen des sichtbaren Textes – Notlösung, wenn die Selektoren für Absender/Betreff nichts finden.
  function lines(el) {
    return String((el && el.innerText) || "").split(/\n+/).map(function (l) { return l.trim(); }).filter(function (l) {
      return l.length > 0 && !/^(\d{1,2}[:.]\d{2}|\d{1,2}\.\s?\w+\.?|\d+)$/.test(l);
    });
  }

  function fillFromLines(item, row) {
    if (item.from && item.subject) return item;
    var parts = lines(row);
    if (!item.from && parts.length) item.from = parts[0];
    if (!item.subject && parts.length > 1) item.subject = parts[1];
    if (!item.snippet && parts.length > 2) item.snippet = parts[2];
    return item;
  }

  // Elemente mit gleichem Attribut können verschachtelt sein – nur das äußerste zählt.
  function outermost(elements, name) {
    var seen = {};
    return elements.filter(function (el) {
      var id = attr(el, name);
      if (!id || seen[id]) return false;
      for (var p = el.parentElement; p; p = p.parentElement) {
        if (attr(p, name) === id) return false;
      }
      seen[id] = true;
      return true;
    });
  }

  // Mitschnitt aus lu-capture.js (Metadaten der Webmail-API).
  function captured() {
    try {
      return JSON.parse(document.documentElement.getAttribute("data-lu-capture") || "{}");
    } catch (e) {
      return {};
    }
  }

  function shortDate(seconds) {
    if (!seconds) return "";
    var date = new Date(seconds * 1000);
    var now = new Date();
    if (date.toDateString() === now.toDateString()) {
      return date.toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });
    }
    var options = { day: "numeric", month: "short" };
    if (date.getFullYear() !== now.getFullYear()) options.year = "numeric";
    return date.toLocaleDateString("de-DE", options);
  }

  function longDate(seconds) {
    return seconds ? new Date(seconds * 1000).toLocaleString("de-DE", { dateStyle: "full", timeStyle: "short" }) : "";
  }

  // MARK: Anbieter

  var adapters = {};

  // Gmail: Zeilen tragen data-thread-id / data-legacy-thread-id, Absender ein email-Attribut.
  adapters.gmail = {
    rows: function () {
      var main = q(document, '[role="main"]');
      if (!main) return null;
      return qa(main, "tr").filter(function (row) {
        return q(row, "[data-legacy-thread-id], [data-thread-id]") && visible(row);
      });
    },
    rowId: function (row) {
      var marker = q(row, "[data-legacy-thread-id], [data-thread-id]");
      return attr(marker, "data-legacy-thread-id") || attr(marker, "data-thread-id").replace(/^#?thread-f:/, "");
    },
    list: function () {
      if (hasPasswordField()) return { state: "login" };
      var rows = this.rows();
      if (!rows) return { state: "loading" };
      var self = this;
      var items = rows.map(function (row) {
        var marker = q(row, "[data-legacy-thread-id], [data-thread-id]");
        var senders = qa(row, "[email]");
        var sender = senders[0];
        var names = senders.map(function (s) { return attr(s, "name") || text(s); }).filter(Boolean);
        var dates = qa(row, "td span[title], td [title][aria-label]").filter(function (s) { return /\d/.test(attr(s, "title")); });
        var date = dates[dates.length - 1];
        var snippet = first(row, [".y2", '[class*="snippet"]']);
        return {
          id: self.rowId(row),
          from: names.slice(0, 3).join(", ") || text(sender),
          fromEmail: attr(sender, "email"),
          subject: text(marker),
          snippet: text(snippet).replace(/^[-–\s]+/, ""),
          date: text(date),
          dateTitle: attr(date, "title"),
          unread: row.classList.contains("zE") || isBold(marker) || isBold(sender),
          attachment: !!q(row, '[data-tooltip*="ttach"], img[src*="attachment"], .brd, .aKS'),
        };
      }).map(function (item, index) { return fillFromLines(item, rows[index]); }).filter(function (item) { return item.id; });
      return { state: "ok", items: items };
    },
    // Auch versteckte Zeilen anklicken: ist eine Unterhaltung offen, blendet Gmail die Liste nur aus.
    // Fehlt die Zeile ganz, direkt über die Adresse öffnen (#<ordner>/<thread-id>).
    open: function (id) {
      var markers = qa(document, "[data-legacy-thread-id], [data-thread-id]");
      for (var i = 0; i < markers.length; i++) {
        var marker = markers[i];
        if (marker.closest("h2")) continue;
        var markerId = attr(marker, "data-legacy-thread-id") || attr(marker, "data-thread-id").replace(/^#?thread-f:/, "");
        if (markerId === id) return press(marker);
      }
      var parts = location.hash.replace(/^#/, "").split("/");
      var base = parts[0] === "label" ? parts.slice(0, 2).join("/") : (parts[0] === "category" || !parts[0] ? "inbox" : parts[0]);
      location.hash = "#" + base + "/" + id;
      return true;
    },
    // Zeigt Gmail gerade die Unterhaltung mit dieser (alten) Thread-ID? Die Liste bleibt versteckt im DOM, deshalb nur
    // Betreff-Überschrift und Nachrichten prüfen. Ohne Kennungen: annehmen (der Betreffvergleich der App greift dann).
    shows: function (main, id) {
      var heading = q(main, "h2[data-legacy-thread-id]");
      if (heading) return attr(heading, "data-legacy-thread-id") === id;
      var ids = qa(main, "[data-legacy-message-id]").map(function (el) { return attr(el, "data-legacy-message-id"); });
      return !ids.length || ids.indexOf(id) >= 0;
    },
    message: function (expected) {
      var main = q(document, '[role="main"]');
      if (!main) return { state: "loading" };
      if (expected && !this.shows(main, expected)) return { state: "loading" };
      var subject = first(main, ["h2[data-thread-perm-id]", "h2.hP", "h2"]);
      var containers = qa(main, "[data-message-id], [data-legacy-message-id]");
      var parts = containers.map(function (container) {
        var body = first(container, [".a3s", '[data-message-id] [dir="ltr"]']);
        if (!body) return null;
        var people = qa(container, "[email]");
        var sender = people[0];
        var date = qa(container, "span[title]").filter(function (s) { return /\d/.test(attr(s, "title")); })[0];
        return {
          from: attr(sender, "name") || text(sender),
          fromEmail: attr(sender, "email"),
          to: people.slice(1).map(function (p) { return attr(p, "name") || attr(p, "email"); }).filter(Boolean).join(", "),
          date: attr(date, "title") || text(date),
          html: cleanHTML(body),
        };
      }).filter(Boolean);
      if (!parts.length) return { state: "loading" };
      return { state: "ok", subject: text(subject), parts: parts };
    },
  };

  // Proton Mail: Listeneinträge mit data-element-id (= Unterhaltungs- bzw. Nachrichten-ID der Proton-API).
  // Absender, Betreff, Datum und Anhänge kommen aus dem Mitschnitt der API-Antworten (lu-capture.js), nur wenn der
  // fehlt, aus dem Seitenaufbau. „Ungelesen“ zeigt die Seite selbst am aktuellsten (Klasse read/unread).
  // Der Inhalt steht in iframe[data-testid="content-iframe"] innerhalb von article[data-message-id].
  adapters.proton = {
    allRows: function () {
      return outermost(qa(document, "[data-element-id]"), "data-element-id").filter(visible);
    },
    isPlaceholder: function (row) {
      return /^placeholder/.test(attr(row, "data-element-id")) || row.classList.contains("item-is-loading");
    },
    rows: function () {
      var self = this;
      return this.allRows().filter(function (row) { return !self.isPlaceholder(row); });
    },
    unreadOf: function (row, fallback) {
      if (row.classList.contains("unread")) return true;
      if (row.classList.contains("read")) return false;
      return fallback;
    },
    fromCapture: function (id, meta, row) {
      var senders = meta.Senders || (meta.Sender ? [meta.Sender] : []);
      var names = senders.map(function (p) { return p && (p.Name || p.Address); }).filter(Boolean);
      var time = meta.ContextTime || meta.Time;
      var unread = meta.ContextNumUnread !== undefined ? meta.ContextNumUnread
        : (meta.NumUnread !== undefined ? meta.NumUnread : meta.Unread);
      var attachments = meta.ContextNumAttachments !== undefined ? meta.ContextNumAttachments : meta.NumAttachments;
      return {
        id: id,
        from: names.slice(0, 3).join(", "),
        fromEmail: (senders[0] && senders[0].Address) || "",
        subject: meta.Subject || "",
        snippet: "",
        date: shortDate(time),
        dateTitle: longDate(time),
        unread: this.unreadOf(row, Number(unread) > 0),
        attachment: Number(attachments) > 0,
      };
    },
    fromPage: function (id, row) {
      var sender = first(row, ['[data-testid*="sender"]', ".item-senders", '[class*="sender"]']);
      var subject = first(row, ['[data-testid*="subject"]', ".item-subject", '[class*="subject"]']);
      var date = first(row, ["time", '[data-testid*="date"]', ".item-date"]);
      var emailHolder = first(row, ['[title*="@"]']);
      return fillFromLines({
        id: id,
        from: text(sender),
        fromEmail: emailIn(attr(emailHolder, "title")) || emailIn(attr(sender, "title")),
        subject: text(subject) || attr(subject, "title"),
        snippet: "",
        date: text(date),
        dateTitle: attr(date, "datetime") || attr(date, "title"),
        unread: this.unreadOf(row, attr(row, "data-unread") === "true"),
        attachment: !!q(row, '[data-testid*="attachment"]'),
      }, row);
    },
    list: function () {
      if (hasPasswordField()) return { state: "login" };
      var all = this.allRows();
      var rows = this.rows();
      // Platzhalter (Liste lädt noch) nicht als „unlesbar“ werten.
      if (all.length && !rows.length) return { state: "loading" };
      if (!rows.length && !q(document, '[data-testid="message-list"], [data-testid*="empty-view"], .items-column-list')) {
        return { state: "loading" };
      }
      var capture = captured();
      var self = this;
      var items = rows.map(function (row) {
        var id = attr(row, "data-element-id");
        var meta = (capture.conversations || {})[id] || (capture.messages || {})[id];
        return meta ? self.fromCapture(id, meta, row) : self.fromPage(id, row);
      });
      return { state: "ok", items: items };
    },
    // Eintrag anklicken – auch wenn die Liste gerade ausgeblendet ist. Ist sie gar nicht da (schmales Fenster: Proton
    // zeigt dann nur die offene Unterhaltung), über die Adresse öffnen: /u/<n>/<ordner>/<ID>.
    open: function (id) {
      var all = qa(document, "[data-element-id]");
      for (var i = 0; i < all.length; i++) {
        if (attr(all[i], "data-element-id") === id && !this.isPlaceholder(all[i])) return press(all[i]);
      }
      var segments = location.pathname.split("/");
      var base = segments[1] === "u" ? "/u/" + segments[2] + "/" + segments[3] : "/" + segments[1];
      return routeTo(base + "/" + encodeURIComponent(id));
    },
    // Fertig geladener Inhalt eines Nachrichten-iframes (auch ohne Text, z. B. nur Bilder oder Anhang).
    frameBody: function (frame) {
      try {
        var doc = frame && (frame.contentDocument || (frame.contentWindow && frame.contentWindow.document));
        if (!doc || !doc.body || doc.readyState === "loading") return "";
        if (!doc.body.children.length && !text(doc.body)) return "";
        return cleanHTML(doc.body) || " ";
      } catch (e) {
        return "";
      }
    },
    // Zeigt Proton gerade diese Unterhaltung/Nachricht? Nach dem Klick steht kurz noch die vorige da.
    shows: function (id, capture) {
      var messages = capture.messages || {};
      var articleIds = qa(document, "article[data-message-id]").map(function (a) { return attr(a, "data-message-id"); });
      if (articleIds.indexOf(id) >= 0) return true;
      var known = articleIds.filter(function (m) { return messages[m] && messages[m].ConversationID; });
      if (known.some(function (m) { return messages[m].ConversationID === id; })) return true;
      // Unbekannte Nachrichten (noch nicht mitgeschnitten): dann zählt die Adresse (…/inbox/<ID>).
      return !known.length && location.pathname.split("/").indexOf(id) >= 0;
    },
    message: function (expected) {
      var capture = captured();
      var allMessages = capture.messages || {};
      var self = this;
      var parts = [];
      if (expected && !this.shows(expected, capture)) return { state: "loading" };
      // Jede aufgeklappte Nachricht ist ein article mit ihrer ID – so passen Absender und Datum sicher zum Inhalt.
      qa(document, "article[data-message-id]").forEach(function (article) {
        var frame = q(article, 'iframe[data-testid="content-iframe"]');
        if (!frame) return;
        var html = self.frameBody(frame);
        if (!html) return;
        var meta = allMessages[attr(article, "data-message-id")];
        var sender = meta && meta.Sender;
        var pageSender = first(article, ['[data-testid*="recipient-label"]', '[data-testid*="sender"]']);
        var pageEmail = first(article, ['[data-testid*="recipient-address"]', '[title*="@"]']);
        var pageDate = first(article, ["time", '[data-testid*="date"]']);
        parts.push({
          from: (sender && (sender.Name || sender.Address)) || text(pageSender),
          fromEmail: (sender && sender.Address) || emailIn(text(pageEmail)) || emailIn(attr(pageEmail, "title")),
          to: meta && meta.ToList ? meta.ToList.map(function (p) { return p && (p.Name || p.Address); }).filter(Boolean).join(", ") : "",
          date: meta ? longDate(meta.Time) : (attr(pageDate, "title") || text(pageDate)),
          html: html,
        });
      });
      // Notlösung ohne article-Kennung: nur die Inhalts-iframes (nicht den Editor des Verfassen-Fensters).
      if (!parts.length) {
        qa(document, 'iframe[data-testid="content-iframe"]').forEach(function (frame) {
          var html = self.frameBody(frame);
          if (html) parts.push({ from: "", fromEmail: "", to: "", date: "", html: html });
        });
      }
      if (!parts.length) return { state: "loading" };
      var id = decodeURIComponent((location.pathname.split("/").filter(Boolean).pop() || ""));
      var conversation = (capture.conversations || {})[id] || allMessages[id];
      var subject = (conversation && conversation.Subject)
        || text(first(document, ['[data-testid="conversation-header:subject"]', "h1"]));
      return { state: "ok", subject: subject || "", parts: parts };
    },
  };

  // Mail.ru: Listeneinträge sind Links auf /<ordner>/<id>/.
  adapters.mailru = {
    rows: function () {
      var rows = qa(document, "a.llc");
      if (!rows.length) {
        rows = qa(document, "a[href]").filter(function (a) {
          return /\/(inbox|sent|drafts|spam|trash|[0-9]+)\/[0-9]+:[^/]+/.test(attr(a, "href"));
        });
      }
      return rows.filter(visible);
    },
    list: function () {
      if (hasPasswordField()) return { state: "login" };
      var rows = this.rows();
      if (!rows.length && !q(document, ".dataset__items, .letter-list, [class*='dataset']")) return { state: "loading" };
      var items = rows.map(function (row) {
        var sender = first(row, [".ll-crpt", ".llc__item_correspondent", '[title*="@"]']);
        var subject = first(row, [".ll-sj__normal", ".llc__subject", '[class*="subject"]']);
        var snippet = first(row, [".ll-sp__normal", ".llc__snippet", '[class*="snippet"]']);
        var date = first(row, [".llc__item_date", ".ll-dt", '[class*="date"]']);
        return {
          id: attr(row, "href"),
          from: text(sender),
          fromEmail: emailIn(attr(sender, "title")),
          subject: text(subject),
          snippet: text(snippet),
          date: text(date),
          dateTitle: attr(date, "title"),
          unread: /llc_(new|unread)/.test(row.className) || !!q(row, ".ll-rs_is-active, [class*='unread']") || isBold(subject),
          attachment: !!q(row, '[class*="attach"]'),
        };
      }).map(function (item, index) { return fillFromLines(item, rows[index]); }).filter(function (item) { return item.id; });
      return { state: "ok", items: items };
    },
    open: function (id) {
      var links = qa(document, "a[href]");
      for (var i = 0; i < links.length; i++) {
        if (attr(links[i], "href") === id) return press(links[i]);
      }
      return /^\//.test(id) ? routeTo(id) : false;
    },
    message: function (expected) {
      // Die Adresse enthält die ID der Nachricht (…/inbox/0:1234:0/) – solange sie nicht passt, lädt sie noch.
      var core = expected && (String(expected).match(/\d+:[^/]+/) || [])[0];
      if (core && location.pathname.indexOf(core) < 0) return { state: "loading" };
      var body = first(document, [".letter-body__body-content", ".letter__body", '[class*="letter-body"]', '[class*="letter__body"]']);
      if (!body) return { state: "loading" };
      var subject = first(document, [".thread-subject", ".thread__subject", '[class*="thread__subject"]', "h2"]);
      var sender = first(document, [".letter-contact", '[class*="letter__author"] [title*="@"]', '[title*="@"]']);
      var date = first(document, [".letter__date", '[class*="letter__date"]']);
      return {
        state: "ok",
        subject: text(subject),
        parts: [{
          from: text(sender),
          fromEmail: emailIn(attr(sender, "title")) || emailIn(text(sender)),
          to: "",
          date: text(date),
          html: cleanHTML(body) || cleanHTML(largestTextBlock(document.body)),
        }],
      };
    },
  };

  // Liste plausibel? Hat mindestens die Hälfte der Einträge weder Absender noch Betreff, hat sich die Seite
  // geändert – dann lieber die Webseite zeigen als eine Liste voller „(unbekannt)“.
  function checked(list) {
    if (list && list.state === "ok" && list.items && list.items.length) {
      var empty = list.items.filter(function (item) { return !item.from && !item.fromEmail && !item.subject; }).length;
      if (empty * 2 >= list.items.length) return { state: "error", reason: "unreadable", items: [] };
    }
    return list;
  }

  function run(name, action) {
    var adapter = adapters[name];
    if (!adapter) return result({ state: "unsupported" });
    try {
      return result(action(adapter));
    } catch (e) {
      return result({ state: "error", error: String(e && e.message || e) });
    }
  }

  window.LuWebmail = {
    version: 1,
    list: function (name) { return run(name, function (a) { return checked(a.list()); }); },
    open: function (name, id) { return run(name, function (a) { return { state: a.open(id) ? "ok" : "missing" }; }); },
    message: function (name, id) { return run(name, function (a) { return a.message(id); }); },
  };
})();
"""##

    /// shared/webmail/lu-capture.js
    public static let webmailCaptureJS = #"""
// Lu-Mail Web-Leser – Mitschnitt (docs/FUNKTIONEN.md §21)
//
// Läuft im Seitenkontext des Webmails ab Dokumentbeginn (macOS: WKUserScript in der Page-World, Windows:
// AddScriptToExecuteOnDocumentCreatedAsync). Merkt sich die Metadaten aus den Listen-Antworten der Webmail-API
// (Proton: /api/mail/v4/conversations und /messages – Absender, Betreff, Datum, ungelesen sind dort Klartext;
// der verschlüsselte Inhalt wird verworfen) und legt sie als JSON im Attribut data-lu-capture von <html> ab.
// lu-webmail.js liest sie von dort – das ist stabiler als Absender und Betreff aus dem Seitenaufbau zu lesen.
(function () {
  "use strict";
  if (window.__luCaptureInstalled) return;
  window.__luCaptureInstalled = true;

  var PATTERN = /\/api\/mail\/v4\/(conversations|messages)(\/[A-Za-z0-9_=-]+)?(\?|$)/;
  var LIMIT = 3000;
  var store = { conversations: {}, messages: {} };
  var count = 0;
  var timer = null;

  function person(p) {
    return p ? { Name: p.Name || "", Address: p.Address || "" } : null;
  }

  function slim(item) {
    return {
      ID: item.ID,
      ConversationID: item.ConversationID,
      Subject: item.Subject,
      Senders: Array.isArray(item.Senders) ? item.Senders.map(person) : undefined,
      Sender: person(item.Sender),
      ToList: Array.isArray(item.ToList) ? item.ToList.map(person) : undefined,
      Time: item.Time,
      ContextTime: item.ContextTime,
      Unread: item.Unread,
      NumUnread: item.NumUnread,
      ContextNumUnread: item.ContextNumUnread,
      NumAttachments: item.NumAttachments,
      ContextNumAttachments: item.ContextNumAttachments,
    };
  }

  function save() {
    if (timer) return;
    timer = setTimeout(function () {
      timer = null;
      try {
        document.documentElement.setAttribute("data-lu-capture", JSON.stringify(store));
      } catch (e) { /* zu groß oder kein Dokument – egal */ }
    }, 50);
  }

  // Zusammenführen statt ersetzen: Detail-Antworten (…/conversations/<ID>) enthalten z. B. keine Context-Felder
  // und keine Absenderliste – die Werte aus der Liste sollen erhalten bleiben.
  function put(map, item) {
    if (!item || !item.ID) return;
    var next = slim(item);
    var old = store[map][item.ID];
    if (old) {
      Object.keys(next).forEach(function (key) {
        if (next[key] === undefined || next[key] === null) next[key] = old[key];
      });
    } else {
      count++;
    }
    store[map][item.ID] = next;
  }

  function take(url, data) {
    if (!data || typeof data !== "object") return;
    // Speicher begrenzen – vor der Antwort leeren, damit sie selbst immer vollständig ankommt.
    var incoming = (data.Conversations || []).length + (data.Messages || []).length + 2;
    if (count + incoming > LIMIT) {
      store = { conversations: {}, messages: {} };
      count = 0;
    }
    (data.Conversations || []).forEach(function (c) { put("conversations", c); });
    (data.Messages || []).forEach(function (m) { put("messages", m); });
    if (data.Conversation) put("conversations", data.Conversation);
    if (data.Message) put("messages", data.Message);
    save();
  }

  // Proton ruft fetch mit einem URL-Objekt auf (new URL(…)), andere mit Text oder Request.
  function urlOf(input) {
    if (!input) return "";
    if (typeof input === "string") return input;
    if (typeof Request !== "undefined" && input instanceof Request) return input.url;
    if (typeof input.href === "string") return input.href;
    return String(input);
  }

  var originalFetch = window.fetch;
  if (typeof originalFetch === "function") {
    window.fetch = function (input, init) {
      var url = urlOf(input);
      var promise = originalFetch.apply(window, arguments);
      if (PATTERN.test(url)) {
        promise.then(function (response) {
          try {
            response.clone().json().then(function (data) { take(url, data); }, function () {});
          } catch (e) { /* Antwort nicht lesbar */ }
        }, function () {});
      }
      return promise;
    };
  }

  var originalOpen = XMLHttpRequest.prototype.open;
  var originalSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open = function (method, url) {
    this.__luUrl = urlOf(url);
    return originalOpen.apply(this, arguments);
  };
  XMLHttpRequest.prototype.send = function () {
    var xhr = this;
    if (PATTERN.test(xhr.__luUrl || "")) {
      xhr.addEventListener("load", function () {
        try {
          var data = xhr.responseType === "json" ? xhr.response : JSON.parse(xhr.responseText);
          take(xhr.__luUrl, data);
        } catch (e) { /* keine JSON-Antwort */ }
      });
    }
    return originalSend.apply(this, arguments);
  };
})();
"""#
}
