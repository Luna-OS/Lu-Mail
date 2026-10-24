using LuMail.Core.Catalog;
using LuMail.Core.Models;

namespace LuMail.Core.Classification;

/// <summary>Eingabe für den Klassifizierer.</summary>
/// <param name="FromAddress">Absender-Adresse.</param>
/// <param name="Subject">Betreff.</param>
/// <param name="Headers">Zusätzliche Header (List-Unsubscribe, List-Id, List-Post, Precedence), case-insensitive.</param>
/// <param name="ServerCategory">Gmail: Kategorie vom Server (X-GM-RAW), sonst null.</param>
public sealed record ClassificationInput(
    string? FromAddress,
    string? Subject,
    IReadOnlyDictionary<string, string>? Headers = null,
    MessageCategory? ServerCategory = null);

/// <summary>Nutzer-Regeln „Absender immer als … einsortieren“.</summary>
public interface ISenderRuleLookup
{
    /// <summary>Kategorie für eine Absender-Adresse (Adresse oder Domain) oder null.</summary>
    MessageCategory? Lookup(string? senderAddress);
}

/// <summary>
/// Lokaler Klassifizierer – exakt nach <c>categories.json → algorithm</c> (Schritte 1–8),
/// identisch zur macOS-Implementierung.
/// </summary>
public sealed class CategoryClassifier
{
    private readonly CategoryCatalog _catalog;
    private readonly ISenderRuleLookup? _userRules;

    public CategoryClassifier(CategoryCatalog catalog, ISenderRuleLookup? userRules = null)
    {
        _catalog = catalog;
        _userRules = userRules;
    }

    public MessageCategory Classify(ClassificationInput input)
    {
        var address = (input.FromAddress ?? "").Trim().ToLowerInvariant();
        var domain = EmailParts.GetDomain(address);
        var localPart = EmailParts.GetLocalPart(address);
        var subject = (input.Subject ?? "").ToLowerInvariant();

        // 1. Nutzer-Regel gewinnt immer.
        if (_userRules?.Lookup(address) is { } userCategory) return userCategory;

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
