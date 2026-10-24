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

/// <summary>Füllt <c>shared/login/login.html</c> und <c>done.html</c>. Alle Werte werden HTML-escaped.</summary>
public static class LoginTemplates
{
    /// <summary>Ersetzt <c>{{KEY}}</c> durch escapete Werte.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        var sb = new StringBuilder(template);
        foreach (var (key, value) in values) sb.Replace("{{" + key + "}}", Html.Escape(value));
        return sb.ToString();
    }

    public static string RenderLogin(Provider provider, string state, string? email, string? error, string? template = null) =>
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
        });

    public static string RenderDone(string title, string message, string? template = null) =>
        Render(template ?? SharedResources.DonePageTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = title,
            ["MESSAGE"] = message,
        });
}
