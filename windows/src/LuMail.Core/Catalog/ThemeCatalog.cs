using System.Text.Json;
using LuMail.Core.Resources;

namespace LuMail.Core.Catalog;

/// <summary>Design-Tokens aus <c>shared/theme/lu-theme.json</c> (Theme „Luna").</summary>
public sealed class ThemeCatalog
{
    public string Name { get; }
    /// <summary>Farbtoken (camelCase-Schlüssel wie in der JSON) → Hex für das dunkle Theme.</summary>
    public IReadOnlyDictionary<string, string> Dark { get; }
    public IReadOnlyDictionary<string, string> Light { get; }
    public IReadOnlyDictionary<string, string> ProviderColors { get; }
    public IReadOnlyDictionary<string, string> CategoryColors { get; }
    public IReadOnlyDictionary<string, double> Radius { get; }

    private ThemeCatalog(JsonElement root)
    {
        Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "Luna" : "Luna";
        var colors = root.GetProperty("colors");
        Dark = ReadStrings(colors.GetProperty("dark"));
        Light = ReadStrings(colors.GetProperty("light"));
        ProviderColors = ReadStrings(root.GetProperty("providers"));
        CategoryColors = ReadStrings(root.GetProperty("categories"));
        Radius = root.GetProperty("radius").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetDouble(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ReadStrings(JsonElement obj) =>
        obj.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<ThemeCatalog> EmbeddedInstance = new(() =>
    {
        using var doc = JsonDocument.Parse(SharedResources.ReadText(SharedResources.ThemeFile));
        return new ThemeCatalog(doc.RootElement.Clone());
    });

    public static ThemeCatalog LoadEmbedded() => EmbeddedInstance.Value;

    /// <summary>"surfaceHover" → "LuSurfaceHover" (Schlüssel der WinUI-Ressourcen).</summary>
    public static string ResourceKey(string token, string prefix = "Lu") =>
        prefix + char.ToUpperInvariant(token[0]) + token[1..];

    /// <summary>Hex "#RRGGBB" oder "#AARRGGBB" → (a, r, g, b).</summary>
    public static (byte A, byte R, byte G, byte B) ParseHex(string hex)
    {
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 6) h = "FF" + h;
        if (h.Length != 8) return (255, 139, 124, 255);
        byte P(int i) => Convert.ToByte(h.Substring(i, 2), 16);
        return (P(0), P(2), P(4), P(6));
    }
}
