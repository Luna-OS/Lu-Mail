using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LuMail.Core.Models;

namespace LuMail.Core.Auth;

/// <summary>Antwort des Token-Endpunkts.</summary>
public sealed class OAuthTokenResponse
{
    public string AccessToken { get; init; } = "";
    public string? RefreshToken { get; init; }
    public string? IdToken { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class OAuthException : Exception
{
    public string? Error { get; }

    public OAuthException(string message, string? error = null, Exception? inner = null) : base(message, inner)
    {
        Error = error;
    }
}

/// <summary>Token-Tausch, Refresh und UserInfo per HttpClient.</summary>
public sealed class OAuthClient
{
    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    public OAuthClient(HttpClient http, TimeProvider? time = null)
    {
        _http = http;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>POST tokenEndpoint: grant_type=authorization_code, code, redirect_uri, client_id, [client_secret], code_verifier.</summary>
    public Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthSettings settings, OAuthClientCredentials client,
        string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirectUri),
            new("client_id", client.ClientId),
        };
        if (!string.IsNullOrEmpty(client.ClientSecret)) form.Add(new("client_secret", client.ClientSecret));
        form.Add(new("code_verifier", codeVerifier));
        return PostTokenAsync(settings.TokenEndpoint, form, ct);
    }

    /// <summary>POST tokenEndpoint: grant_type=refresh_token.</summary>
    public Task<OAuthTokenResponse> RefreshAsync(OAuthSettings settings, OAuthClientCredentials client,
        string refreshToken, CancellationToken ct = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
            new("client_id", client.ClientId),
        };
        if (!string.IsNullOrEmpty(client.ClientSecret)) form.Add(new("client_secret", client.ClientSecret));
        return PostTokenAsync(settings.TokenEndpoint, form, ct);
    }

    /// <summary>GET userInfoEndpoint?access_token=… → Feld "email".</summary>
    public async Task<string?> GetUserInfoEmailAsync(string endpoint, string accessToken, CancellationToken ct = default)
    {
        var url = endpoint + (endpoint.Contains('?') ? "&" : "?") + "access_token=" + Uri.EscapeDataString(accessToken);
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new OAuthException($"UserInfo-Abfrage fehlgeschlagen (HTTP {(int)response.StatusCode}).");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String
            ? email.GetString()
            : null;
    }

    private async Task<OAuthTokenResponse> PostTokenAsync(string endpoint, List<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var now = _time.GetUtcNow();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (JsonException ex)
        {
            throw new OAuthException($"Ungültige Antwort vom Token-Endpunkt (HTTP {(int)response.StatusCode}).", null, ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var error = Str(root, "error");
            if (!response.IsSuccessStatusCode || error is not null)
            {
                var description = Str(root, "error_description");
                var message = new StringBuilder("Token-Anfrage abgelehnt");
                if (error is not null) message.Append(": ").Append(error);
                if (description is not null) message.Append(" – ").Append(description);
                throw new OAuthException(message.ToString(), error);
            }

            var accessToken = Str(root, "access_token");
            if (string.IsNullOrEmpty(accessToken)) throw new OAuthException("Token-Antwort enthält kein access_token.");

            return new OAuthTokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = Str(root, "refresh_token"),
                IdToken = Str(root, "id_token"),
                TokenType = Str(root, "token_type") ?? "Bearer",
                ExpiresAt = now.AddSeconds(ExpiresIn(root)),
            };
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static double ExpiresIn(JsonElement root)
    {
        if (root.TryGetProperty("expires_in", out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
            if (v.ValueKind == JsonValueKind.String &&
                double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        }
        return 3600;
    }
}

/// <summary>Minimaler JWT-Payload-Leser (keine Signaturprüfung – das id_token kommt direkt vom Token-Endpunkt per TLS).</summary>
public static class JwtDecoder
{
    public static string? TryGetClaim(string? jwt, string claim)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            using var doc = JsonDocument.Parse(Base64Url.Decode(parts[1]));
            return doc.RootElement.TryGetProperty(claim, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    public static string? TryGetEmail(string? idToken) => TryGetClaim(idToken, "email");
}

/// <summary>SASL XOAUTH2 (IMAP AUTHENTICATE XOAUTH2 / SMTP AUTH XOAUTH2).</summary>
public static class XOAuth2
{
    /// <summary>"user=" + email + "\x01auth=Bearer " + token + "\x01\x01".</summary>
    public static string BuildRaw(string user, string accessToken) =>
        "user=" + user + "\u0001auth=Bearer " + accessToken + "\u0001\u0001";

    /// <summary>Base64 des Rohstrings (so wird er an den Server gesendet).</summary>
    public static string BuildInitialResponse(string user, string accessToken) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildRaw(user, accessToken)));
}
