using System.Globalization;
using System.Text;
using LuMail.Core.Models;

namespace LuMail.Core.Auth;

public static class OAuthUrls
{
    /// <summary>Ersetzt <c>{port}</c> in redirectUriTemplate.</summary>
    public static string BuildRedirectUri(string template, int port) =>
        template.Replace("{port}", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>
    /// <c>authorizationEndpoint?response_type=code&amp;client_id&amp;redirect_uri&amp;scope&amp;state&amp;code_challenge
    /// &amp;code_challenge_method=S256&amp;login_hint=&lt;optional&gt;&amp;&lt;extraParams&gt;</c>
    /// </summary>
    public static Uri BuildAuthorizationUrl(
        OAuthSettings settings, string clientId, string redirectUri, string state, string codeChallenge, string? loginHint = null)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", clientId),
            new("redirect_uri", redirectUri),
            new("scope", string.Join(' ', settings.Scopes)),
            new("state", state),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", PkcePair.Method),
        };
        if (!string.IsNullOrWhiteSpace(loginHint)) parameters.Add(new("login_hint", loginHint.Trim()));
        foreach (var (key, value) in settings.ExtraParams) parameters.Add(new(key, value));

        var sb = new StringBuilder(settings.AuthorizationEndpoint);
        sb.Append(settings.AuthorizationEndpoint.Contains('?') ? '&' : '?');
        sb.Append(string.Join('&', parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
        return new Uri(sb.ToString());
    }

    /// <summary>Parst einen Query-String (oder eine Form-URL-Kodierung): '+' = Leerzeichen.</summary>
    public static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(query)) return result;
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = Decode(idx < 0 ? pair : pair[..idx]);
            var value = idx < 0 ? "" : Decode(pair[(idx + 1)..]);
            result.TryAdd(key, value);
        }
        return result;
    }

    private static string Decode(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return s; }
    }
}
