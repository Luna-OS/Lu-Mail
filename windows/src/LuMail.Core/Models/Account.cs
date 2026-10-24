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

    [JsonIgnore]
    public bool IsOAuth => string.Equals(AuthMethod, AuthOAuth2, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Email;
}
