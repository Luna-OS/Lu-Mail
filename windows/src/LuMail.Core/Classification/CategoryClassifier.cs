using LuMail.Core.Catalog;
using LuMail.Core.Models;

namespace LuMail.Core.Classification;

/// <summary>Eingabe für den Klassifizierer.</summary>
/// <param name="FromAddress">Absender-Adresse.</param>
/// <param name="Subject">Betreff.</param>
/// <param name="Headers">Zusätzliche Header (List-Unsubscribe, List-Id, List-Post, Precedence), case-insensitive.</param>
/// <param name="ServerCategory">Gmail: Kategorie vom Server (X-GM-RAW), sonst null.</param>
/// <param name="FromName">Anzeigename des Absenders (für Regeln mit field = from).</param>
/// <param name="To">Alle Empfänger (Adressen und Namen; für Regeln mit field = to).</param>
public sealed record ClassificationInput(
    string? FromAddress,
    string? Subject,
    IReadOnlyDictionary<string, string>? Headers = null,
    MessageCategory? ServerCategory = null,
    string? FromName = null,
    IReadOnlyList<string>? To = null);

/// <summary>Eigene Regeln aus rules.json (senders und rules).</summary>
public interface ICategoryRuleSource
{
    /// <summary>Kategorie-Id für eine Absender-Adresse (Adresse oder Domain) oder null.</summary>
    string? LookupSender(string? senderAddress);

    /// <summary>Kategorie-Id der ersten passenden Inhaltsregel oder null.</summary>
    string? MatchRule(string? fromName, string? fromAddress, IEnumerable<string>? to, string? subject);
}

/// <summary>
/// Klassifizierer: <b>senders</b> → <b>rules</b> (erste passende) → Gmail-Server-Kategorie →
/// lokale Regeln exakt nach <c>categories.json → algorithm</c> (Schritte 3–8), identisch zur macOS-Implementierung.
/// Liefert eine Kategorie-Id ("primary", …, oder eine eigene "custom-…").
/// </summary>
public sealed class CategoryClassifier
{
    private readonly CategoryCatalog _catalog;
    private readonly ICategoryRuleSource? _userRules;

    public CategoryClassifier(CategoryCatalog catalog, ICategoryRuleSource? userRules = null)
    {
        _catalog = catalog;
        _userRules = userRules;
    }

    public string Classify(ClassificationInput input)
    {
        // senders (Nutzer-Regel pro Absender) gewinnt immer.
        if (_userRules?.LookupSender(input.FromAddress) is { Length: > 0 } sender) return sender;
        // rules: erste passende Inhaltsregel.
        if (_userRules?.MatchRule(input.FromName, input.FromAddress, input.To, input.Subject) is { Length: > 0 } rule) return rule;
        return ClassifyBuiltIn(input).ToId();
    }

    /// <summary>Ohne eigene Regeln: Gmail-Server-Kategorie, sonst categories.json Schritte 3–8.</summary>
    public MessageCategory ClassifyBuiltIn(ClassificationInput input)
    {
        var address = (input.FromAddress ?? "").Trim().ToLowerInvariant();
        var domain = EmailParts.GetDomain(address);
        var localPart = EmailParts.GetLocalPart(address);
        var subject = (input.Subject ?? "").ToLowerInvariant();

        // 2. Gmail: Kategorie vom Server. Nur wenn keine geliefert wird, geht es lokal weiter.
        if (input.ServerCategory is { } serverCategory) return serverCategory;

        var social = _catalog.RulesFor(MessageCategory.Social);
        var forums = _catalog.RulesFor(MessageCategory.Forums);
        var updates = _catalog.RulesFor(MessageCategory.Updates);
        var promotions = _catalog.RulesFor(MessageCategory.Promotions);

        // 3. social: Absender-Domain ist (Sub-)Domain von rules.social.senderDomains.
        if (DomainIn(domain, social.SenderDomains)) return MessageCategory.Social;

        // 4. forums: Domain passt ODER einer der rules.forums.headers ist vorhanden.
        if (DomainIn(domain, forums.SenderDomains) || AnyHeaderPresent(input.Headers, forums.Headers))
            return MessageCategory.Forums;

        // 5. updates: Betreff enthält eines der rules.updates.subjectKeywords.
        if (ContainsAny(subject, updates.SubjectKeywords)) return MessageCategory.Updates;

        // 6. promotions: Header vorhanden ODER Precedence passt ODER lokaler Teil passt ODER Betreff-Stichwort.
        if (AnyHeaderPresent(input.Headers, promotions.Headers)
            || PrecedenceIn(input.Headers, promotions.Precedence)
            || (localPart.Length > 0 && promotions.SenderLocalParts.Contains(localPart))
            || ContainsAny(subject, promotions.SubjectKeywords))
            return MessageCategory.Promotions;

        // 7. updates: lokaler Teil der Absender-Adresse ist in rules.updates.senderLocalParts.
        if (localPart.Length > 0 && updates.SenderLocalParts.Contains(localPart)) return MessageCategory.Updates;

        // 8. Sonst: primary.
        return MessageCategory.Primary;
    }

    private static bool DomainIn(string domain, List<string> domains) =>
        domain.Length > 0 && domains.Any(d => EmailParts.DomainMatches(domain, d));

    private static bool ContainsAny(string lowerSubject, List<string> keywords) =>
        lowerSubject.Length > 0 && keywords.Any(k => lowerSubject.Contains(k, StringComparison.Ordinal));

    private static bool AnyHeaderPresent(IReadOnlyDictionary<string, string>? headers, List<string> names)
    {
        if (headers is null || headers.Count == 0) return false;
        foreach (var name in names)
        {
            foreach (var (key, value) in headers)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                    return true;
            }
        }
        return false;
    }

    private static bool PrecedenceIn(IReadOnlyDictionary<string, string>? headers, List<string> values)
    {
        if (headers is null) return false;
        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, "Precedence", StringComparison.OrdinalIgnoreCase)
                && values.Contains((value ?? "").Trim().ToLowerInvariant()))
                return true;
        }
        return false;
    }
}
