using System.Text.Json.Serialization;

namespace LuMail.Core.Models;

/// <summary>Ein E-Mail-Anbieter aus <c>shared/providers.json</c>.</summary>
public sealed class Provider
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Markenfarbe als Hex-String (#RRGGBB).</summary>
    public string Color { get; set; } = "#8B7CFF";
    public List<string> Domains { get; set; } = new();
    public ServerEndpoint Imap { get; set; } = new();
    public ServerEndpoint Smtp { get; set; } = new();
    /// <summary>"oauth2" und/oder "password".</summary>
    public List<string> AuthMethods { get; set; } = new();

    [JsonPropertyName("oauth")]
    public OAuthSettings? OAuth { get; set; }

    /// <summary>"gmail" = Kategorien per X-GM-RAW vom Server, "none" = lokaler Klassifizierer.</summary>
    public string ServerCategories { get; set; } = "none";
    public string PasswordLabel { get; set; } = "Passwort";
    public string PasswordHelp { get; set; } = "";
    public string HelpUrl { get; set; } = "";

    /// <summary>Selbstsigniertes Zertifikat akzeptieren – aber nur für Loopback-Hosts (Proton Mail Bridge).</summary>
    public bool AllowInsecureLoopbackCertificate { get; set; }

    /// <summary>Nach dem SMTP-Versand eine Kopie per IMAP APPEND im Ordner mit Rolle "sent" ablegen.</summary>
    public bool SaveSentViaImap { get; set; }

    /// <summary>„Andere (IMAP)“: Server werden pro Konto gewählt (<see cref="Account.CustomServers"/>).</summary>
    public bool CustomServers { get; set; }

    /// <summary>MX-Host-Endungen für die Anbieter-Erkennung (z. B. Google Workspace → "google.com").</summary>
    public List<string> MxSuffixes { get; set; } = new();

    /// <summary>Webmail-Daten für Web-Konten (FUNKTIONEN §20), null bei „Andere (IMAP)“.</summary>
    public WebConfig? Web { get; set; }

    [JsonIgnore]
    public bool SupportsWeb => Web is not null && AuthMethods.Contains(Account.AuthWeb, StringComparer.OrdinalIgnoreCase);

    /// <summary>„So geht's“-Schritte und lokale Portprüfung für den Assistenten.</summary>
    public ProviderSetup Setup { get; set; } = new();

    [JsonIgnore]
    public bool SupportsOAuth =>
        OAuth is not null && AuthMethods.Contains(Account.AuthOAuth2, StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool SupportsPassword => AuthMethods.Contains(Account.AuthPassword, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// „IMAP zuerst, wo das normale Passwort genügt“ (§20): <c>authMethods</c> beginnt mit <c>password</c>
    /// (Hostinger) – IMAP ist der Standardweg, die Web-Anmeldung die Alternative.
    /// </summary>
    [JsonIgnore]
    public bool PrefersImap => string.Equals(AuthMethods.FirstOrDefault(), Account.AuthPassword, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool UsesGmailCategories => string.Equals(ServerCategories, "gmail", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name;
}

public sealed class ServerEndpoint
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    /// <summary>"tls" (implizites TLS), "starttls" oder "none".</summary>
    public string Security { get; set; } = "tls";
}

public sealed class OAuthSettings
{
    public string AuthorizationEndpoint { get; set; } = "";
    public string TokenEndpoint { get; set; } = "";
    public string? UserInfoEndpoint { get; set; }
    public List<string> Scopes { get; set; } = new();
    /// <summary>z. B. <c>http://127.0.0.1:{port}/callback</c>.</summary>
    public string RedirectUriTemplate { get; set; } = "http://127.0.0.1:{port}/callback";
    public Dictionary<string, string> ExtraParams { get; set; } = new();
    /// <summary>"idToken" oder "userInfo".</summary>
    public string EmailSource { get; set; } = "idToken";
    public string? AppRegistrationUrl { get; set; }
}

/// <summary><c>providers.json → web</c>.</summary>
public sealed class WebConfig
{
    public string LoginUrl { get; set; } = "";
    public string HomeUrl { get; set; } = "";
    public List<string> LoggedInPrefixes { get; set; } = new();
    /// <summary>Ordner-Schlüssel (primary, promotions, social, updates, forums, sent, drafts, archive, spam, trash) → URL.</summary>
    public Dictionary<string, string> Folders { get; set; } = new();
    public string? UnreadTitlePattern { get; set; }
    /// <summary>Web-Leser für die Lu-Ansicht (§21): "gmail", "proton", "mailru"; null = nur Webseite.</summary>
    public string? Reader { get; set; }
    /// <summary>Adresse zum Verfassen einer neuen E-Mail im Webmail (optional).</summary>
    public string? ComposeUrl { get; set; }
}

/// <summary><c>providers.json → setup</c>.</summary>
public sealed class ProviderSetup
{
    public List<SetupStep> PasswordSteps { get; set; } = new();

    [JsonPropertyName("oauthSteps")]
    public List<SetupStep> OAuthSteps { get; set; } = new();

    /// <summary>Proton: 1143 – TCP-Test auf 127.0.0.1, ob die Bridge läuft.</summary>
    public int? CheckLocalPort { get; set; }
}

/// <summary>Ein Schritt in „So geht's“ (optional mit Link-Button).</summary>
public sealed class SetupStep
{
    public string Text { get; set; } = "";
    public string? Url { get; set; }
    public string? Button { get; set; }
}

/// <summary><c>providers.json → discovery</c>.</summary>
public sealed class DiscoverySettings
{
    /// <summary>DoH-URL mit {domain}, Antwort im JSON-Format (application/dns-json).</summary>
    public string MxLookup { get; set; } = "https://cloudflare-dns.com/dns-query?name={domain}&type=MX";
    /// <summary>Autoconfig-URLs (Thunderbird-Format) mit {domain} und {email}.</summary>
    public List<string> Autoconfig { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 6;
}

/// <summary>Regeln für eine Ordnerrolle (SPECIAL-USE-Attribute und bekannte Namen).</summary>
public sealed class FolderRoleDefinition
{
    public List<string> SpecialUse { get; set; } = new();
    public List<string> Names { get; set; } = new();
}

/// <summary>Wurzelobjekt von providers.json.</summary>
public sealed class ProviderCatalogDocument
{
    public int Version { get; set; }
    public List<Provider> Providers { get; set; } = new();
    public Dictionary<string, FolderRoleDefinition> FolderRoles { get; set; } = new();
    public DiscoverySettings Discovery { get; set; } = new();
    /// <summary>Nur macOS (WKWebView); Windows nutzt den Standard-User-Agent von WebView2.</summary>
    public Dictionary<string, string> WebUserAgent { get; set; } = new();
}
