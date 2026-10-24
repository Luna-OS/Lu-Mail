using System.Text;
using LuMail.Core.Models;
using LuMail.Core.Resources;

namespace LuMail.Core.Login;

/// <summary>HTML-Escaping für Template-Werte.</summary>
public static class Html
{
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length + 16);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}

/// <summary>Füllt <c>shared/login/login.html</c> und <c>done.html</c>. Alle Werte werden HTML-escaped (außer {{EXTRA_FIELDS}}).</summary>
public static partial class LoginTemplates
{
    public const string CustomServersFile = "custom-servers.html";

    /// <summary>
    /// Ersetzt <c>{{KEY}}</c> in einem Durchgang (eingesetzte Werte werden nicht erneut ersetzt).
    /// Werte werden escaped; Schlüssel in <paramref name="rawKeys"/> werden unverändert eingesetzt.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string?> values, IReadOnlyCollection<string>? rawKeys = null) =>
        Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            if (!values.TryGetValue(key, out var value)) return m.Value;
            return rawKeys is not null && rawKeys.Contains(key) ? value ?? "" : Html.Escape(value);
        });

    public static string RenderLogin(Provider provider, string state, string? email, string? error, string? template = null,
        CustomServerSettings? servers = null) =>
        Render(template ?? SharedResources.LoginPageTemplate, new Dictionary<string, string?>
        {
            ["PROVIDER_NAME"] = provider.Name,
            ["PROVIDER_COLOR"] = provider.Color,
            ["PASSWORD_LABEL"] = provider.PasswordLabel,
            ["PASSWORD_HELP"] = provider.PasswordHelp,
            ["HELP_URL"] = provider.HelpUrl,
            ["STATE"] = state,
            ["EMAIL"] = email ?? "",
            ["ERROR"] = error ?? "",
            ["EXTRA_FIELDS"] = provider.CustomServers ? RenderCustomServers(servers) : "",
        }, new[] { "EXTRA_FIELDS" });

    /// <summary>Felder für „Andere (IMAP)“ (<c>shared/login/custom-servers.html</c>); leer = 993/tls bzw. 465/tls.</summary>
    public static string RenderCustomServers(CustomServerSettings? servers, string? template = null)
    {
        var s = servers ?? new CustomServerSettings();
        static string Sel(bool on) => on ? "selected" : "";
        var imapStartTls = string.Equals(s.Imap.Security, "starttls", StringComparison.OrdinalIgnoreCase);
        var smtpStartTls = string.Equals(s.Smtp.Security, "starttls", StringComparison.OrdinalIgnoreCase);
        return Render(template ?? SharedResources.ReadText(CustomServersFile), new Dictionary<string, string?>
        {
            ["IMAP_HOST"] = s.Imap.Host,
            ["IMAP_PORT"] = (s.Imap.Port > 0 ? s.Imap.Port : 993).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SMTP_HOST"] = s.Smtp.Host,
            ["SMTP_PORT"] = (s.Smtp.Port > 0 ? s.Smtp.Port : 465).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["IMAP_TLS_SELECTED"] = Sel(!imapStartTls),
            ["IMAP_STARTTLS_SELECTED"] = Sel(imapStartTls),
            ["SMTP_TLS_SELECTED"] = Sel(!smtpStartTls),
            ["SMTP_STARTTLS_SELECTED"] = Sel(smtpStartTls),
        });
    }

    /// <summary>
    /// Liest und prüft die Serverfelder aus dem Formular (imapHost, imapPort, imapSecurity, smtpHost, smtpPort, smtpSecurity).
    /// Liefert die (ggf. teilweise) Werte zum erneuten Anzeigen und eine Fehlermeldung oder null.
    /// </summary>
    public static (CustomServerSettings Servers, string? Error) ParseCustomServers(IReadOnlyDictionary<string, string> form)
    {
        string F(string key) => form.TryGetValue(key, out var v) ? v.Trim() : "";
        var servers = new CustomServerSettings
        {
            Imap = new ServerEndpoint { Host = F("imapHost"), Port = ParsePort(F("imapPort"), 993), Security = ParseSecurity(F("imapSecurity")) },
            Smtp = new ServerEndpoint { Host = F("smtpHost"), Port = ParsePort(F("smtpPort"), 465), Security = ParseSecurity(F("smtpSecurity")) },
        };
        string? error = null;
        if (!IsValidHost(servers.Imap.Host)) error = "Bitte einen gültigen IMAP-Server angeben (z. B. imap.beispiel.de).";
        else if (!IsValidHost(servers.Smtp.Host)) error = "Bitte einen gültigen SMTP-Server angeben (z. B. smtp.beispiel.de).";
        else if (servers.Imap.Port <= 0 || servers.Smtp.Port <= 0) error = "Ports müssen zwischen 1 und 65535 liegen.";
        else if (!IsSecurity(F("imapSecurity")) || !IsSecurity(F("smtpSecurity"))) error = "Bitte SSL/TLS oder STARTTLS wählen.";
        return (servers, error);
    }

    private static int ParsePort(string text, int fallback)
    {
        if (text.Length == 0) return fallback;
        return int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p) && p is > 0 and <= 65535 ? p : -1;
    }

    private static bool IsSecurity(string s) => s.Length == 0 || s.Equals("tls", StringComparison.OrdinalIgnoreCase) || s.Equals("starttls", StringComparison.OrdinalIgnoreCase);

    private static string ParseSecurity(string s) => s.Equals("starttls", StringComparison.OrdinalIgnoreCase) ? "starttls" : "tls";

    private static bool IsValidHost(string host) =>
        host.Length is > 0 and <= 253 && HostPattern().IsMatch(host);

    public static string RenderDone(string title, string message, string? template = null) =>
        Render(template ?? SharedResources.DonePageTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = title,
            ["MESSAGE"] = message,
        });

    [System.Text.RegularExpressions.GeneratedRegex(@"\{\{([A-Z_]+)\}\}")]
    private static partial System.Text.RegularExpressions.Regex Placeholder();

    [System.Text.RegularExpressions.GeneratedRegex(@"^(?:\[[0-9A-Fa-f:.]+\]|[A-Za-z0-9](?:[A-Za-z0-9-]{0,62})(?:\.[A-Za-z0-9-]{1,63})*)$")]
    private static partial System.Text.RegularExpressions.Regex HostPattern();
}
