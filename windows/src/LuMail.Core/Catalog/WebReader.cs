using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LuMail.Core.Login;
using LuMail.Core.Mail;
using LuMail.Core.Resources;

namespace LuMail.Core.Catalog;

/// <summary>Zustand einer Antwort von <c>lu-webmail.js</c>.</summary>
public enum WebReaderState { Ok, Loading, Login, Missing, Unsupported, Error }

/// <summary>Eine Zeile der Nachrichtenliste im Webmail.</summary>
public sealed class WebReaderItem
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string Date { get; set; } = "";
    public string DateTitle { get; set; } = "";
    public bool Unread { get; set; }
    public bool Attachment { get; set; }

    [JsonIgnore] public string Sender => From.Length > 0 ? From : FromEmail.Length > 0 ? FromEmail : "(unbekannt)";
    [JsonIgnore] public string DisplaySubject => Subject.Length > 0 ? Subject : "(kein Betreff)";
    /// <summary>Kurzer Datumstext: Text aus dem Webmail, sonst der lange Titel.</summary>
    [JsonIgnore] public string ShortDate => Date.Length > 0 ? Date : DateTitle;
}

/// <summary>Ein Teil (eine Nachricht) einer geöffneten Unterhaltung.</summary>
public sealed class WebReaderPart
{
    public string From { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string To { get; set; } = "";
    public string Date { get; set; } = "";
    public string Html { get; set; } = "";

    [JsonIgnore] public string Sender => From.Length > 0 ? From : FromEmail;
}

public sealed class WebListResult
{
    public WebReaderState State { get; set; } = WebReaderState.Error;
    /// <summary>Grund bei <c>error</c>, z. B. <c>unreadable</c> (Qualitätsprüfung: die Seite hat sich geändert).</summary>
    public string? Reason { get; set; }
    public List<WebReaderItem> Items { get; set; } = new();
}

public sealed class WebMessageResult
{
    public WebReaderState State { get; set; } = WebReaderState.Error;
    public string Subject { get; set; } = "";
    public List<WebReaderPart> Parts { get; set; } = new();
}

/// <summary>Ergebnis des Listenabrufs.</summary>
public enum WebReaderOutcome { Ok, NeedsLogin, Failed }

/// <summary>Wartezeiten nach FUNKTIONEN §21 (für Tests verkürzbar).</summary>
public sealed record WebReaderTiming(TimeSpan ListInterval, TimeSpan EmptyAfter, TimeSpan ListTimeout,
    TimeSpan MessageInterval, TimeSpan MessageTimeout)
{
    public static WebReaderTiming Default { get; } = new(TimeSpan.FromMilliseconds(700), TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(15));
}

/// <summary>Wie der Hintergrund-Browser zur Ordneradresse kommt.</summary>
public enum WebFolderNavigation { None, HistoryBack, Navigate }

/// <summary>
/// Lu-Ansicht für Web-Konten (FUNKTIONEN §21): Skriptaufrufe von <c>shared/webmail/lu-webmail.js</c>, Dekodierung der
/// Ergebnisse (WebView2 kodiert den zurückgegebenen JSON-String noch einmal als JSON) und Abfrage-Schleifen.
/// </summary>
public static class WebReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Sicheres JavaScript-String-Literal (JSON ist gültiges JavaScript).</summary>
    public static string JsString(string value) => JsonSerializer.Serialize(value);

    /// <summary>Skript + <c>\n;</c> + Aufruf – das Skript definiert <c>window.LuWebmail</c> nur einmal.</summary>
    public static string Script(string call) => SharedResources.WebmailScript + "\n;" + call;

    public static string ListScript(string reader) => Script($"LuWebmail.list({JsString(reader)})");
    public static string OpenScript(string reader, string id) => Script($"LuWebmail.open({JsString(reader)}, {JsString(id)})");
    public static string MessageScript(string reader) => Script($"LuWebmail.message({JsString(reader)})");

    /// <summary>
    /// Ergebnis von <c>ExecuteScriptAsync</c> → JSON des Lesers. WebView2 liefert den String JSON-kodiert
    /// (<c>"{\"state\":…}"</c>); <c>null</c>/leer → null. Ein direkt zurückgegebenes Objekt wird unverändert übernommen.
    /// </summary>
    public static string? UnwrapScriptResult(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (t == "null" || t == "undefined") return null;
        if (!t.StartsWith('"')) return t;
        try { return JsonSerializer.Deserialize<string>(t); }
        catch (JsonException) { return null; }
    }

    public static T? Decode<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return null; }
    }

    /// <summary>Liste dekodieren; unbekannter Zustand → error, Einträge ohne id werden verworfen.</summary>
    public static WebListResult? DecodeList(string? raw)
    {
        var json = UnwrapScriptResult(raw);
        var result = Decode<WebListResult>(NormalizeState(json));
        if (result is null) return null;
        result.Items = result.Items.Where(i => !string.IsNullOrEmpty(i.Id)).ToList();
        return result;
    }

    public static WebMessageResult? DecodeMessage(string? raw) => Decode<WebMessageResult>(NormalizeState(UnwrapScriptResult(raw)));

    public static WebReaderState DecodeState(string? raw) =>
        Decode<StateOnly>(NormalizeState(UnwrapScriptResult(raw)))?.State ?? WebReaderState.Error;

    private sealed class StateOnly
    {
        public WebReaderState State { get; set; } = WebReaderState.Error;
    }

    /// <summary>Unbekannte <c>state</c>-Werte (oder fehlende) als "error" behandeln.</summary>
    private static string? NormalizeState(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var state = doc.RootElement.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            if (state is not null && Enum.TryParse<WebReaderState>(state, ignoreCase: true, out _)) return json;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
            dict["state"] = JsonSerializer.SerializeToElement("error");
            return JsonSerializer.Serialize(dict);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Steht der Browser in einer Nachricht des Ordners (Adresse beginnt mit der Ordneradresse), genügt history.back().</summary>
    public static WebFolderNavigation NavigationFor(string? currentUrl, string folderUrl)
    {
        if (string.Equals(currentUrl, folderUrl, StringComparison.Ordinal)) return WebFolderNavigation.None;
        if (!string.IsNullOrEmpty(currentUrl) && currentUrl.StartsWith(folderUrl, StringComparison.Ordinal)) return WebFolderNavigation.HistoryBack;
        return WebFolderNavigation.Navigate;
    }

    /// <summary>
    /// Alle 0,7 s <c>list</c> bis <c>ok</c> mit Einträgen; eine leere Liste gilt erst nach 4 s als leer.
    /// <c>login</c> → NeedsLogin; <c>error</c>/<c>unsupported</c> oder 20 s ohne Ergebnis → Failed.
    /// </summary>
    public static async Task<(WebReaderOutcome Outcome, IReadOnlyList<WebReaderItem> Items)> PollListAsync(
        Func<string, Task<string?>> execute, string reader, WebReaderTiming? timing = null, CancellationToken ct = default)
    {
        var t = timing ?? WebReaderTiming.Default;
        var clock = Stopwatch.StartNew();
        var script = ListScript(reader);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var result = DecodeList(await execute(script).ConfigureAwait(false));
            switch (result?.State)
            {
                case WebReaderState.Ok when result.Items.Count > 0 || clock.Elapsed >= t.EmptyAfter:
                    return (WebReaderOutcome.Ok, result.Items);
                case WebReaderState.Login:
                    return (WebReaderOutcome.NeedsLogin, Array.Empty<WebReaderItem>());
                case WebReaderState.Error or WebReaderState.Unsupported:
                    return (WebReaderOutcome.Failed, Array.Empty<WebReaderItem>());
            }
            if (clock.Elapsed >= t.ListTimeout) return (WebReaderOutcome.Failed, Array.Empty<WebReaderItem>());
            await Task.Delay(t.ListInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary><c>open(id)</c>, dann alle 0,5 s <c>message</c> bis <c>ok</c> (max. 15 s); sonst null.</summary>
    public static async Task<WebMessageResult?> OpenAndPollMessageAsync(
        Func<string, Task<string?>> execute, string reader, string id, WebReaderTiming? timing = null, CancellationToken ct = default)
    {
        var t = timing ?? WebReaderTiming.Default;
        if (DecodeState(await execute(OpenScript(reader, id)).ConfigureAwait(false)) != WebReaderState.Ok) return null;
        var clock = Stopwatch.StartNew();
        var script = MessageScript(reader);
        while (clock.Elapsed < t.MessageTimeout)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(t.MessageInterval, ct).ConfigureAwait(false);
            var result = DecodeMessage(await execute(script).ConfigureAwait(false));
            if (result?.State == WebReaderState.Ok) return result;
            if (result?.State is WebReaderState.Error or WebReaderState.Login) return null;
        }
        return null;
    }

    /// <summary>
    /// Lesebereich: alle Teile untereinander (ältere eingeklappt), pro Teil Absender, Empfänger und Datum,
    /// Inhalt mit derselben CSP wie IMAP-Mails (kein JavaScript, externe Inhalte blockiert).
    /// </summary>
    public static string PrepareHtml(WebMessageResult message, bool allowExternalContent, PlainTextStyle? style = null)
    {
        var st = style ?? PlainTextStyle.Light;
        var sb = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><style>")
            .Append($"html,body{{background:{st.Background};color:{st.Foreground};}}")
            .Append("body{margin:12px;font:14px/1.5 'Segoe UI Variable','Segoe UI',-apple-system,system-ui,sans-serif;}")
            .Append($"a{{color:{st.Link};}}")
            .Append("details{border:1px solid rgba(128,128,128,.35);border-radius:10px;margin:0 0 10px;padding:0 12px;}")
            .Append("summary{cursor:pointer;padding:10px 0;font-weight:600;}summary span{font-weight:400;opacity:.7;margin-left:8px;}")
            .Append(".to{font-size:12px;opacity:.7;margin:-4px 0 8px;}.body{padding:0 0 12px;overflow-x:auto;}")
            .Append("</style></head><body>");
        var parts = message.Parts;
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            var open = i == parts.Count - 1 ? " open" : "";
            var sender = p.FromEmail.Length > 0 && p.From.Length > 0 && !p.From.Contains(p.FromEmail, StringComparison.OrdinalIgnoreCase)
                ? $"{p.From} <{p.FromEmail}>" : p.Sender;
            sb.Append("<details").Append(open).Append("><summary>").Append(Html.Escape(sender.Length > 0 ? sender : "Unbekannt"))
              .Append("<span>").Append(Html.Escape(p.Date)).Append("</span></summary>");
            if (p.To.Length > 0) sb.Append("<div class=\"to\">An: ").Append(Html.Escape(p.To)).Append("</div>");
            sb.Append("<div class=\"body\">").Append(p.Html).Append("</div></details>");
        }
        if (parts.Count == 0) sb.Append("<p>(Kein Inhalt)</p>");
        sb.Append("</body></html>");
        return HtmlPreparer.InjectCsp(sb.ToString(), allowExternalContent ? HtmlPreparer.RelaxedCsp : HtmlPreparer.BlockedCsp);
    }
}
