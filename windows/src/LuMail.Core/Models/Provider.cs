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

    [JsonIgnore]
    public bool SupportsOAuth =>
        OAuth is not null && AuthMethods.Contains(Account.AuthOAuth2, StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool SupportsPassword => AuthMethods.Contains(Account.AuthPassword, StringComparer.OrdinalIgnoreCase);

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
}
