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

    /// <summary>Eingebaute Client-IDs (<c>BuiltInOAuth.json</c>, von der CI aus Secrets gefüllt; im Repository <c>{}</c>).</summary>
    public static OAuthClientConfig BuiltIn { get; } = LoadBuiltIn();

    private static OAuthClientConfig LoadBuiltIn()
    {
        try
        {
            using var stream = typeof(OAuthClientConfig).Assembly.GetManifestResourceStream("LuMail.BuiltInOAuth.json");
            if (stream is null) return Empty;
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// Reihenfolge nach FUNKTIONEN §18: Eintrag dieser Konfiguration (Nutzer-oauth.json) → Eintrag aus <paramref name="fallback"/>.
    /// </summary>
    public OAuthClientConfig WithFallback(OAuthClientConfig fallback)
    {
        var merged = new Dictionary<string, OAuthClientCredentials>(fallback._clients, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, c) in _clients) merged[id] = c;
        return new OAuthClientConfig(merged);
    }

    /// <summary>Nutzer-oauth.json mit eingebauten Client-IDs als Rückfall.</summary>
    public static OAuthClientConfig LoadWithBuiltIn(string userPath) => Load(userPath).WithFallback(BuiltIn);

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
