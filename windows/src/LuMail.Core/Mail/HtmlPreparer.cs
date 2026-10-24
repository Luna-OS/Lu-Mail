using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LuMail.Core.Login;
using LuMail.Core.Models;

namespace LuMail.Core.Mail;

/// <summary>Farben für die Darstellung von Klartext-Mails.</summary>
public sealed record PlainTextStyle(string Background, string Foreground, string Link)
{
    public static PlainTextStyle Light { get; } = new("#FFFFFF", "#1B1A33", "#6B5BEF");
    public static PlainTextStyle Dark { get; } = new("#1A1C3A", "#ECEBFF", "#8B7CFF");
}

/// <summary>
/// Bereitet Nachrichten für die Web-Ansicht vor (ARCHITEKTUR.md „E-Mail-Anzeige &amp; Datenschutz“):
/// vorangestellte CSP blockiert externe Inhalte, bis der Nutzer „Externe Inhalte laden“ wählt.
/// </summary>
public static partial class HtmlPreparer
{
    /// <summary>Standard-CSP: keine externen Bilder/Tracking-Pixel, kein Skript.</summary>
    public const string BlockedCsp = "default-src 'none'; img-src data: cid:; style-src 'unsafe-inline'";

    /// <summary>CSP nach „Externe Inhalte laden“: Bilder, Styles, Fonts per http(s) erlaubt, Skripte weiterhin nicht.</summary>
    public const string RelaxedCsp =
        "default-src 'none'; img-src data: cid: http: https:; style-src 'unsafe-inline' http: https:; font-src data: http: https:; media-src data: http: https:";

    /// <summary>Erzeugt das anzuzeigende HTML-Dokument.</summary>
    public static string Prepare(MessageDetail message, bool allowExternalContent, PlainTextStyle? plainStyle = null) =>
        Prepare(message.HtmlBody, message.TextBody, allowExternalContent, message.InlineResources, plainStyle);

    public static string Prepare(string? html, string? text, bool allowExternalContent,
        IReadOnlyDictionary<string, InlineResource>? inline = null, PlainTextStyle? plainStyle = null)
    {
        var csp = allowExternalContent ? RelaxedCsp : BlockedCsp;
        string document;
        if (!string.IsNullOrWhiteSpace(html))
        {
            document = inline is { Count: > 0 } ? ReplaceCidReferences(html, inline) : html;
        }
        else
        {
            document = PlainTextToHtml(text ?? "", plainStyle ?? PlainTextStyle.Light);
        }
        return InjectCsp(document, csp);
    }

    /// <summary>
    /// Stellt <c>&lt;meta http-equiv="Content-Security-Policy"&gt;</c> (und no-referrer) an den Dokumentanfang –
    /// direkt nach einem führenden DOCTYPE, sonst ganz vorn. Der HTML-Parser legt das meta-Element dann
    /// zwingend in den &lt;head&gt;, bevor irgendein Inhalt der Mail gelesen wird (keine Umgehung über Kommentare o. Ä.).
    /// </summary>
    public static string InjectCsp(string html, string csp)
    {
        var meta = "<meta http-equiv=\"Content-Security-Policy\" content=\"" + Html.Escape(csp) + "\">"
                   + "<meta name=\"referrer\" content=\"no-referrer\">";
        var doctype = LeadingDoctype().Match(html);
        return doctype.Success
            ? html[..doctype.Length] + meta + html[doctype.Length..]
            : meta + html;
    }

    /// <summary>
    /// Konversation: alle Nachrichten untereinander (älteste zuerst), ältere in <c>&lt;details&gt;</c> eingeklappt –
    /// funktioniert ohne JavaScript. Ein Dokument mit einer CSP.
    /// </summary>
    public static string PrepareConversation(IReadOnlyList<MessageDetail> messages, bool allowExternalContent, PlainTextStyle? style = null)
    {
        var st = style ?? PlainTextStyle.Light;
        var sb = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><style>")
            .Append($"html,body{{background:{st.Background};color:{st.Foreground};}}")
            .Append("body{margin:12px;font:14px/1.5 'Segoe UI Variable','Segoe UI',-apple-system,system-ui,sans-serif;}")
            .Append($"a{{color:{st.Link};}}")
            .Append("details{border:1px solid rgba(128,128,128,.35);border-radius:10px;margin:0 0 10px;padding:0 12px;}")
            .Append("summary{cursor:pointer;padding:10px 0;font-weight:600;}summary span{font-weight:400;opacity:.7;margin-left:8px;}")
            .Append(".body{padding:0 0 12px;overflow-x:auto;}pre{white-space:pre-wrap;word-wrap:break-word;font:inherit;margin:0;}")
            .Append("</style></head><body>");
        for (var i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            var open = i == messages.Count - 1 ? " open" : "";
            var date = m.Date.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            sb.Append("<details").Append(open).Append("><summary>").Append(Html.Escape(m.From?.DisplayName ?? "Unbekannt"))
              .Append("<span>").Append(Html.Escape(date)).Append("</span></summary><div class=\"body\">");
            if (!string.IsNullOrWhiteSpace(m.HtmlBody))
            {
                var body = m.InlineResources.Count > 0 ? ReplaceCidReferences(m.HtmlBody!, m.InlineResources) : m.HtmlBody!;
                sb.Append(OuterDocumentTags().Replace(LeadingDoctype().Replace(body, ""), ""));
            }
            else
            {
                sb.Append("<pre>").Append(Html.Escape(m.TextBody ?? "")).Append("</pre>");
            }
            sb.Append("</div></details>");
        }
        sb.Append("</body></html>");
        return InjectCsp(sb.ToString(), allowExternalContent ? RelaxedCsp : BlockedCsp);
    }

    /// <summary>Klartext → escaptes HTML mit klickbaren Links.</summary>
    public static string PlainTextToHtml(string text, PlainTextStyle style)
    {
        var escaped = Html.Escape(text.Replace("\r\n", "\n"));
        var linked = UrlInEscapedText().Replace(escaped, m =>
        {
            var url = m.Value;
            var trail = "";
            while (url.Length > 0 && ".,;:!?)".Contains(url[^1]))
            {
                trail = url[^1] + trail;
                url = url[..^1];
            }
            return $"<a href=\"{url}\">{url}</a>{trail}";
        });
        return "<!doctype html><html><head><meta charset=\"utf-8\"><style>"
               + $"html,body{{background:{style.Background};color:{style.Foreground};}}"
               + "body{margin:16px;font:14px/1.5 'Segoe UI Variable','Segoe UI',-apple-system,system-ui,sans-serif;}"
               + $"a{{color:{style.Link};}}"
               + "pre{white-space:pre-wrap;word-wrap:break-word;font:inherit;margin:0;}"
               + "</style></head><body><pre>" + linked + "</pre></body></html>";
    }

    /// <summary>Ersetzt <c>cid:</c>-Verweise durch data:-URLs der eingebetteten Teile.</summary>
    public static string ReplaceCidReferences(string html, IReadOnlyDictionary<string, InlineResource> inline) =>
        CidReference().Replace(html, m =>
        {
            var id = WebUtility.UrlDecode(m.Groups["id"].Value).Trim('<', '>');
            return inline.TryGetValue(id, out var res)
                ? $"data:{res.ContentType};base64,{Convert.ToBase64String(res.Data)}"
                : m.Value;
        });

    /// <summary>True, wenn das HTML externe Ressourcen (http/https) referenziert.</summary>
    public static bool HasExternalContent(string? html) =>
        !string.IsNullOrEmpty(html) && ExternalResource().IsMatch(html);

    /// <summary>Grobe HTML→Text-Umwandlung (für Antworten/Weiterleitungen von HTML-Mails).</summary>
    public static string HtmlToPlainText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = ScriptOrStyle().Replace(html, "");
        s = Comment().Replace(s, "");
        s = LineBreakTags().Replace(s, "\n");
        s = Tag().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        s = s.Replace(' ', ' ').Replace("\r\n", "\n");
        var lines = s.Split('\n').Select(l => l.TrimEnd());
        var text = string.Join("\n", lines);
        text = ManyNewlines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"</?(html|body)\b[^>]*>|<head\b[^>]*>.*?</head\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OuterDocumentTags();

    [GeneratedRegex(@"^\s*<!doctype[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingDoctype();

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlInEscapedText();

    [GeneratedRegex(@"cid:(?<id>[^""'\s>)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CidReference();

    [GeneratedRegex(@"(?:\b(?:src|background|poster|srcset)\s*=\s*[""']?\s*https?:|url\(\s*[""']?\s*https?:|<link\b[^>]*\bhref\s*=\s*[""']?\s*https?:)", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalResource();

    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<\s*(br|/p|/div|/tr|/li|/h[1-6]|/blockquote|/table)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewlines();
}
