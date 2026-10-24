using System.Text.Json.Serialization;

namespace LuMail.Core.Models;

/// <summary>
/// Ein eingerichtetes Konto. Wird ohne Geheimnisse in <c>accounts.json</c> gespeichert:
/// <c>{ "id", "providerId", "email", "displayName", "authMethod" }</c>.
/// </summary>
public sealed class Account
{
    public const string AuthOAuth2 = "oauth2";
    public const string AuthPassword = "password";

    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();
    public string ProviderId { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>"oauth2" oder "password".</summary>
    public string AuthMethod { get; set; } = AuthPassword;

    /// <summary>Nur bei Anbietern mit <c>customServers</c>: eigene IMAP-/SMTP-Server dieses Kontos.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CustomServerSettings? CustomServers { get; set; }

    /// <summary>Signatur (Text), wird als <c>\n\n-- \n&lt;Signatur&gt;</c> eingefügt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Signature { get; set; }

    /// <summary>IMAP-Server dieses Kontos (eigene Server oder die des Anbieters).</summary>
    public ServerEndpoint ImapFor(Provider provider) =>
        provider.CustomServers && CustomServers?.Imap is { Host.Length: > 0 } imap ? imap : provider.Imap;

    /// <summary>SMTP-Server dieses Kontos.</summary>
    public ServerEndpoint SmtpFor(Provider provider) =>
        provider.CustomServers && CustomServers?.Smtp is { Host.Length: > 0 } smtp ? smtp : provider.Smtp;

    [JsonIgnore]
    public bool IsOAuth => string.Equals(AuthMethod, AuthOAuth2, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Email;
}

/// <summary><c>accounts.json → customServers</c>: <c>{ "imap": {host,port,security}, "smtp": {…} }</c>.</summary>
public sealed class CustomServerSettings
{
    public ServerEndpoint Imap { get; set; } = new() { Port = 993, Security = "tls" };
    public ServerEndpoint Smtp { get; set; } = new() { Port = 465, Security = "tls" };
}
