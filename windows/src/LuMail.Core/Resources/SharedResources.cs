using System.Text;
using System.Text.Json;

namespace LuMail.Core.Resources;

/// <summary>
/// Zugriff auf die eingebetteten Dateien aus <c>shared/</c>
/// (logischer Name <c>LuMail.Shared.&lt;Dateiname&gt;</c>, siehe LuMail.Core.csproj).
/// </summary>
public static class SharedResources
{
    public const string Prefix = "LuMail.Shared.";

    public const string ProvidersFile = "providers.json";
    public const string CategoriesFile = "categories.json";
    public const string ThemeFile = "lu-theme.json";
    public const string LoginPageFile = "login.html";
    public const string DonePageFile = "done.html";
    public const string OAuthExampleFile = "oauth.example.json";

    public static string ReadText(string fileName)
    {
        var assembly = typeof(SharedResources).Assembly;
        using var stream = assembly.GetManifestResourceStream(Prefix + fileName)
            ?? throw new InvalidOperationException($"Eingebettete Ressource '{Prefix}{fileName}' fehlt.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static IEnumerable<string> ResourceNames =>
        typeof(SharedResources).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal));

    public const string WebmailScriptFile = "lu-webmail.js";

    private static readonly Lazy<string> WebmailScriptText = new(() => ReadText(WebmailScriptFile));

    /// <summary>Web-Leser <c>shared/webmail/lu-webmail.js</c> (definiert <c>window.LuWebmail</c>).</summary>
    public static string WebmailScript => WebmailScriptText.Value;

    public const string CaptureScriptFile = "lu-capture.js";

    private static readonly Lazy<string> CaptureScriptText = new(() => ReadText(CaptureScriptFile));

    /// <summary>
    /// Mitschnitt <c>shared/webmail/lu-capture.js</c> (§21): läuft ab Dokumentbeginn im Seitenkontext und legt die
    /// Metadaten der Webmail-API im Attribut <c>data-lu-capture</c> von &lt;html&gt; ab.
    /// </summary>
    public static string CaptureScript => CaptureScriptText.Value;

    public static string LoginPageTemplate => ReadText(LoginPageFile);
    public static string DonePageTemplate => ReadText(DonePageFile);
}

/// <summary>Gemeinsame JSON-Optionen (camelCase wie die Dateien in shared/ und accounts.json).</summary>
public static class LuJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };
}
