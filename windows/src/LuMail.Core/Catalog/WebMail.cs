using System.Text.RegularExpressions;
using LuMail.Core.Models;

namespace LuMail.Core.Catalog;

/// <summary>Ein Eintrag in der Seitenleiste eines Web-Kontos.</summary>
public sealed record WebFolderEntry(string Key, string Name, string Url, bool IsCategory);

/// <summary>Reine Hilfsfunktionen für Web-Konten (FUNKTIONEN §20).</summary>
public static partial class WebMail
{
    /// <summary>Reihenfolge der Seitenleiste: Kategorien, dann Rollen.</summary>
    public static readonly (string Key, string Name, bool IsCategory)[] Order =
    {
        ("primary", "Posteingang", true), ("promotions", "Werbung", true), ("social", "Soziale Netzwerke", true),
        ("updates", "Updates", true), ("forums", "Foren", true),
        ("sent", "Gesendet", false), ("drafts", "Entwürfe", false), ("archive", "Archiv", false),
        ("spam", "Spam", false), ("trash", "Papierkorb", false),
    };

    /// <summary>Anmeldung abgeschlossen, wenn die Adresse mit einem <c>loggedInPrefixes</c>-Eintrag beginnt (leer → nie).</summary>
    public static bool IsLoggedIn(WebConfig web, string? url) =>
        !string.IsNullOrEmpty(url) && web.LoggedInPrefixes.Any(p => p.Length > 0 && url.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public static string? FolderUrl(WebConfig web, string key) =>
        web.Folders.TryGetValue(key, out var url) && !string.IsNullOrWhiteSpace(url) ? url : null;

    /// <summary>Vorhandene Ordner in fester Reihenfolge; Kategorien außer Posteingang nur bei getrennten Kategorien.</summary>
    public static IReadOnlyList<WebFolderEntry> SidebarEntries(WebConfig web, bool separateCategories) =>
        Order.Where(o => FolderUrl(web, o.Key) is not null && (!o.IsCategory || o.Key == "primary" || separateCategories))
             .Select(o => new WebFolderEntry(o.Key, o.Name, FolderUrl(web, o.Key)!, o.IsCategory))
             .ToList();

    /// <summary>Ungelesen-Zahl aus dem Seitentitel (erste Gruppe von <c>unreadTitlePattern</c>), sonst null.</summary>
    public static int? UnreadCount(WebConfig web, string? title)
    {
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(web.UnreadTitlePattern)) return null;
        try
        {
            var m = Regex.Match(title, web.UnreadTitlePattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
            return m.Success && m.Groups.Count > 1 && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>Seite ist im Posteingang (Adresse beginnt mit <c>folders.primary</c> oder <c>homeUrl</c>).</summary>
    public static bool IsOnInbox(WebConfig web, string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        var primary = FolderUrl(web, "primary");
        return (primary is not null && url.StartsWith(primary, StringComparison.OrdinalIgnoreCase))
               || (web.HomeUrl.Length > 0 && url.StartsWith(web.HomeUrl, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Erste E-Mail-Adresse im Seitentitel („Inbox (3) - name@gmail.com - Gmail“).</summary>
    public static string? EmailFromTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var m = EmailToken().Match(title);
        return m.Success ? m.Value : null;
    }

    /// <summary>Letzte zwei Host-Teile („mail.google.com“ → „google.com“).</summary>
    public static string? SiteOf(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return null;
        var parts = uri.Host.ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? string.Join('.', parts) : string.Join('.', parts[^2..]);
    }

    /// <summary>Gehört die Adresse zum Anbieter (gleiche letzte zwei Host-Teile wie eine der web-Adressen)?</summary>
    public static bool IsInternalHost(WebConfig web, string? url)
    {
        var site = SiteOf(url);
        if (site is null) return false;
        var known = new[] { web.LoginUrl, web.HomeUrl }.Concat(web.LoggedInPrefixes).Concat(web.Folders.Values)
            .Select(SiteOf).Where(s => s is not null);
        return known.Contains(site);
    }

    /// <summary>Weiterleitungs-Links wie „https://www.google.com/url?q=https://ziel.de“ → „https://ziel.de“.</summary>
    public static string? RedirectTarget(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/url") return null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            if (index <= 0) continue;
            var name = pair[..index];
            if (name != "q" && name != "url") continue;
            var target = Uri.UnescapeDataString(pair[(index + 1)..].Replace('+', ' '));
            if (target.StartsWith("http://", StringComparison.Ordinal) || target.StartsWith("https://", StringComparison.Ordinal))
                return target;
        }
        return null;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailToken();
}
