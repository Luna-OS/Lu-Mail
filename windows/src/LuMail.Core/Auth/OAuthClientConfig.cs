using System.Text.Json;

namespace LuMail.Core.Auth;

/// <summary>Eigene OAuth-Client-Daten eines Anbieters aus oauth.json.</summary>
public sealed record OAuthClientCredentials(string ClientId, string? ClientSecret)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>
/// Inhalt von <c>%APPDATA%\LuMail\oauth.json</c>:
/// <c>{ "gmail": { "clientId": "…", "clientSecret": "…" }, "mailru": { … } }</c>.
/// Schlüssel mit führendem "_" (z. B. "_hinweis") werden ignoriert. Leere clientId = OAuth nicht verfügbar.
/// </summary>
public sealed class OAuthClientConfig
{
    private readonly Dictionary<string, OAuthClientCredentials> _clients;

    public static OAuthClientConfig Empty { get; } = new(new Dictionary<string, OAuthClientCredentials>());

    private OAuthClientConfig(Dictionary<string, OAuthClientCredentials> clients) => _clients = clients;

    public IReadOnlyDictionary<string, OAuthClientCredentials> Clients => _clients;

    /// <summary>Lädt die Datei; fehlt sie oder ist sie ungültig, ist die Konfiguration leer.</summary>
    public static OAuthClientConfig Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static OAuthClientConfig Parse(string json)
    {
        var result = new Dictionary<string, OAuthClientCredentials>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return new OAuthClientConfig(result);

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Name.StartsWith('_') || property.Value.ValueKind != JsonValueKind.Object) continue;
            var clientId = GetString(property.Value, "clientId");
            var secret = GetString(property.Value, "clientSecret");
            if (string.IsNullOrWhiteSpace(clientId)) continue;
            result[property.Name] = new OAuthClientCredentials(clientId.Trim(),
                string.IsNullOrWhiteSpace(secret) ? null : secret.Trim());
        }
        return new OAuthClientConfig(result);
    }

    private static string? GetString(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString();
        }
        return null;
    }

    public bool TryGet(string providerId, out OAuthClientCredentials credentials)
    {
        if (_clients.TryGetValue(providerId, out var c) && c.IsConfigured)
        {
            credentials = c;
            return true;
        }
        credentials = null!;
        return false;
    }
}
