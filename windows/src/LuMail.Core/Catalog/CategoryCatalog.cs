using System.Text.Json;
using LuMail.Core.Models;
using LuMail.Core.Resources;

namespace LuMail.Core.Catalog;

/// <summary>Kategorien und Regeln aus der eingebetteten <c>shared/categories.json</c>.</summary>
public sealed class CategoryCatalog
{
    private static readonly CategoryRuleSet Empty = new();
    private readonly Dictionary<MessageCategory, CategoryRuleSet> _rules = new();

    public IReadOnlyList<CategoryDefinition> Categories { get; }
    public IReadOnlyList<string> Algorithm { get; }

    private CategoryCatalog(CategoryCatalogDocument doc)
    {
        Categories = doc.Categories;
        Algorithm = doc.Algorithm;
        foreach (var (key, set) in doc.Rules)
        {
            if (MessageCategories.TryParse(key, out var category)) _rules[category] = set.Normalized();
        }
    }

    private static readonly Lazy<CategoryCatalog> EmbeddedInstance =
        new(() => Parse(SharedResources.ReadText(SharedResources.CategoriesFile)));

    public static CategoryCatalog LoadEmbedded() => EmbeddedInstance.Value;

    public static CategoryCatalog Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<CategoryCatalogDocument>(json, LuJson.Options)
                  ?? throw new InvalidDataException("categories.json ist leer.");
        return new CategoryCatalog(doc);
    }

    /// <summary>Regeln einer Kategorie (alle Einträge bereits klein geschrieben).</summary>
    public CategoryRuleSet RulesFor(MessageCategory category) => _rules.TryGetValue(category, out var r) ? r : Empty;

    public CategoryDefinition? Find(MessageCategory category) => Categories.FirstOrDefault(c => c.Category == category);

    /// <summary>Deutscher Anzeigename (z. B. "Werbung").</summary>
    public string DisplayName(MessageCategory category) => Find(category)?.Name ?? category.ToId();

    /// <summary>Gmail-Suchausdruck für X-GM-RAW (z. B. "category:promotions").</summary>
    public string GmailQuery(MessageCategory category) =>
        Find(category)?.GmailQuery is { Length: > 0 } q ? q : "category:" + category.ToId();
}
