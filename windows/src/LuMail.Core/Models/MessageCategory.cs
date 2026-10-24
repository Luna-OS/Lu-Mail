using System.Text.Json.Serialization;

namespace LuMail.Core.Models;

/// <summary>Kategorien aus <c>shared/categories.json</c> (virtuelle Unterordner von INBOX).</summary>
public enum MessageCategory
{
    Primary,
    Promotions,
    Social,
    Updates,
    Forums,
}

public static class MessageCategories
{
    /// <summary>Reihenfolge wie in categories.json / Sidebar.</summary>
    public static IReadOnlyList<MessageCategory> All { get; } = new[]
    {
        MessageCategory.Primary, MessageCategory.Promotions, MessageCategory.Social,
        MessageCategory.Updates, MessageCategory.Forums,
    };

    public static string ToId(this MessageCategory category) => category switch
    {
        MessageCategory.Primary => "primary",
        MessageCategory.Promotions => "promotions",
        MessageCategory.Social => "social",
        MessageCategory.Updates => "updates",
        MessageCategory.Forums => "forums",
        _ => "primary",
    };

    public static bool TryParse(string? id, out MessageCategory category)
    {
        switch (id?.Trim().ToLowerInvariant())
        {
            case "primary": category = MessageCategory.Primary; return true;
            case "promotions": category = MessageCategory.Promotions; return true;
            case "social": category = MessageCategory.Social; return true;
            case "updates": category = MessageCategory.Updates; return true;
            case "forums": category = MessageCategory.Forums; return true;
            default: category = MessageCategory.Primary; return false;
        }
    }
}

/// <summary>Eintrag in categories.json → categories.</summary>
public sealed class CategoryDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string GmailQuery { get; set; } = "";

    [JsonIgnore]
    public MessageCategory Category => MessageCategories.TryParse(Id, out var c) ? c : MessageCategory.Primary;
}

/// <summary>Regelblock einer Kategorie (categories.json → rules.&lt;id&gt;). Fehlende Listen sind leer.</summary>
public sealed class CategoryRuleSet
{
    public List<string> SenderDomains { get; set; } = new();
    public List<string> Headers { get; set; } = new();
    public List<string> SubjectKeywords { get; set; } = new();
    public List<string> SenderLocalParts { get; set; } = new();
    public List<string> Precedence { get; set; } = new();

    /// <summary>Alle Einträge klein schreiben (Vergleiche sind immer case-insensitive).</summary>
    internal CategoryRuleSet Normalized() => new()
    {
        SenderDomains = Lower(SenderDomains),
        Headers = Lower(Headers),
        SubjectKeywords = Lower(SubjectKeywords),
        SenderLocalParts = Lower(SenderLocalParts),
        Precedence = Lower(Precedence),
    };

    private static List<string> Lower(List<string>? list) =>
        (list ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant()).ToList();
}

/// <summary>Wurzelobjekt von categories.json.</summary>
public sealed class CategoryCatalogDocument
{
    public int Version { get; set; }
    public List<CategoryDefinition> Categories { get; set; } = new();
    public Dictionary<string, CategoryRuleSet> Rules { get; set; } = new();
    public List<string> Algorithm { get; set; } = new();
}
