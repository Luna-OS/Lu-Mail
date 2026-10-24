using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LuMail.Core.Auth;
using LuMail.Core.Models;
using MailKit.Search;

namespace LuMail.Core.Mail;

// ---------------------------------------------------------------- §11 Abbestellen

/// <summary>Auswertung von List-Unsubscribe / List-Unsubscribe-Post (RFC 2369, RFC 8058).</summary>
public sealed partial class UnsubscribeInfo
{
    public Uri? HttpsUrl { get; init; }
    public string? MailtoAddress { get; init; }
    public string MailtoSubject { get; init; } = "unsubscribe";
    /// <summary>https-Adresse und <c>List-Unsubscribe-Post: List-Unsubscribe=One-Click</c>.</summary>
    public bool OneClick { get; init; }

    public bool IsAvailable => HttpsUrl is not null || MailtoAddress is not null;

    /// <summary>Gewählter Weg in der Reihenfolge der Spezifikation.</summary>
    public UnsubscribeMethod Method =>
        OneClick && HttpsUrl is not null ? UnsubscribeMethod.OneClickPost
        : MailtoAddress is not null ? UnsubscribeMethod.Mailto
        : HttpsUrl is not null ? UnsubscribeMethod.Browser
        : UnsubscribeMethod.None;

    public static UnsubscribeInfo? Parse(string? listUnsubscribe, string? listUnsubscribePost)
    {
        if (string.IsNullOrWhiteSpace(listUnsubscribe)) return null;
        Uri? https = null;
        string? mailto = null;
        var subject = "unsubscribe";
        foreach (Match m in Bracketed().Matches(listUnsubscribe))
        {
            var value = m.Groups[1].Value.Trim();
            if (https is null && value.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
            {
                https = u;
            }
            else if (mailto is null && value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = value[7..];
                var q = rest.IndexOf('?');
                var address = Uri.UnescapeDataString(q < 0 ? rest : rest[..q]).Trim();
                if (address.Contains('@'))
                {
                    mailto = address;
                    if (q >= 0 && OAuthUrls.ParseQuery(rest[(q + 1)..]).FirstOrDefault(p => p.Key.Equals("subject", StringComparison.OrdinalIgnoreCase)) is { Value: { Length: > 0 } s })
                        subject = s;
                }
            }
        }
        var oneClick = https is not null && (listUnsubscribePost ?? "").Replace(" ", "")
            .Equals("List-Unsubscribe=One-Click", StringComparison.OrdinalIgnoreCase);
        var info = new UnsubscribeInfo { HttpsUrl = https, MailtoAddress = mailto, MailtoSubject = subject, OneClick = oneClick };
        return info.IsAvailable ? info : null;
    }

    [GeneratedRegex(@"<([^>]+)>")]
    private static partial Regex Bracketed();
}

public enum UnsubscribeMethod { None, OneClickPost, Mailto, Browser }

/// <summary>RFC-8058-One-Click: POST mit Body <c>List-Unsubscribe=One-Click</c>, ohne Cookies.</summary>
public static class OneClickUnsubscribe
{
    public const string Body = "List-Unsubscribe=One-Click";

    /// <summary>Erzeugt einen passenden HttpClient (keine Cookies, keine Weiterleitungen auf andere Schemata).</summary>
    public static HttpClient CreateClient() => new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static async Task<bool> PostAsync(HttpClient http, Uri url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(Body, Encoding.ASCII, "application/x-www-form-urlencoded"),
        };
        request.Content.Headers.ContentType!.CharSet = null; // exakt application/x-www-form-urlencoded
        request.Headers.UserAgent.ParseAdd("Lu-Mail");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
}

// ------------------------------------------------------------------ §12 Aufräumen

/// <summary>Auswahl für „Werbung automatisch aufräumen“.</summary>
public static class CleanupSelector
{
    public const int MaxPerRun = 100;

    /// <summary>Erlaubte Fristen in Tagen (0 = aus).</summary>
    public static readonly int[] AllowedDays = { 0, 7, 30, 90 };

    /// <summary>Nachrichten der Kategorien mit Frist, älter als die Frist und nicht markiert – höchstens 100, älteste zuerst.</summary>
    public static List<MessageSummary> Select(IEnumerable<MessageSummary> messages, IReadOnlyDictionary<string, int> daysByCategory, DateTimeOffset now)
    {
        return messages
            .Where(m => !m.Flagged && m.CategoryId is { } c && daysByCategory.TryGetValue(c, out var days) && days > 0
                        && m.Date < now.AddDays(-days))
            .OrderBy(m => m.Date)
            .Take(MaxPerRun)
            .ToList();
    }
}

// --------------------------------------------------------------- §4 Serversuche

public static class ServerSearch
{
    public const int MaxResultsPerAccount = 50;

    /// <summary>Gmail: <c>X-GM-RAW "&lt;text&gt;"</c>, sonst <c>OR OR FROM SUBJECT BODY</c> (MailKit sendet CHARSET UTF-8 und Literale).</summary>
    public static SearchQuery BuildQuery(string text, bool gmail)
    {
        var t = text.Trim();
        return gmail
            ? SearchQuery.GMailRawSearch(t)
            : SearchQuery.FromContains(t).Or(SearchQuery.SubjectContains(t)).Or(SearchQuery.BodyContains(t));
    }

    /// <summary>Lesbare Form des IMAP-Suchkriteriums (Dokumentation/Tests).</summary>
    public static string DescribeCriteria(string text, bool gmail)
    {
        var q = Quote(text.Trim());
        return gmail ? $"X-GM-RAW {q}" : $"CHARSET UTF-8 OR OR FROM {q} SUBJECT {q} BODY {q}";
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

// ------------------------------------------------------------- §7 STATUS-Zähler

public static class FolderStatusSelection
{
    /// <summary>STATUS (UNSEEN) für alle auswählbaren Ordner außer Gesendet, Entwürfe, Papierkorb und Archiv.</summary>
    public static bool ShouldQuery(FolderRole role, bool noSelect) =>
        !noSelect && role is not (FolderRole.Sent or FolderRole.Drafts or FolderRole.Trash or FolderRole.Archive);
}

// ------------------------------------------------------------ §13 Erinnerungen

/// <summary>Vorgaben für „Später erinnern“.</summary>
public static class SnoozePresets
{
    public sealed record Preset(string Label, DateTimeOffset Until);

    /// <summary>Heute 18:00 (nur wenn noch nicht vorbei), Morgen 08:00, Montag 08:00 (nächster Montag nach heute).</summary>
    public static IReadOnlyList<Preset> For(DateTimeOffset nowLocal)
    {
        var list = new List<Preset>();
        var today = nowLocal.Date;
        DateTimeOffset At(DateTime day, int hour) => new(day.AddHours(hour), nowLocal.Offset);
        var evening = At(today, 18);
        if (evening > nowLocal) list.Add(new Preset("Heute 18:00", evening));
        list.Add(new Preset("Morgen 08:00", At(today.AddDays(1), 8)));
        var daysToMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysToMonday == 0) daysToMonday = 7;
        list.Add(new Preset("Montag 08:00", At(today.AddDays(daysToMonday), 8)));
        return list;
    }

    public static string Format(DateTimeOffset when) =>
        when.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}
