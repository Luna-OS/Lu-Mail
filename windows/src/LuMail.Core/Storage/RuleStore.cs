using System.Text.Json;
using System.Text.Json.Serialization;
using LuMail.Core.Catalog;
using LuMail.Core.Classification;
using LuMail.Core.Models;
using LuMail.Core.Resources;

namespace LuMail.Core.Storage;

/// <summary>Inhaltsregel: <c>{ "field": "from|to|subject", "contains": "…", "category": "…" }</c>.</summary>
public sealed class ContentRule
{
    public string Field { get; set; } = "subject";
    public string Contains { get; set; } = "";
    public string Category { get; set; } = "primary";
}

/// <summary>Eigene Kategorie: <c>{ "id": "custom-…", "name", "color", "icon" }</c>.</summary>
public sealed class CustomCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#22C55E";
    public string Icon { get; set; } = "tag";
}

/// <summary>Wurzelobjekt von rules.json (Version 2).</summary>
public sealed class RulesDocument
{
    public int Version { get; set; } = 2;
    public Dictionary<string, string> Senders { get; set; } = new();
    public List<ContentRule> Rules { get; set; } = new();
    public List<CustomCategory> CustomCategories { get; set; } = new();
}

/// <summary>
/// Eigene Regeln und Kategorien (<c>rules.json</c>, Version 2; ARCHITEKTUR/FUNKTIONEN §10).
/// Die alte flache Form <c>{ "&lt;absender&gt;": "&lt;kategorie&gt;" }</c> wird als <c>senders</c> gelesen.
/// </summary>
public sealed class RuleStore : ICategoryRuleSource
{
    private readonly string? _path;
    private readonly object _gate = new();
    private RulesDocument _doc;

    public RuleStore(string? path)
    {
        _path = path;
        _doc = path is not null && File.Exists(path) ? Parse(SafeRead(path)) : new RulesDocument();
    }

    /// <summary>Nur im Speicher (Tests).</summary>
    public static RuleStore InMemory() => new(null);

    public event EventHandler? Changed;

    public IReadOnlyDictionary<string, string> Senders
    {
        get { lock (_gate) return new Dictionary<string, string>(_doc.Senders, StringComparer.OrdinalIgnoreCase); }
    }

    public IReadOnlyList<ContentRule> Rules
    {
        get { lock (_gate) return _doc.Rules.Select(Clone).ToList(); }
    }

    public IReadOnlyList<CustomCategory> CustomCategories
    {
        get { lock (_gate) return _doc.CustomCategories.Select(c => new CustomCategory { Id = c.Id, Name = c.Name, Color = c.Color, Icon = c.Icon }).ToList(); }
    }

    // ------------------------------------------------------------- Absender

    public void SetSender(string senderAddressOrDomain, string categoryId) =>
        Mutate(d =>
        {
            var key = Normalize(senderAddressOrDomain);
            if (key.Length == 0) return false;
            d.Senders[key] = categoryId;
            return true;
        });

    public void SetSender(string senderAddressOrDomain, MessageCategory category) => SetSender(senderAddressOrDomain, category.ToId());

    public bool RemoveSender(string senderAddressOrDomain) =>
        Mutate(d => d.Senders.Remove(Normalize(senderAddressOrDomain)));

    /// <summary>Exakte Adresse zuerst, dann Domain und übergeordnete Domains (z. B. mail.shop.de → shop.de).</summary>
    public string? LookupSender(string? senderAddress)
    {
        var address = Normalize(senderAddress);
        if (address.Length == 0) return null;
        lock (_gate)
        {
            if (_doc.Senders.Count == 0) return null;
            if (_doc.Senders.TryGetValue(address, out var exact)) return exact;
            var domain = EmailParts.GetDomain(address);
            while (domain.Contains('.'))
            {
                if (_doc.Senders.TryGetValue(domain, out var byDomain)) return byDomain;
                domain = domain[(domain.IndexOf('.') + 1)..];
            }
            return null;
        }
    }

    // --------------------------------------------------------------- Regeln

    public void AddRule(ContentRule rule) => Mutate(d =>
    {
        if (string.IsNullOrWhiteSpace(rule.Contains)) return false;
        d.Rules.Add(new ContentRule { Field = NormalizeField(rule.Field), Contains = rule.Contains.Trim(), Category = rule.Category });
        return true;
    });

    public bool RemoveRuleAt(int index) => Mutate(d =>
    {
        if (index < 0 || index >= d.Rules.Count) return false;
        d.Rules.RemoveAt(index);
        return true;
    });

    /// <summary>Erste passende Regel (Teilstring, ohne Groß-/Kleinschreibung).</summary>
    public string? MatchRule(string? fromName, string? fromAddress, IEnumerable<string>? to, string? subject)
    {
        lock (_gate)
        {
            foreach (var rule in _doc.Rules)
            {
                var needle = rule.Contains.Trim();
                if (needle.Length == 0) continue;
                var hit = NormalizeField(rule.Field) switch
                {
                    "from" => Has(fromName, needle) || Has(fromAddress, needle),
                    "to" => to?.Any(t => Has(t, needle)) == true,
                    _ => Has(subject, needle),
                };
                if (hit) return rule.Category;
            }
            return null;
        }
    }

    private static bool Has(string? text, string needle) =>
        !string.IsNullOrEmpty(text) && text.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeField(string? field) => (field ?? "").Trim().ToLowerInvariant() switch
    {
        "from" => "from",
        "to" => "to",
        _ => "subject",
    };

    // ------------------------------------------------------ Eigene Kategorien

    /// <summary>Legt eine eigene Kategorie an; Id = "custom-" + Name (klein, Bindestriche).</summary>
    public CustomCategory AddCategory(string name, string color, string icon = "tag")
    {
        var category = new CustomCategory { Name = name.Trim(), Color = color, Icon = icon };
        Mutate(d =>
        {
            var baseId = "custom-" + Slug(category.Name);
            var id = baseId;
            for (var i = 2; d.CustomCategories.Any(c => c.Id == id); i++) id = $"{baseId}-{i}";
            category.Id = id;
            d.CustomCategories.Add(category);
            return true;
        });
        return category;
    }

    /// <summary>Entfernt eine eigene Kategorie samt Regeln/Absendern, die auf sie zeigen.</summary>
    public bool RemoveCategory(string id) => Mutate(d =>
    {
        var removed = d.CustomCategories.RemoveAll(c => c.Id == id) > 0;
        if (!removed) return false;
        d.Rules.RemoveAll(r => r.Category == id);
        foreach (var key in d.Senders.Where(s => s.Value == id).Select(s => s.Key).ToList()) d.Senders.Remove(key);
        return true;
    });

    public CustomCategory? FindCategory(string? id)
    {
        lock (_gate) return _doc.CustomCategories.FirstOrDefault(c => c.Id == id);
    }

    internal static string Slug(string name)
    {
        var map = new Dictionary<char, string> { ['ä'] = "ae", ['ö'] = "oe", ['ü'] = "ue", ['ß'] = "ss" };
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (map.TryGetValue(ch, out var rep)) sb.Append(rep);
            else if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "kategorie" : slug;
    }

    // ------------------------------------------------------------ Speichern

    private bool Mutate(Func<RulesDocument, bool> change)
    {
        bool changed;
        lock (_gate)
        {
            changed = change(_doc);
            if (changed) Persist();
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    private static ContentRule Clone(ContentRule r) => new() { Field = r.Field, Contains = r.Contains, Category = r.Category };

    private static string Normalize(string? s) => (s ?? "").Trim().Trim('<', '>').ToLowerInvariant();

    private void Persist()
    {
        if (_path is null) return;
        var doc = new RulesDocument
        {
            Version = 2,
            Senders = _doc.Senders.OrderBy(s => s.Key, StringComparer.Ordinal).ToDictionary(s => s.Key, s => s.Value),
            Rules = _doc.Rules,
            CustomCategories = _doc.CustomCategories,
        };
        JsonFile.WriteAtomic(_path, JsonSerializer.Serialize(doc, LuJson.Options));
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return "{}"; }
    }

    /// <summary>Liest Version 2 sowie die alte flache Form (und Arrays/„rules“-Objekte älterer Entwürfe) als senders.</summary>
    internal static RulesDocument Parse(string json)
    {
        var doc = new RulesDocument();
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = parsed.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                ReadSenderArray(root, doc);
                return doc;
            }
            if (root.ValueKind != JsonValueKind.Object) return doc;

            var isV2 = root.TryGetProperty("version", out _) || root.TryGetProperty("senders", out _)
                       || root.TryGetProperty("customCategories", out _)
                       || (root.TryGetProperty("rules", out var r) && r.ValueKind == JsonValueKind.Array);
            if (!isV2)
            {
                // Alte Form: { "absender": "kategorie" } bzw. { "rules": { … } }
                var flat = root.TryGetProperty("rules", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : root;
                foreach (var p in flat.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) doc.Senders[Normalize(p.Name)] = p.Value.GetString()!.Trim().ToLowerInvariant();
                return doc;
            }

            if (root.TryGetProperty("senders", out var senders) && senders.ValueKind == JsonValueKind.Object)
                foreach (var p in senders.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) doc.Senders[Normalize(p.Name)] = p.Value.GetString()!.Trim();
            if (root.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
                doc.Rules = rules.Deserialize<List<ContentRule>>(LuJson.Options)?.Where(x => !string.IsNullOrWhiteSpace(x.Contains)).ToList() ?? new();
            if (root.TryGetProperty("customCategories", out var cats) && cats.ValueKind == JsonValueKind.Array)
                doc.CustomCategories = cats.Deserialize<List<CustomCategory>>(LuJson.Options)?.Where(c => !string.IsNullOrWhiteSpace(c.Id)).ToList() ?? new();
        }
        catch (JsonException)
        {
            // Defekte Datei: leere Regeln.
        }
        doc.Senders = new Dictionary<string, string>(doc.Senders, StringComparer.OrdinalIgnoreCase);
        return doc;
    }

    private static void ReadSenderArray(JsonElement root, RulesDocument doc)
    {
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string? sender = null, category = null;
            foreach (var p in item.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String) continue;
                var name = p.Name.ToLowerInvariant();
                if (name is "sender" or "address" or "email" or "domain" or "key" or "pattern") sender = p.Value.GetString();
                else if (name is "category" or "categoryid") category = p.Value.GetString();
            }
            if (sender is not null && category is not null) doc.Senders[Normalize(sender)] = category;
        }
    }
}
