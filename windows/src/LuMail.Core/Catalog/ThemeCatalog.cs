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
    /// <summary>Varianten (luna, neumond, vollmond) in Dateireihenfolge.</summary>
    public IReadOnlyList<ThemeVariant> Variants { get; }
    /// <summary>Akzentfarben (lila, blau, …) in Dateireihenfolge.</summary>
    public IReadOnlyList<ThemeAccent> Accents { get; }

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

        var variants = new List<ThemeVariant>();
        if (root.TryGetProperty("variants", out var vs))
        {
            foreach (var v in vs.EnumerateObject())
            {
                var c = v.Value.GetProperty("colors");
                variants.Add(new ThemeVariant(v.Name,
                    v.Value.TryGetProperty("name", out var n2) ? n2.GetString() ?? v.Name : v.Name,
                    v.Value.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    ReadStrings(c.GetProperty("dark")), ReadStrings(c.GetProperty("light"))));
            }
        }
        if (variants.Count == 0) variants.Add(new ThemeVariant("luna", "Luna", "", Dark, Light));
        Variants = variants;

        var accents = new List<ThemeAccent>();
        if (root.TryGetProperty("accents", out var acs))
        {
            foreach (var a in acs.EnumerateObject())
                accents.Add(new ThemeAccent(a.Name, a.Value.GetProperty("name").GetString() ?? a.Name,
                    a.Value.GetProperty("dark").GetString() ?? "", a.Value.GetProperty("light").GetString() ?? ""));
        }
        Accents = accents;
    }

    /// <summary>Variante nach Id (unbekannt → erste, also Luna).</summary>
    public ThemeVariant Variant(string? id) =>
        Variants.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Variants[0];

    /// <summary>
    /// Effektive Palette: Farben der Variante; mit Akzent werden accent, accentHover und unreadDot ersetzt.
    /// </summary>
    public IReadOnlyDictionary<string, string> Palette(string? variantId, bool dark, string? accentId)
    {
        var variant = Variant(variantId);
        var palette = new Dictionary<string, string>(dark ? variant.Dark : variant.Light, StringComparer.OrdinalIgnoreCase);
        var accent = Accents.FirstOrDefault(a => string.Equals(a.Id, accentId, StringComparison.OrdinalIgnoreCase));
        if (accent is not null)
        {
            var hex = dark ? accent.Dark : accent.Light;
            palette["accent"] = hex;
            palette["accentHover"] = hex;
            palette["unreadDot"] = hex;
        }
        return palette;
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

/// <summary>Variante aus lu-theme.json → variants.</summary>
public sealed record ThemeVariant(string Id, string Name, string Description,
    IReadOnlyDictionary<string, string> Dark, IReadOnlyDictionary<string, string> Light);

/// <summary>Akzentfarbe aus lu-theme.json → accents.</summary>
public sealed record ThemeAccent(string Id, string Name, string Dark, string Light);
