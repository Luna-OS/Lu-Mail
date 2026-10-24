using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuMail.Core.Catalog;
using LuMail.Core.Resources;

namespace LuMail.Core.Mail;

/// <summary>Ergebnis der Tracker-Erkennung.</summary>
public sealed record TrackerReport(int Count, IReadOnlyList<string> Domains)
{
    public static TrackerReport None { get; } = new(0, Array.Empty<string>());

    /// <summary>„3 Tracker blockiert (list-manage.com, …)“ bzw. leer.</summary>
    public string Describe()
    {
        if (Count == 0) return "";
        var domains = Domains.Take(3).ToList();
        var list = domains.Count == 0 ? "" : " (" + string.Join(", ", domains) + (Domains.Count > 3 ? ", …" : "") + ")";
        return $"{Count} Tracker blockiert{list}";
    }
}

/// <summary>
/// Tracking-Bericht (FUNKTIONEN §14): <c>&lt;img&gt;</c> mit width/height ≤ 1 oder Host (bzw. Elternzone)
/// in <c>shared/trackers.json → domains</c>.
/// </summary>
public sealed partial class TrackerDetector
{
    private readonly List<string> _domains;

    public TrackerDetector(IEnumerable<string> domains) =>
        _domains = domains.Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length > 0).ToList();

    private static readonly Lazy<TrackerDetector> EmbeddedInstance = new(() =>
    {
        using var doc = JsonDocument.Parse(SharedResources.ReadText("trackers.json"));
        return new TrackerDetector(doc.RootElement.GetProperty("domains").EnumerateArray().Select(e => e.GetString() ?? ""));
    });

    public static TrackerDetector LoadEmbedded() => EmbeddedInstance.Value;

    public TrackerReport Detect(string? html)
    {
        if (string.IsNullOrEmpty(html)) return TrackerReport.None;
        var count = 0;
        var domains = new List<string>();
        foreach (Match img in ImgTag().Matches(html))
        {
            var tag = img.Value;
            var src = WebUtility.HtmlDecode(Attr(tag, "src") ?? "");
            // Nur entfernte Bilder können verfolgen (data:/cid: nicht).
            if (!Uri.TryCreate(src, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) continue;
            var tiny = IsTiny(Attr(tag, "width")) || IsTiny(Attr(tag, "height")) || TinyStyle().IsMatch(Attr(tag, "style") ?? "");
            var listed = Match(uri);
            if (listed is null && !tiny) continue;
            count++;
            var name = listed ?? uri.Host.ToLowerInvariant();
            if (name.Length > 0 && !domains.Contains(name)) domains.Add(name);
        }
        return new TrackerReport(count, domains);
    }

    /// <summary>Passender Listeneintrag (Domain oder Elternzone; Einträge mit Pfad wie "facebook.com/tr" als Präfix).</summary>
    private string? Match(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        foreach (var d in _domains)
        {
            if (d.Contains('/'))
            {
                var domainPart = d[..d.IndexOf('/')];
                var pathPart = d[d.IndexOf('/')..];
                if (EmailParts.DomainMatches(host, domainPart) && uri.AbsolutePath.StartsWith(pathPart, StringComparison.OrdinalIgnoreCase)) return d;
            }
            else if (EmailParts.DomainMatches(host, d)) return d;
        }
        return null;
    }

    private static bool IsTiny(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim().ToLowerInvariant().Replace("px", "");
        return double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && n <= 1;
    }

    internal static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, @"\b" + name + @"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["v"].Value : null;
    }

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTag();

    [GeneratedRegex(@"(?:^|;)\s*(?:width|height)\s*:\s*[01](?:\.0+)?(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TinyStyle();
}

/// <summary>Marke aus <c>shared/phishing.json</c>.</summary>
public sealed record PhishingBrand(string Name, IReadOnlyList<string> Keywords, IReadOnlyList<string> Domains);

/// <summary>
/// Phishing-Warnung (FUNKTIONEN §14): Anzeigename mit fremder Adresse, Marke im Anzeigenamen ohne passende Domain,
/// Link-Text der wie eine Adresse aussieht, aber auf eine andere Domain zeigt.
/// </summary>
public sealed partial class PhishingDetector
{
    private readonly IReadOnlyList<PhishingBrand> _brands;

    public PhishingDetector(IReadOnlyList<PhishingBrand> brands) => _brands = brands;

    private static readonly Lazy<PhishingDetector> EmbeddedInstance = new(() =>
    {
        using var doc = JsonDocument.Parse(SharedResources.ReadText("phishing.json"));
        var brands = doc.RootElement.GetProperty("brands").EnumerateArray().Select(b => new PhishingBrand(
            b.GetProperty("name").GetString() ?? "",
            b.GetProperty("keywords").EnumerateArray().Select(k => (k.GetString() ?? "").ToLowerInvariant()).ToList(),
            b.GetProperty("domains").EnumerateArray().Select(k => (k.GetString() ?? "").ToLowerInvariant()).ToList())).ToList();
        return new PhishingDetector(brands);
    });

    public static PhishingDetector LoadEmbedded() => EmbeddedInstance.Value;

    /// <summary>Liefert die deutschen Warnungen (leer = unauffällig).</summary>
    public IReadOnlyList<string> Check(string? fromName, string? fromAddress, string? html)
    {
        var warnings = new List<string>();
        var senderDomain = EmailParts.GetDomain(fromAddress);
        var name = (fromName ?? "").Trim();

        // 1. Anzeigename enthält eine E-Mail-Adresse mit anderer Domain
        foreach (Match m in EmailInText().Matches(name))
        {
            var d = EmailParts.GetDomain(m.Value);
            if (d.Length > 0 && senderDomain.Length > 0 && d != senderDomain)
            {
                warnings.Add($"Der Anzeigename nennt die Adresse {m.Value}, gesendet wurde aber von {fromAddress}.");
                break;
            }
        }

        // 2. Marke im Anzeigenamen, Domain gehört nicht dazu
        if (senderDomain.Length > 0)
        {
            var lowerName = name.ToLowerInvariant();
            foreach (var brand in _brands)
            {
                if (!brand.Keywords.Any(k => k.Length > 0 && ContainsWord(lowerName, k))) continue;
                if (brand.Domains.Any(d => EmailParts.DomainMatches(senderDomain, d))) continue;
                warnings.Add($"Der Absender nennt sich „{name}“, die Adresse gehört aber nicht zu {brand.Name} ({senderDomain}).");
                break;
            }
        }

        // 3. Link-Text sieht wie eine Adresse aus, zeigt aber woanders hin
        if (!string.IsNullOrEmpty(html))
        {
            foreach (Match a in Anchor().Matches(html))
            {
                var href = WebUtility.HtmlDecode(a.Groups["href"].Value).Trim();
                var text = WebUtility.HtmlDecode(Tag().Replace(a.Groups["text"].Value, "")).Trim();
                if (!LooksLikeUrl().IsMatch(text)) continue;
                var shown = HostOf(text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + text : text);
                var target = HostOf(href);
                if (shown is null || target is null) continue;
                if (SameSite(shown, target)) continue;
                warnings.Add($"Ein Link zeigt „{text}“ an, führt aber zu {target}.");
                break;
            }
        }
        return warnings;
    }

    private static bool ContainsWord(string haystack, string keyword)
    {
        var idx = haystack.IndexOf(keyword, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var before = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
            var end = idx + keyword.Length;
            var after = end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (before && after) return true;
            idx = haystack.IndexOf(keyword, idx + 1, StringComparison.Ordinal);
        }
        return false;
    }

    private static string? HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? u.Host.ToLowerInvariant().TrimStart('.')
            : null;

    /// <summary>Gleiche Domain oder eine ist Subdomain der anderen („www.“ ignoriert).</summary>
    private static bool SameSite(string a, string b)
    {
        a = a.StartsWith("www.", StringComparison.Ordinal) ? a[4..] : a;
        b = b.StartsWith("www.", StringComparison.Ordinal) ? b[4..] : b;
        return EmailParts.DomainMatches(a, b) || EmailParts.DomainMatches(b, a);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailInText();

    [GeneratedRegex(@"<a\b[^>]*\bhref\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<text>.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^(https?://|www\.)[^\s]+\.[^\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeUrl();
}
