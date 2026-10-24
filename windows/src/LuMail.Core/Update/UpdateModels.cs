using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LuMail.Core.Resources;

namespace LuMail.Core.Update;

/// <summary>Einstellung „Updates“.</summary>
public enum UpdateMode
{
    /// <summary>Im Hintergrund laden, beim Beenden still installieren (Standard).</summary>
    Automatic,
    /// <summary>Nur Hinweis anzeigen, Laden erst auf Klick.</summary>
    NotifyOnly,
    /// <summary>Keine automatische Prüfung.</summary>
    Off,
}

/// <summary>Inhalt von <c>shared/update.json</c>.</summary>
public sealed class UpdateConfig
{
    public int Version { get; set; }
    public string Repository { get; set; } = "";
    public string LatestReleaseApi { get; set; } = "";
    public double CheckIntervalHours { get; set; } = 6;
    public int InitialDelaySeconds { get; set; } = 15;
    /// <summary>Plattform ("macos", "windows-x64", "windows-arm64") → Endung des Asset-Namens.</summary>
    public Dictionary<string, string> AssetSuffixes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ChecksumsAsset { get; set; } = "SHA256SUMS.txt";

    public const string FileName = "update.json";

    private static readonly Lazy<UpdateConfig> EmbeddedInstance = new(() => Parse(SharedResources.ReadText(FileName)));

    public static UpdateConfig LoadEmbedded() => EmbeddedInstance.Value;

    public static UpdateConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<UpdateConfig>(json, LuJson.Options)
                     ?? throw new InvalidDataException("update.json ist leer.");
        config.AssetSuffixes = new Dictionary<string, string>(config.AssetSuffixes, StringComparer.OrdinalIgnoreCase);
        return config;
    }

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(Math.Max(0, InitialDelaySeconds));
    public TimeSpan CheckInterval => TimeSpan.FromHours(CheckIntervalHours > 0 ? CheckIntervalHours : 6);
}

/// <summary>Plattformschlüssel für <see cref="UpdateConfig.AssetSuffixes"/>.</summary>
public static class UpdatePlatforms
{
    public const string MacOS = "macos";
    public const string WindowsX64 = "windows-x64";
    public const string WindowsArm64 = "windows-arm64";

    /// <summary>Windows-Paket nach Prozessor-Architektur (ARM64 → arm64, sonst x64).</summary>
    public static string ForWindows(Architecture architecture) =>
        architecture == Architecture.Arm64 ? WindowsArm64 : WindowsX64;
}

/// <summary>Version MAJOR.MINOR.PATCH (numerischer Vergleich je Teil).</summary>
public readonly partial record struct SemanticVersion(int Major, int Minor, int Patch) : IComparable<SemanticVersion>
{
    public static SemanticVersion Zero { get; } = new(0, 0, 0);

    public bool IsZero => Major == 0 && Minor == 0 && Patch == 0;

    /// <summary>
    /// Streng: optional führendes "v", danach genau MAJOR.MINOR.PATCH.
    /// "1.2.3-beta", "1.2" oder "1.2.3.4" werden abgelehnt (z. B. Vorab-Releases ignorieren).
    /// </summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Strict().Match(text.Trim());
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;
        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    /// <summary>
    /// Version der laufenden App aus AssemblyInformationalVersion ("0.1.0+abc123") oder AssemblyVersion ("0.1.0.0").
    /// Unlesbar → 0.0.0 (Entwickler-Build).
    /// </summary>
    public static SemanticVersion FromAssemblyVersion(string? informationalVersion, Version? assemblyVersion = null)
    {
        var info = informationalVersion?.Split('+')[0].Trim();
        if (TryParse(info, out var v)) return v;
        if (assemblyVersion is not null && assemblyVersion.Major >= 0)
            return new SemanticVersion(assemblyVersion.Major, Math.Max(0, assemblyVersion.Minor), Math.Max(0, assemblyVersion.Build));
        return Zero;
    }

    public int CompareTo(SemanticVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : Patch.CompareTo(other.Patch);
    }

    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    [GeneratedRegex(@"^[vV]?(\d{1,9})\.(\d{1,9})\.(\d{1,9})$")]
    private static partial Regex Strict();
}

/// <summary>Antwort von <c>GET /repos/{owner}/{repo}/releases/latest</c> (nur benötigte Felder).</summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();

    public static GitHubRelease Parse(string json) =>
        JsonSerializer.Deserialize<GitHubRelease>(json) ?? throw new InvalidDataException("Leere Release-Antwort.");
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

/// <summary>Ein verfügbares, geprüftes Update.</summary>
public sealed class UpdateInfo
{
    public SemanticVersion Version { get; init; }
    public string TagName { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Release-Notizen (Markdown).</summary>
    public string Notes { get; init; } = "";
    /// <summary>Release-Seite („Was ist neu?“).</summary>
    public string? HtmlUrl { get; init; }
    public GitHubAsset Asset { get; init; } = new();
    /// <summary>Erwartete SHA-256-Prüfsumme (hex, klein).</summary>
    public string Sha256 { get; init; } = "";
}

/// <summary>Fehler bei Prüfung/Download mit deutscher Meldung.</summary>
public sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Parser für SHA256SUMS.txt (<c>&lt;hex&gt;  &lt;Dateiname&gt;</c>, auch <c>&lt;hex&gt; *&lt;Dateiname&gt;</c>).</summary>
public static partial class Sha256Sums
{
    public static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var m = Line().Match(raw.TrimEnd('\r'));
            if (!m.Success) continue;
            var name = m.Groups["name"].Value.Trim();
            if (name.Length > 0) result[name] = m.Groups["hash"].Value.ToLowerInvariant();
        }
        return result;
    }

    [GeneratedRegex(@"^\s*(?<hash>[0-9A-Fa-f]{64})\s+\*?(?<name>.+?)\s*$")]
    private static partial Regex Line();
}
