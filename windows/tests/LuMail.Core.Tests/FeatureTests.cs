using System.Net;
using System.Text;
using LuMail.Core.Catalog;
using LuMail.Core.Login;
using LuMail.Core.Mail;
using LuMail.Core.Models;
using LuMail.Core.Storage;
using MimeKit;
using MailService = LuMail.Core.Mail.MailService;

namespace LuMail.Core.Tests;

public class ConversationTests
{
    private static MessageSummary M(uint uid, string subject, string from, int minutes, string? id = null, string? inReplyTo = null,
        string[]? refs = null, string folder = "INBOX", string account = "A", params string[] to) => new()
    {
        AccountId = account, FolderPath = folder, Uid = uid, Subject = subject, FromAddress = from, FromName = from.Split('@')[0],
        Date = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero).AddMinutes(minutes),
        MessageId = id, InReplyTo = inReplyTo, References = refs ?? Array.Empty<string>(),
        Recipients = to.Select(t => new MailAddress(null, t)).ToList(),
    };

    [Theory]
    [InlineData("Re: AW: Fwd: Projekt", "projekt")]
    [InlineData("WG:  FW: Ответ: Отчёт", "отчёт")]
    [InlineData("RE[2]: Termin", "termin")]
    [InlineData("Reise", "reise")]
    public void NormalizesSubjects(string input, string expected) => Assert.Equal(expected, ConversationBuilder.NormalizeSubject(input));

    [Fact]
    public void GroupsByHeaders()
    {
        var list = new[]
        {
            M(1, "Projekt", "anna@x.de", 0, id: "a@x"),
            M(2, "Re: Projekt", "bob@y.de", 5, id: "b@y", inReplyTo: "a@x"),
            M(3, "Ganz anders", "carl@z.de", 10, id: "c@z", refs: new[] { "a@x", "b@y" }),
            M(4, "Projekt", "dora@w.de", 20, id: "d@w"),            // gleicher Betreff, keine gemeinsamen Teilnehmer
        };
        var groups = ConversationBuilder.Group(list);
        Assert.Equal(2, groups.Count);
        var thread = groups.Single(g => g.Count == 3);
        Assert.Equal(new uint[] { 1, 2, 3 }, thread.Messages.Select(m => m.Uid));
        Assert.Equal(3u, thread.Latest.Uid);
        Assert.Equal(new[] { "carl", "bob", "anna" }, thread.Participants);
        Assert.Equal(4u, groups[0].Latest.Uid); // neueste Konversation zuerst
    }

    [Fact]
    public void GroupsBySubjectAndParticipants_WhenNoHeaders()
    {
        var list = new[]
        {
            M(1, "Urlaub", "anna@x.de", 0, to: "me@me.de"),
            M(2, "AW: Urlaub", "me@me.de", 5, to: "anna@x.de"),
            M(3, "Urlaub", "fremd@q.de", 6, to: "andere@q.de"),
        };
        var groups = ConversationBuilder.Group(list);
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Messages.Select(m => m.Uid).SequenceEqual(new uint[] { 1, 2 }));
    }

    [Fact]
    public void NeverMixesAccountsOrFolders()
    {
        var list = new[]
        {
            M(1, "X", "a@x.de", 0, id: "1@x"),
            M(2, "Re: X", "b@x.de", 1, inReplyTo: "1@x", account: "B"),
            M(3, "Re: X", "b@x.de", 2, inReplyTo: "1@x", folder: "Archiv"),
        };
        Assert.Equal(3, ConversationBuilder.Group(list).Count);
    }
}

public class PrivacyTests
{
    [Fact]
    public void TrackerReport_CountsPixelsAndListedDomains()
    {
        var html = """
            <img src="https://xyz.list-manage.com/track/open.php?u=1" width="600">
            <img src="https://cdn.shop.de/pixel.gif" width="1" height="1">
            <img src="https://cdn.shop.de/logo.png" width="200" height="80">
            <img src="https://www.facebook.com/tr?id=1&amp;ev=open">
            <img src="data:image/gif;base64,R0lG" width="1" height="1">
            <img src="https://img.sendgrid.net/a.png" style="width:1px;height:1px">
            """;
        var report = TrackerDetector.LoadEmbedded().Detect(html);
        Assert.Equal(4, report.Count);
        Assert.Contains("list-manage.com", report.Domains);
        Assert.Contains("cdn.shop.de", report.Domains);
        Assert.Contains("facebook.com/tr", report.Domains);
        Assert.Equal("4 Tracker blockiert (list-manage.com, cdn.shop.de, facebook.com/tr, …)", report.Describe());
        Assert.Equal("", TrackerDetector.LoadEmbedded().Detect("<p>Hallo</p>").Describe());
    }

    private static IReadOnlyList<string> Check(string name, string address, string? html = null) =>
        PhishingDetector.LoadEmbedded().Check(name, address, html);

    [Fact]
    public void Phishing_DisplayNameWithOtherAddress()
    {
        Assert.StartsWith("Der Anzeigename nennt die Adresse service@paypal.com", Check("service@paypal.com", "random@evil.ru")[0]);
        Assert.Single(Check("info@firma.de", "random@evil.ru"));
        Assert.Empty(Check("max@firma.de", "max@firma.de"));
    }

    [Fact]
    public void Phishing_BrandWithForeignDomain()
    {
        Assert.Contains(Check("PayPal Kundenservice", "info@pay-pal-secure.com"), w => w.Contains("PayPal"));
        Assert.Empty(Check("PayPal", "service@mail.paypal.de"));
        Assert.Empty(Check("Sparkasse Köln", "info@sparkasse.de"));
        Assert.NotEmpty(Check("Сбербанк", "noreply@sber-bank.xyz"));
        Assert.Empty(Check("Amazonas Reisen", "info@amazonas-reisen.de")); // nur ganze Wörter
    }

    [Fact]
    public void Phishing_LinkTextShowsOtherDomain()
    {
        Assert.NotEmpty(Check("Bank", "a@bank.de", """<a href="https://evil.example/login">https://www.bank.de/login</a>"""));
        Assert.NotEmpty(Check("Bank", "a@bank.de", """<a href="http://evil.example/">www.bank.de</a>"""));
        Assert.Empty(Check("Bank", "a@bank.de", """<a href="https://login.bank.de/x">www.bank.de</a>"""));
        Assert.Empty(Check("Bank", "a@bank.de", """<a href="https://evil.example/">Hier klicken</a>"""));
    }
}

public class UnsubscribeTests
{
    [Fact]
    public void OneClick_WhenHttpsAndPostHeader()
    {
        var info = UnsubscribeInfo.Parse("<mailto:unsub@shop.de?subject=stop>, <https://shop.de/u/123>", "List-Unsubscribe=One-Click")!;
        Assert.True(info.OneClick);
        Assert.Equal(UnsubscribeMethod.OneClickPost, info.Method);
        Assert.Equal("https://shop.de/u/123", info.HttpsUrl!.AbsoluteUri);
        Assert.Equal("unsub@shop.de", info.MailtoAddress);
        Assert.Equal("stop", info.MailtoSubject);
    }

    [Fact]
    public void Mailto_ThenBrowser()
    {
        var mail = UnsubscribeInfo.Parse("<https://shop.de/u>, <mailto:u@shop.de>", null)!;
        Assert.Equal(UnsubscribeMethod.Mailto, mail.Method);
        Assert.Equal("unsubscribe", mail.MailtoSubject);
        Assert.Equal(UnsubscribeMethod.Browser, UnsubscribeInfo.Parse("<https://shop.de/u>", null)!.Method);
        Assert.Null(UnsubscribeInfo.Parse("<http://unsicher.de/u>", null)); // nur https
        Assert.Null(UnsubscribeInfo.Parse(null, null));
    }

    [Fact]
    public async Task OneClickPost_SendsExactBody()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var http = new HttpClient(new CapturingHandler(handler, b => body = b));
        Assert.True(await OneClickUnsubscribe.PostAsync(http, new Uri("https://shop.de/u/1")));
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("List-Unsubscribe=One-Click", body);
        Assert.Equal("application/x-www-form-urlencoded", captured.Content!.Headers.ContentType!.ToString());
        Assert.False(captured.Headers.Contains("Cookie"));
    }

    private sealed class CapturingHandler : DelegatingHandler
    {
        private readonly Action<string> _body;
        public CapturingHandler(HttpMessageHandler inner, Action<string> body) : base(inner) => _body = body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _body(await request.Content!.ReadAsStringAsync(ct));
            return await base.SendAsync(request, ct);
        }
    }
}

public class SelectionLogicTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 18, 12, 0, 0, TimeSpan.FromHours(1)); // Mittwoch

    [Fact]
    public void Cleanup_SelectsOldUnflaggedMessagesOfConfiguredCategories()
    {
        var msgs = new List<MessageSummary>();
        for (uint i = 0; i < 150; i++)
            msgs.Add(new MessageSummary { Uid = i, CategoryId = "promotions", Date = Now.AddDays(-40 - i) });
        msgs.Add(new MessageSummary { Uid = 500, CategoryId = "promotions", Date = Now.AddDays(-60), Flagged = true });
        msgs.Add(new MessageSummary { Uid = 501, CategoryId = "promotions", Date = Now.AddDays(-5) });
        msgs.Add(new MessageSummary { Uid = 502, CategoryId = "social", Date = Now.AddDays(-100) });
        msgs.Add(new MessageSummary { Uid = 503, CategoryId = "primary", Date = Now.AddDays(-400) });

        var days = new Dictionary<string, int> { ["promotions"] = 30, ["social"] = 0 };
        var selected = CleanupSelector.Select(msgs, days, Now);
        Assert.Equal(100, selected.Count);
        Assert.DoesNotContain(selected, m => m.Flagged || m.Uid >= 500);
        Assert.Equal(149u, selected[0].Uid); // älteste zuerst
    }

    [Fact]
    public void ServerSearch_Criteria()
    {
        Assert.Equal("X-GM-RAW \"rechnung 2024\"", ServerSearch.DescribeCriteria("rechnung 2024", gmail: true));
        Assert.Equal("CHARSET UTF-8 OR OR FROM \"Grüße\" SUBJECT \"Grüße\" BODY \"Grüße\"", ServerSearch.DescribeCriteria(" Grüße ", gmail: false));
        Assert.NotNull(ServerSearch.BuildQuery("x", true));
        Assert.IsType<MailKit.Search.BinarySearchQuery>(ServerSearch.BuildQuery("x", false));
    }

    [Theory]
    [InlineData(FolderRole.Inbox, false, true)]
    [InlineData(FolderRole.None, false, true)]
    [InlineData(FolderRole.Spam, false, true)]
    [InlineData(FolderRole.Sent, false, false)]
    [InlineData(FolderRole.Drafts, false, false)]
    [InlineData(FolderRole.Trash, false, false)]
    [InlineData(FolderRole.Archive, false, false)]
    [InlineData(FolderRole.None, true, false)]
    public void StatusSelection(FolderRole role, bool noSelect, bool expected) =>
        Assert.Equal(expected, FolderStatusSelection.ShouldQuery(role, noSelect));

    [Fact]
    public void SnoozePresets_Wednesday()
    {
        var presets = SnoozePresets.For(Now);
        Assert.Equal(new[] { "Heute 18:00", "Morgen 08:00", "Montag 08:00" }, presets.Select(p => p.Label));
        Assert.Equal(new DateTime(2026, 3, 18, 18, 0, 0), presets[0].Until.DateTime);
        Assert.Equal(new DateTime(2026, 3, 19, 8, 0, 0), presets[1].Until.DateTime);
        Assert.Equal(new DateTime(2026, 3, 23, 8, 0, 0), presets[2].Until.DateTime);
    }

    [Fact]
    public void SnoozePresets_MondayEvening_SkipsTodayAndUsesNextMonday()
    {
        var mondayEvening = new DateTimeOffset(2026, 3, 23, 19, 0, 0, TimeSpan.FromHours(1));
        var presets = SnoozePresets.For(mondayEvening);
        Assert.Equal(new[] { "Morgen 08:00", "Montag 08:00" }, presets.Select(p => p.Label));
        Assert.Equal(new DateTime(2026, 3, 30, 8, 0, 0), presets[1].Until.DateTime);
    }

    [Fact]
    public void Backoff_DoublesUpToTenMinutes()
    {
        TimeSpan? b = null;
        var seen = new List<double>();
        for (var i = 0; i < 7; i++) { b = ReconnectBackoff.Next(b); seen.Add(b.Value.TotalSeconds); }
        Assert.Equal(new double[] { 30, 60, 120, 240, 480, 600, 600 }, seen);
    }

    [Fact]
    public async Task Debouncer_FiresOnceAfterQuietPeriod()
    {
        // Ohne kurze Task.Delay-Abstände: auf ausgelasteten CI-Runnern können die länger als die Wartezeit dauern.
        var count = 0;
        using var d = new Debouncer(TimeSpan.FromMilliseconds(200), () => Interlocked.Increment(ref count));
        for (var i = 0; i < 5; i++) d.Trigger();
        await WaitUntil(() => Volatile.Read(ref count) >= 1, TimeSpan.FromSeconds(5));
        await Task.Delay(600);
        Assert.Equal(1, Volatile.Read(ref count));

        // Nach der Ruhephase löst ein neuer Trigger wieder genau einmal aus.
        d.Trigger();
        await WaitUntil(() => Volatile.Read(ref count) >= 2, TimeSpan.FromSeconds(5));
        await Task.Delay(600);
        Assert.Equal(2, Volatile.Read(ref count));
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
    }
}

public class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumail-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Contacts_RankByCountThenLastUsed_AndDoNotRecountSameMessage()
    {
        var path = Path.Combine(_dir, "contacts.json");
        var store = new ContactStore(path);
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        store.Observe("Anna@Example.org", "Anna Alt", t0);
        store.Observe("anna@example.org", "Anna Alt", t0);            // gleiche Nachricht erneut → zählt nicht
        store.Observe("anna@example.org", "Anna Neu", t0.AddDays(1));
        store.Observe("annika@firma.de", "Annika", t0.AddDays(5));
        store.RecordSent("bob@firma.de", "Bob Anna", t0.AddDays(2));
        store.Observe("kein-kontakt", "x", t0);
        store.Save();

        var json = File.ReadAllText(path);
        Assert.Contains("\"anna@example.org\"", json);
        Assert.Contains("\"lastUsed\"", json);
        var reloaded = new ContactStore(path);
        Assert.Equal(2, reloaded.Get("anna@example.org")!.Count);
        Assert.Equal("Anna Neu", reloaded.Get("anna@example.org")!.Name);

        var suggestions = reloaded.Suggest("ann");
        Assert.Equal(new[] { "anna@example.org", "annika@firma.de", "bob@firma.de" }, suggestions.Select(s => s.Address));
        Assert.Equal("Anna Neu <anna@example.org>", suggestions[0].ToString());
        Assert.Empty(reloaded.Suggest(""));
        Assert.Equal(new[] { "annika@firma.de", "bob@firma.de" },
            reloaded.Suggest("firma").Select(s => s.Address)); // gleiche Zahl → lastUsed neuer zuerst
    }

    [Fact]
    public void Snoozed_RoundTripsSpecFormat()
    {
        var path = Path.Combine(_dir, "snoozed.json");
        var store = new SnoozeStore(path);
        var until = new DateTimeOffset(2026, 3, 18, 18, 0, 0, TimeSpan.FromHours(1));
        store.Add(new SnoozeEntry { AccountId = "A", Folder = "INBOX", MessageId = "x@y", Uid = 42, Until = until });
        var json = File.ReadAllText(path);
        foreach (var key in new[] { "accountId", "folder", "messageId", "uid", "until" }) Assert.Contains($"\"{key}\"", json);

        var reloaded = new SnoozeStore(path);
        Assert.True(reloaded.IsSnoozed("A", "INBOX", 42));
        Assert.Empty(reloaded.Due(until.AddMinutes(-1)));
        Assert.Single(reloaded.Due(until));
        Assert.True(reloaded.Remove("A", "INBOX", 42));
        Assert.Empty(new SnoozeStore(path).All);
    }

    [Fact]
    public void Outbox_SavesEmlAndJson()
    {
        var outbox = new OutboxStore(Path.Combine(_dir, "outbox"));
        var account = new Account { Id = "A", Email = "me@example.org", DisplayName = "Ich" };
        var mime = MailService.BuildMimeMessage(account, new OutgoingMessage { To = "a@b.de", Cc = "c@d.de", Subject = "Später", Body = "Text" });
        var sendAt = DateTimeOffset.Now.AddHours(1);
        var entry = outbox.Add("A", sendAt, mime);

        Assert.True(File.Exists(Path.Combine(_dir, "outbox", entry.Id + ".eml")));
        var json = File.ReadAllText(Path.Combine(_dir, "outbox", entry.Id + ".json"));
        Assert.Contains("\"accountId\": \"A\"", json);
        Assert.Contains("\"sendAt\"", json);
        Assert.Equal(new[] { "a@b.de", "c@d.de" }, outbox.All().Single().Recipients);
        Assert.Empty(outbox.Due(DateTimeOffset.Now));
        Assert.Single(outbox.Due(sendAt));
        Assert.Equal("Später", outbox.Load(entry.Id).Subject);
        outbox.Delete(entry.Id);
        Assert.Empty(outbox.All());
    }

    [Fact]
    public void OfflineCache_SnapshotMessagesAndLimit()
    {
        var cache = new OfflineCache(Path.Combine(_dir, "cache"), maxBytes: 3000);
        var snapshot = new AccountSnapshot
        {
            SavedAt = DateTimeOffset.Now,
            Folders = { new MailFolder { AccountId = "A", Path = "INBOX", DisplayName = "Posteingang", Role = FolderRole.Inbox, Unread = 2 } },
            Messages =
            {
                ["INBOX"] = new List<MessageSummary>
                {
                    new() { AccountId = "A", FolderPath = "INBOX", Uid = 7, Subject = "Hallo", CategoryId = "promotions",
                            Recipients = new[] { new MailAddress("B", "b@x.de") }, References = new[] { "r@x" } },
                },
            },
        };
        cache.SaveSnapshot("A", snapshot);
        Assert.True(File.Exists(Path.Combine(_dir, "cache", "A.json")));
        var loaded = cache.LoadSnapshot("A")!;
        Assert.Equal(FolderRole.Inbox, loaded.Folders[0].Role);
        var m = loaded.Messages["INBOX"].Single();
        Assert.Equal(("Hallo", "promotions", "b@x.de", "r@x"), (m.Subject, m.CategoryId, m.Recipients[0].Address, m.References[0]));

        Assert.Matches("^[0-9a-f]{16}$", OfflineCache.FolderHash("INBOX"));
        Assert.EndsWith(Path.Combine("A", OfflineCache.FolderHash("INBOX"), "7.eml"), cache.MessagePath("A", "INBOX", 7));

        MimeMessage Mail(int size)
        {
            var msg = new MimeMessage { Subject = "x" };
            msg.From.Add(new MailboxAddress("a", "a@b.de"));
            msg.Body = new TextPart("plain") { Text = new string('x', size) };
            return msg;
        }
        cache.SaveMessage("A", "INBOX", 1, Mail(1200));
        File.SetLastWriteTimeUtc(cache.MessagePath("A", "INBOX", 1), DateTime.UtcNow.AddMinutes(-10));
        cache.SaveMessage("A", "INBOX", 2, Mail(1200));
        File.SetLastWriteTimeUtc(cache.MessagePath("A", "INBOX", 2), DateTime.UtcNow.AddMinutes(-5));
        cache.SaveMessage("A", "INBOX", 3, Mail(1200)); // über 3000 Byte → älteste (1) wird gelöscht
        Assert.Null(cache.TryLoadMessage("A", "INBOX", 1));
        Assert.NotNull(cache.TryLoadMessage("A", "INBOX", 3));
        Assert.True(cache.MessageBytes <= 3000);

        cache.RemoveAccount("A");
        Assert.Null(cache.LoadSnapshot("A"));
        Assert.Null(cache.TryLoadMessage("A", "INBOX", 3));
    }
}

public class ComposeAndProviderTests
{
    private static readonly Account Me = new() { Email = "me@example.org", DisplayName = "Ich", Signature = "Viele Grüße\r\nIch" };

    [Fact]
    public void Signature_InNewReplyAndForward_BeforeQuote()
    {
        Assert.Equal("\n\n-- \nViele Grüße\nIch", MessageComposer.CreateNew(Me).Body);
        Assert.Equal("", MessageComposer.CreateNew(new Account()).Body);
        var detail = new MessageDetail
        {
            Subject = "Hi", From = new MailAddress("Anna", "anna@x.de"), TextBody = "Text",
            Attachments = new[] { new AttachmentInfo { FileName = "a.pdf", ContentType = "application/pdf", Content = new byte[] { 1 } } },
        };
        var reply = MessageComposer.CreateReply(detail, Me, false);
        Assert.StartsWith("\n\n-- \nViele Grüße\nIch\n\nAm ", reply.Body);
        Assert.Empty(reply.Attachments);
        var fwd = MessageComposer.CreateForward(detail, Me);
        Assert.StartsWith("\n\n-- \nViele Grüße\nIch\n\n---------- Weitergeleitete Nachricht", fwd.Body);
        Assert.Equal("a.pdf", fwd.Attachments.Single().FileName); // Weiterleiten übernimmt Anhänge
    }

    [Fact]
    public void MultipartMixed_TextFirst_Base64_Rfc2231Filename()
    {
        var mime = MailService.BuildMimeMessage(Me, new OutgoingMessage
        {
            To = "a@b.de", Subject = "Anhang", Body = "Siehe Anhang",
            Attachments =
            {
                new AttachmentInfo { FileName = "Übersicht 2026.pdf", ContentType = "application/pdf", Content = Encoding.UTF8.GetBytes("%PDF") },
                new AttachmentInfo { FileName = "plain.txt", ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("abc") },
            },
        });
        var mixed = Assert.IsType<Multipart>(mime.Body);
        Assert.Equal("mixed", mixed.ContentType.MediaSubtype);
        Assert.IsType<TextPart>(mixed[0]);
        using var ms = new MemoryStream();
        mime.WriteTo(ms);
        var raw = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Content-Disposition: attachment; filename*=UTF-8''%C3%9Cbersicht%202026.pdf", raw);
        Assert.Contains("filename*=UTF-8''plain.txt", raw);
        Assert.Contains("Content-Transfer-Encoding: base64", raw);
        Assert.Equal(2, mime.Attachments.Count());
    }

    [Fact]
    public void Account_CustomServers_UsedOnlyForCustomProvider()
    {
        var catalog = ProviderCatalog.LoadEmbedded();
        var custom = catalog.Get("custom");
        var account = new Account
        {
            ProviderId = "custom",
            CustomServers = new CustomServerSettings
            {
                Imap = new ServerEndpoint { Host = "imap.firma.de", Port = 143, Security = "starttls" },
                Smtp = new ServerEndpoint { Host = "smtp.firma.de", Port = 587, Security = "starttls" },
            },
        };
        Assert.Equal("imap.firma.de", account.ImapFor(custom).Host);
        Assert.Equal(587, account.SmtpFor(custom).Port);
        Assert.Equal("imap.gmail.com", account.ImapFor(catalog.Get("gmail")).Host);
        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls, MailConnector.ToSocketOptions(account.ImapFor(custom).Security));

        var json = System.Text.Json.JsonSerializer.Serialize(account, Resources.LuJson.Options);
        Assert.Contains("\"customServers\": {", json);
        Assert.Contains("\"imap\": {", json);
        Assert.DoesNotContain("\"signature\"", json); // null wird nicht geschrieben
    }

    [Fact]
    public void LoginPage_CustomProviderShowsServerFields_OthersNot()
    {
        var catalog = ProviderCatalog.LoadEmbedded();
        var normal = LoginTemplates.RenderLogin(catalog.Get("gmx"), "S", "", null);
        Assert.DoesNotContain("{{", normal);
        Assert.DoesNotContain("imapHost", normal);

        var custom = LoginTemplates.RenderLogin(catalog.Get("custom"), "S", "", null, servers: new CustomServerSettings
        {
            Imap = new ServerEndpoint { Host = "imap.<x>.de", Port = 143, Security = "starttls" },
            Smtp = new ServerEndpoint { Host = "", Port = 0, Security = "tls" },
        });
        Assert.DoesNotContain("{{", custom);
        Assert.Contains("name=\"imapHost\"", custom);
        Assert.Contains("value=\"imap.&lt;x&gt;.de\"", custom);
        Assert.Contains("value=\"143\"", custom);
        Assert.Contains("value=\"465\"", custom);
        Assert.Contains("<option value=\"starttls\" selected>STARTTLS</option>", custom);
        Assert.Contains("<option value=\"tls\" selected>SSL/TLS</option>", custom); // SMTP

        var defaults = LoginTemplates.RenderCustomServers(null);
        Assert.Contains("value=\"993\"", defaults);
        Assert.Contains("value=\"465\"", defaults);
    }

    [Fact]
    public void CustomServerForm_Validation()
    {
        var ok = LoginTemplates.ParseCustomServers(new Dictionary<string, string>
        {
            ["imapHost"] = " imap.firma.de ", ["imapPort"] = "", ["imapSecurity"] = "starttls",
            ["smtpHost"] = "smtp.firma.de", ["smtpPort"] = "587", ["smtpSecurity"] = "starttls",
        });
        Assert.Null(ok.Error);
        Assert.Equal(("imap.firma.de", 993, "starttls"), (ok.Servers.Imap.Host, ok.Servers.Imap.Port, ok.Servers.Imap.Security));
        Assert.Equal(587, ok.Servers.Smtp.Port);

        Assert.NotNull(LoginTemplates.ParseCustomServers(new Dictionary<string, string> { ["imapHost"] = "", ["smtpHost"] = "s.de" }).Error);
        Assert.NotNull(LoginTemplates.ParseCustomServers(new Dictionary<string, string> { ["imapHost"] = "i.de", ["smtpHost"] = "s.de", ["imapPort"] = "70000" }).Error);
        Assert.NotNull(LoginTemplates.ParseCustomServers(new Dictionary<string, string> { ["imapHost"] = "bad host", ["smtpHost"] = "s.de" }).Error);
        Assert.NotNull(LoginTemplates.ParseCustomServers(new Dictionary<string, string> { ["imapHost"] = "i.de", ["smtpHost"] = "s.de", ["smtpSecurity"] = "none" }).Error);
    }

    [Fact]
    public void ThemeVariantsAndAccents()
    {
        var theme = ThemeCatalog.LoadEmbedded();
        Assert.Equal(new[] { "luna", "neumond", "vollmond" }, theme.Variants.Select(v => v.Id));
        Assert.Equal(theme.Dark["background"], theme.Variant("luna").Dark["background"]);
        Assert.Equal("#000000", theme.Variant("neumond").Dark["background"]);
        Assert.Equal("luna", theme.Variant("unbekannt").Id);
        Assert.Equal(6, theme.Accents.Count);
        var palette = theme.Palette("vollmond", dark: false, accentId: "blau");
        Assert.Equal("#2563EB", palette["accent"]);
        Assert.Equal("#2563EB", palette["unreadDot"]);
        Assert.Equal(theme.Variant("vollmond").Light["background"], palette["background"]);
        Assert.Equal(theme.Variant("luna").Dark["accent"], theme.Palette("luna", true, "")["accent"]);
    }
}

public class ConversationHtmlTests
{
    [Fact]
    public void OlderMessagesCollapsed_NewestOpen_SingleCsp()
    {
        var html = HtmlPreparer.PrepareConversation(new[]
        {
            new MessageDetail { From = new MailAddress("Anna", "a@x.de"), TextBody = "Erste <Nachricht>", Date = DateTimeOffset.Now.AddHours(-2) },
            new MessageDetail { From = new MailAddress(null, "b@y.de"), HtmlBody = "<!doctype html><html><head><title>t</title></head><body><p>Zweite</p></body></html>", Date = DateTimeOffset.Now },
        }, allowExternalContent: false);
        Assert.StartsWith("<!doctype html><meta http-equiv=\"Content-Security-Policy\"", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "Content-Security-Policy"));
        Assert.Contains("<details><summary>Anna", html);
        Assert.Contains("<details open><summary>b@y.de", html);
        Assert.Contains("Erste &lt;Nachricht&gt;", html);
        Assert.Contains("<p>Zweite</p>", html);
        Assert.DoesNotContain("<title>", html);
    }
}

public class DiscoveryTests
{
    private static readonly ProviderCatalog Catalog = ProviderCatalog.LoadEmbedded();

    private sealed class Router : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> Routes { get; } = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var url = request.RequestUri!.AbsoluteUri;
            var route = Routes.FirstOrDefault(r => url.StartsWith(r.Key, StringComparison.Ordinal));
            return Task.FromResult(route.Value?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static HttpResponseMessage Mx(params string[] data) =>
        AuthTests.Json(HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new
        {
            Status = 0,
            Answer = data.Select(d => new { name = "x", type = 15, TTL = 300, data = d }),
        }));

    [Fact]
    public async Task KnownDomain_NoNetwork()
    {
        var router = new Router();
        var result = await new ProviderDiscovery(Catalog, new HttpClient(router)).DiscoverAsync("max@web.de");
        Assert.Equal(("webde", "domain"), (result.Provider.Id, result.Source));
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task GoogleWorkspaceMx_IsGmail_WithDnsJsonAccept()
    {
        var router = new Router();
        router.Routes["https://cloudflare-dns.com/dns-query?name=firma.de"] = _ => Mx("20 alt1.aspmx.l.google.com.", "10 aspmx.l.google.com.");
        var result = await new ProviderDiscovery(Catalog, new HttpClient(router)).DiscoverAsync("max@firma.de");
        Assert.Equal(("gmail", "mx"), (result.Provider.Id, result.Source));
        Assert.Contains(router.Requests[0].Headers.Accept, a => a.MediaType == "application/dns-json");
        Assert.Contains("type=MX", router.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task HostingerMx_IsHostinger()
    {
        var router = new Router();
        router.Routes["https://cloudflare-dns.com/"] = _ => Mx("5 mx1.hostinger.com.", "10 mx2.hostinger.com.");
        var result = await new ProviderDiscovery(Catalog, new HttpClient(router)).DiscoverAsync("info@meinshop.de");
        Assert.Equal("hostinger", result.Provider.Id);
    }

    [Fact]
    public async Task Autoconfig_PrefillsCustomServers_IgnoresPlain()
    {
        const string xml = """
            <?xml version="1.0"?>
            <clientConfig version="1.1"><emailProvider id="verein.de">
              <incomingServer type="pop3"><hostname>pop.verein.de</hostname><port>995</port><socketType>SSL</socketType></incomingServer>
              <incomingServer type="imap"><hostname>imap.%EMAILDOMAIN%</hostname><port>143</port><socketType>plain</socketType></incomingServer>
              <incomingServer type="imap"><hostname>imap.%EMAILDOMAIN%</hostname><port>993</port><socketType>SSL</socketType></incomingServer>
              <outgoingServer type="smtp"><hostname>smtp.verein.de</hostname><port>587</port><socketType>STARTTLS</socketType></outgoingServer>
            </emailProvider></clientConfig>
            """;
        var router = new Router();
        router.Routes["https://cloudflare-dns.com/"] = _ => Mx("10 mail.verein.de.");
        router.Routes["https://autoconfig.verein.de/mail/config-v1.1.xml?emailaddress=anna%40verein.de"] =
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) };
        var result = await new ProviderDiscovery(Catalog, new HttpClient(router)).DiscoverAsync("anna@verein.de");
        Assert.Equal(("custom", "autoconfig"), (result.Provider.Id, result.Source));
        Assert.Equal(("imap.verein.de", 993, "tls"), (result.Servers!.Imap.Host, result.Servers.Imap.Port, result.Servers.Imap.Security));
        Assert.Equal(("smtp.verein.de", 587, "starttls"), (result.Servers.Smtp.Host, result.Servers.Smtp.Port, result.Servers.Smtp.Security));
    }

    [Fact]
    public async Task Nothing_IsCustomWithoutServers()
    {
        var router = new Router(); // alles 404
        var result = await new ProviderDiscovery(Catalog, new HttpClient(router)).DiscoverAsync("x@unbekannt-xyz.de");
        Assert.Equal(("custom", "none"), (result.Provider.Id, result.Source));
        Assert.Null(result.Servers);
        Assert.All(router.Requests.Skip(1), r => Assert.Equal("https", r.RequestUri!.Scheme));
    }

    [Fact]
    public void MxParsing_SortsByPriority() =>
        Assert.Equal(new[] { "a.example.com", "b.example.com" },
            ProviderDiscovery.ParseMx("""{"Answer":[{"data":"20 b.example.com."},{"data":"10 a.example.com."}]}"""));

    [Fact]
    public async Task LocalPortCheck_DetectsListener()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.True(await LocalPortCheck.IsOpenAsync(port));
        listener.Stop();
        Assert.False(await LocalPortCheck.IsOpenAsync(port));
    }

    [Fact]
    public void ProviderSetupFieldsAreLoaded()
    {
        var gmail = Catalog.Get("gmail");
        Assert.Contains("google.com", gmail.MxSuffixes);
        Assert.NotEmpty(gmail.Setup.PasswordSteps);
        Assert.NotEmpty(gmail.Setup.OAuthSteps);
        Assert.Contains(gmail.Setup.PasswordSteps, s => s.Url is not null && s.Button is not null);
        Assert.Equal(1143, Catalog.Get("proton").Setup.CheckLocalPort);
        Assert.Null(Catalog.Get("gmx").Setup.CheckLocalPort);
        Assert.Equal(6, Catalog.Discovery.TimeoutSeconds);
        Assert.Equal(3, Catalog.Discovery.Autoconfig.Count);
    }

    [Fact]
    public void OAuth_UserConfigBeforeBuiltIn()
    {
        var builtIn = LuMail.Core.Auth.OAuthClientConfig.Parse("""{"gmail":{"clientId":"builtin-g","clientSecret":"s"},"outlook":{"clientId":"builtin-ms"}}""");
        var user = LuMail.Core.Auth.OAuthClientConfig.Parse("""{"gmail":{"clientId":"mine"},"mailru":{"clientId":""}}""");
        var merged = user.WithFallback(builtIn);
        Assert.True(merged.TryGet("gmail", out var g));
        Assert.Equal("mine", g.ClientId);
        Assert.True(merged.TryGet("outlook", out var o));
        Assert.Equal("builtin-ms", o.ClientId);
        Assert.False(merged.TryGet("mailru", out _));
        Assert.NotNull(LuMail.Core.Auth.OAuthClientConfig.BuiltIn); // eingebettete Ressource lesbar (im Repo "{}")
        Assert.Contains("LuMail.BuiltInOAuth.json", typeof(LuMail.Core.Auth.OAuthClientConfig).Assembly.GetManifestResourceNames());
    }
}

public class WebMailTests
{
    private static readonly ProviderCatalog Catalog = ProviderCatalog.LoadEmbedded();
    private static WebConfig Gmail => Catalog.Get("gmail").Web!;

    [Fact]
    public void AllProvidersExceptCustomHaveWeb_AndLoginUrlNeverCountsAsLoggedIn()
    {
        foreach (var p in Catalog.Providers)
        {
            if (p.CustomServers)
            {
                Assert.False(p.SupportsWeb);
                continue;
            }
            Assert.True(p.SupportsWeb, p.Id);
            Assert.Equal("web", p.AuthMethods[0]);
            Assert.False(WebMail.IsLoggedIn(p.Web!, p.Web!.LoginUrl), p.Id);
            Assert.NotNull(WebMail.FolderUrl(p.Web!, "primary"));
            Assert.True(WebMail.IsInternalHost(p.Web!, p.Web!.HomeUrl), p.Id);
        }
    }

    [Fact]
    public void IsLoggedIn_PrefixMatch_EmptyPrefixesNever()
    {
        Assert.True(WebMail.IsLoggedIn(Gmail, "https://mail.google.com/mail/u/0/#inbox"));
        Assert.False(WebMail.IsLoggedIn(Gmail, "https://accounts.google.com/signin"));
        Assert.False(WebMail.IsLoggedIn(Gmail, null));
        var icloud = Catalog.Get("icloud").Web!;
        Assert.Empty(icloud.LoggedInPrefixes);
        Assert.False(WebMail.IsLoggedIn(icloud, "https://www.icloud.com/mail/"));
    }

    [Fact]
    public void SidebarEntries_OrderAndCategorySetting()
    {
        Assert.Equal(new[] { "primary", "promotions", "social", "updates", "forums", "sent", "drafts", "archive", "spam", "trash" }
                .Where(k => Gmail.Folders.ContainsKey(k)),
            WebMail.SidebarEntries(Gmail, separateCategories: true).Select(e => e.Key));
        var compact = WebMail.SidebarEntries(Gmail, separateCategories: false);
        Assert.Equal("Posteingang", compact[0].Name);
        Assert.DoesNotContain(compact, e => e.Key is "promotions" or "social" or "updates" or "forums");
        Assert.Contains(compact, e => e.Key == "sent");
        Assert.Equal(new[] { "primary" }, WebMail.SidebarEntries(Catalog.Get("gmx").Web!, true).Select(e => e.Key));
        Assert.Null(WebMail.FolderUrl(Catalog.Get("mailru").Web!, "archive"));
    }

    [Fact]
    public void UnreadCount_InboxDetection_EmailFromTitle()
    {
        Assert.Equal(3, WebMail.UnreadCount(Gmail, "Inbox (3) - max@gmail.com - Gmail"));
        Assert.Null(WebMail.UnreadCount(Gmail, "Inbox - max@gmail.com - Gmail"));
        Assert.True(WebMail.IsOnInbox(Gmail, "https://mail.google.com/mail/u/0/#inbox"));
        Assert.False(WebMail.IsOnInbox(Gmail, "https://mail.google.com/mail/u/0/#sent"));
        Assert.Equal("max@gmail.com", WebMail.EmailFromTitle("Inbox (3) - max@gmail.com - Gmail"));
        Assert.Null(WebMail.EmailFromTitle("Proton Mail"));
        Assert.Equal("https://shop.de/a?b=1", WebMail.RedirectTarget("https://www.google.com/url?q=https://shop.de/a%3Fb%3D1&sa=D"));
        Assert.Null(WebMail.RedirectTarget("https://www.google.com/search?q=test"));
        Assert.Null(WebMail.RedirectTarget("https://www.google.com/url?q=javascript:alert(1)"));
    }

    [Fact]
    public void InternalHosts()
    {
        Assert.True(WebMail.IsInternalHost(Gmail, "https://accounts.google.com/x"));
        Assert.False(WebMail.IsInternalHost(Gmail, "https://www.example.org/"));
        Assert.Equal("google.com", WebMail.SiteOf("https://mail.google.com/mail"));
    }

    [Fact]
    public void WebAccount_JsonAndFlags()
    {
        var account = new Account { ProviderId = "gmail", Email = "max@gmail.com", AuthMethod = Account.AuthWeb };
        Assert.True(account.IsWeb);
        Assert.False(account.IsOAuth);
        var json = System.Text.Json.JsonSerializer.Serialize(account, Resources.LuJson.Options);
        Assert.Contains("\"authMethod\": \"web\"", json);
        Assert.DoesNotContain("isWeb", json, StringComparison.OrdinalIgnoreCase);
    }
}

public class WebAccountSetupTests
{
    [Fact]
    public void AddWebAccount_StoresWebMethod_NoSecrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumail-web-" + Guid.NewGuid().ToString("N"));
        try
        {
            var secrets = new InMemorySecretStore();
            var store = new AccountStore(Path.Combine(dir, "accounts.json"));
            var tokens = new LuMail.Core.Auth.OAuthTokenManager(new LuMail.Core.Auth.OAuthClient(new HttpClient()), secrets, () => LuMail.Core.Auth.OAuthClientConfig.Empty);
            var mail = new MailService(ProviderCatalog.LoadEmbedded(), CategoryCatalog.LoadEmbedded(), new StoredCredentialSource(secrets, tokens));
            var setup = new AccountSetupService(new BrowserLoginService(_ => { }, new LuMail.Core.Auth.OAuthClient(new HttpClient())),
                mail, store, secrets, tokens, () => LuMail.Core.Auth.OAuthClientConfig.Empty);
            var proton = ProviderCatalog.LoadEmbedded().Get("proton");

            var a = setup.AddWebAccount(proton, "ID-1", null);
            Assert.Equal(("Proton Mail", "web"), (a.Email, a.AuthMethod));
            var b = setup.AddWebAccount(proton, "ID-2", "max@proton.me");
            Assert.Equal("Max", b.DisplayName);
            Assert.Equal(2, store.Load().Count(x => x.IsWeb));
            Assert.Equal(0, secrets.Count);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

public class WebReaderTests
{
    private static readonly ProviderCatalog Catalog = ProviderCatalog.LoadEmbedded();

    private static string Wrap(string json) => System.Text.Json.JsonSerializer.Serialize(json); // wie ExecuteScriptAsync

    [Fact]
    public void ReaderConfigAndComposeUrl()
    {
        Assert.Equal("gmail", Catalog.Get("gmail").Web!.Reader);
        Assert.Equal("proton", Catalog.Get("proton").Web!.Reader);
        Assert.Equal("mailru", Catalog.Get("mailru").Web!.Reader);
        Assert.Null(Catalog.Get("outlook").Web!.Reader);
        Assert.False(string.IsNullOrEmpty(Catalog.Get("gmail").Web!.ComposeUrl));
        Assert.Null(Catalog.Get("proton").Web!.ComposeUrl);
    }

    [Fact]
    public void ScriptsEmbedReaderAndQuoteSafely()
    {
        Assert.Contains("window.LuWebmail", LuMail.Core.Resources.SharedResources.WebmailScript);
        var s = WebReader.OpenScript("gmail", "id\"');alert(1)//\n");
        Assert.Contains("window.LuWebmail", s);
        Assert.EndsWith("\n;LuWebmail.open(\"gmail\", \"id\\u0022\\u0027);alert(1)//\\n\")", s);
        Assert.EndsWith(";LuWebmail.list(\"proton\")", WebReader.ListScript("proton"));
        Assert.EndsWith(";LuWebmail.message(\"mailru\")", WebReader.MessageScript("mailru"));
        Assert.EndsWith(";LuWebmail.message(\"proton\", \"c1\")", WebReader.MessageScript("proton", "c1"));
    }

    [Fact]
    public void DecodesDoubleEncodedResults_WithDefaults()
    {
        var list = WebReader.DecodeList(Wrap("""{"state":"ok","items":[{"id":"a1","from":"Anna","subject":"Hi","unread":true,"attachment":true},{"from":"ohne id"},{"id":"b2"}]}"""))!;
        Assert.Equal(WebReaderState.Ok, list.State);
        Assert.Equal(2, list.Items.Count);
        Assert.True(list.Items[0].Unread && list.Items[0].Attachment);
        Assert.Equal(("(unbekannt)", "(kein Betreff)", ""), (list.Items[1].Sender, list.Items[1].DisplaySubject, list.Items[1].ShortDate));

        Assert.Null(WebReader.DecodeList("null"));
        Assert.Null(WebReader.DecodeList(null));
        Assert.Equal(WebReaderState.Error, WebReader.DecodeList(Wrap("""{"state":"weird"}"""))!.State);
        Assert.Equal(WebReaderState.Login, WebReader.DecodeList(Wrap("""{"state":"login"}"""))!.State);
        Assert.Equal(WebReaderState.Ok, WebReader.DecodeList("""{"state":"ok","items":[]}""")!.State); // nicht doppelt kodiert

        var msg = WebReader.DecodeMessage(Wrap("""{"state":"ok","subject":"Betreff","parts":[{"from":"Anna","fromEmail":"a@x.de","to":"ich","date":"10:12","html":"<p>Hallo</p>"}]}"""))!;
        Assert.Equal(("Betreff", "Anna", "<p>Hallo</p>"), (msg.Subject, msg.Parts[0].Sender, msg.Parts[0].Html));
        Assert.Equal(WebReaderState.Missing, WebReader.DecodeState(Wrap("""{"state":"missing"}""")));
    }

    [Theory]
    [InlineData("https://mail.google.com/mail/u/0/#inbox", "https://mail.google.com/mail/u/0/#inbox", WebFolderNavigation.None)]
    [InlineData("https://mail.google.com/mail/u/0/#inbox/FMfcg123", "https://mail.google.com/mail/u/0/#inbox", WebFolderNavigation.HistoryBack)]
    [InlineData("https://mail.google.com/mail/u/0/#sent", "https://mail.google.com/mail/u/0/#inbox", WebFolderNavigation.Navigate)]
    [InlineData(null, "https://e.mail.ru/inbox/", WebFolderNavigation.Navigate)]
    public void FolderNavigation(string? current, string folder, WebFolderNavigation expected) =>
        Assert.Equal(expected, WebReader.NavigationFor(current, folder));

    private static readonly WebReaderTiming Fast = new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(200))
    { ErrorAfter = TimeSpan.FromMilliseconds(100) };

    private static Func<string, Task<string?>> Sequence(params string?[] results)
    {
        var i = 0;
        return _ => Task.FromResult<string?>(results[Math.Min(i++, results.Length - 1)] is { } r ? Wrap(r) : "null");
    }

    [Fact]
    public async Task PollList_WaitsForItems_EmptyOnlyAfterDelay_LoginAndTimeout()
    {
        var ok = await WebReader.PollListAsync(Sequence("""{"state":"loading"}""", """{"state":"ok","items":[]}""", """{"state":"ok","items":[{"id":"1"}]}"""), "gmail", Fast);
        Assert.Equal((WebReaderOutcome.Ok, 1), (ok.Outcome, ok.Items.Count));

        var empty = await WebReader.PollListAsync(Sequence("""{"state":"ok","items":[]}"""), "gmail", Fast);
        Assert.Equal((WebReaderOutcome.Ok, 0), (empty.Outcome, empty.Items.Count));

        Assert.Equal(WebReaderOutcome.NeedsLogin, (await WebReader.PollListAsync(Sequence("""{"state":"login"}"""), "gmail", Fast)).Outcome);
        Assert.Equal(WebReaderOutcome.Failed, (await WebReader.PollListAsync(Sequence("""{"state":"error"}"""), "gmail", Fast)).Outcome);
        Assert.Equal(WebReaderOutcome.Failed, (await WebReader.PollListAsync(Sequence(null, """{"state":"loading"}"""), "gmail", Fast)).Outcome);
    }

    [Fact]
    public async Task UnreadableList_IsError_AndFails()
    {
        // Qualitätsprüfung von lu-webmail.js: zusätzliches Feld "reason" wird vertragen.
        const string unreadable = """{"state":"error","reason":"unreadable","items":[]}""";
        var list = WebReader.DecodeList(Wrap(unreadable))!;
        Assert.Equal((WebReaderState.Error, "unreadable", 0), (list.State, list.Reason, list.Items.Count));
        Assert.Null(WebReader.DecodeList(Wrap("""{"state":"ok","items":[]}"""))!.Reason);
        var polled = await WebReader.PollListAsync(Sequence("""{"state":"loading"}""", unreadable), "proton", Fast);
        Assert.Equal((WebReaderOutcome.Failed, 0), (polled.Outcome, polled.Items.Count));
    }

    [Fact]
    public void CaptureScriptIsEmbedded()
    {
        var script = LuMail.Core.Resources.SharedResources.CaptureScript;
        Assert.Contains("data-lu-capture", script);
        Assert.Contains("__luCaptureInstalled", script);
        Assert.Contains(LuMail.Core.Resources.SharedResources.Prefix + "lu-capture.js", LuMail.Core.Resources.SharedResources.ResourceNames);
    }

    [Fact]
    public async Task PollList_ErrorFailsOnlyWhenPersistent_UnsupportedImmediately()
    {
        Assert.Equal(4, WebReaderTiming.Default.ErrorAfter.TotalSeconds);

        // kurzes "error" (halb aufgebaute Seite) → weiter abfragen
        var recovered = await WebReader.PollListAsync(Sequence("""{"state":"error","reason":"unreadable"}""", """{"state":"ok","items":[{"id":"1"}]}"""), "proton", Fast);
        Assert.Equal((WebReaderOutcome.Ok, 1), (recovered.Outcome, recovered.Items.Count));

        // anhaltendes "error" → Failed nach ErrorAfter, nicht erst nach dem Zeitlimit
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(WebReaderOutcome.Failed, (await WebReader.PollListAsync(Sequence("""{"state":"error"}"""), "proton", Fast)).Outcome);
        Assert.InRange(clock.ElapsedMilliseconds, 100, 380);

        clock.Restart();
        Assert.Equal(WebReaderOutcome.Failed, (await WebReader.PollListAsync(Sequence("""{"state":"unsupported"}"""), "proton", Fast)).Outcome);
        Assert.True(clock.ElapsedMilliseconds < 90);
    }

    [Fact]
    public async Task Polling_CancelThrows_NeverFailed()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        Func<string, Task<string?>> execute = _ =>
        {
            if (++calls == 3) cts.Cancel();
            return Task.FromResult<string?>(Wrap("""{"state":"loading"}"""));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WebReader.PollListAsync(execute, "gmail", Fast, cts.Token));
        Assert.Equal(3, calls);

        using var cts2 = new CancellationTokenSource();
        calls = 0;
        Func<string, Task<string?>> message = _ =>
        {
            if (++calls == 2) cts2.Cancel();
            return Task.FromResult<string?>(Wrap(calls == 1 ? """{"state":"ok"}""" : """{"state":"loading"}"""));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WebReader.OpenAndPollMessageAsync(message, "gmail", "1", Fast, cts2.Token));
    }

    /// <summary>
    /// Wie WebAccountService.ExecuteAsync auf dem UI-Thread: Aufrufe außerhalb des Kontext-Threads scheitern still
    /// (null, wie RPC_E_WRONG_THREAD + catch), echte Aufrufe sind asynchron und kehren in den Kontext zurück.
    /// </summary>
    private static Func<string, Task<string?>> UiAffine(SingleThreadContext ui, Action<bool> record, params string[] results)
    {
        var i = 0;
        return async _ =>
        {
            var onUi = Thread.CurrentThread == ui.Thread;
            record(onUi);
            if (!onUi) return null;
            await Task.Delay(1);
            return Wrap(results[Math.Min(i++, results.Length - 1)]);
        };
    }

    [Fact]
    public async Task Polling_StaysOnCallerContext()
    {
        using var ui = new SingleThreadContext();
        var onUi = 0;
        var offUi = 0;
        void Record(bool on) { if (on) onUi++; else offUi++; }

        // "error" (halb aufgebaute Seite), dann "ok" → Ok; jeder Aufruf auf dem UI-Thread
        var list = await ui.Run(() => WebReader.PollListAsync(
            UiAffine(ui, Record, """{"state":"error","reason":"unreadable"}""", """{"state":"loading"}""", """{"state":"ok","items":[{"id":"1"}]}"""),
            "proton", Fast));
        Assert.Equal((WebReaderOutcome.Ok, 1), (list.Outcome, list.Items.Count));
        Assert.Equal(0, offUi);
        Assert.Equal(3, onUi);

        // Nachricht: open ok, erste Abfrage loading, zweite ok → Ergebnis
        onUi = 0;
        var message = await ui.Run(() => WebReader.OpenAndPollMessageAsync(
            UiAffine(ui, Record, """{"state":"ok"}""", """{"state":"loading"}""", """{"state":"ok","subject":"S","parts":[{"html":"<p>x</p>"}]}"""),
            "proton", "1", Fast));
        Assert.Equal("S", message?.Subject);
        Assert.Equal(0, offUi);
        Assert.Equal(3, onUi);
    }

    [Fact]
    public async Task OpenAndPollMessage()
    {
        var m = await WebReader.OpenAndPollMessageAsync(Sequence("""{"state":"ok"}""", """{"state":"loading"}""", """{"state":"ok","subject":"S","parts":[]}"""), "gmail", "1", Fast);
        Assert.Equal("S", m!.Subject);
        Assert.Null(await WebReader.OpenAndPollMessageAsync(Sequence("""{"state":"missing"}"""), "gmail", "1", Fast));
        Assert.Null(await WebReader.OpenAndPollMessageAsync(Sequence("""{"state":"ok"}""", """{"state":"loading"}"""), "gmail", "1", Fast));
    }

    [Fact]
    public void PrepareHtml_CspAndParts()
    {
        var html = WebReader.PrepareHtml(new WebMessageResult
        {
            State = WebReaderState.Ok, Subject = "S",
            Parts =
            {
                new WebReaderPart { From = "Anna", FromEmail = "a@x.de", To = "Ich <i@y.de>", Date = "Gestern", Html = "<p>Eins</p>" },
                new WebReaderPart { From = "Bob", Date = "10:00", Html = "<p>Zwei</p>" },
            },
        }, allowExternalContent: false);
        Assert.StartsWith("<!doctype html><meta http-equiv=\"Content-Security-Policy\" content=\"default-src &#39;none&#39;; img-src data: cid:", html);
        Assert.Contains("<details><summary>Anna &lt;a@x.de&gt;<span>Gestern</span>", html);
        Assert.Contains("An: Ich &lt;i@y.de&gt;", html);
        Assert.Contains("<details open><summary>Bob", html);
    }
}

public class NightSkyTests
{
    [Fact]
    public void Stars_AreDeterministicAndInRange()
    {
        var a = NightSky.Stars();
        var b = NightSky.Stars();
        Assert.Equal(160, a.Count);
        Assert.Equal(a, b); // fester Startwert
        Assert.NotEqual(a, NightSky.Stars(seed: 1));
        Assert.All(a, s =>
        {
            Assert.InRange(s.X, 0, 1);
            Assert.InRange(s.Y, 0, 1);
            Assert.InRange(s.Size, 0.8, 2.4);
            Assert.InRange(s.Period, 2, 6);
            Assert.InRange(s.Phase, 0, s.Period);
            Assert.InRange(s.RestOpacity, 0.25, 1);
        });
        Assert.True(a.Count(s => s.Tint == SkyStarTint.White) > 80);
        Assert.Contains(a, s => s.Tint == SkyStarTint.Blue);
        Assert.Contains(a, s => s.Tint == SkyStarTint.Yellow);
        Assert.True(a.Select(s => Math.Round(s.Period, 2)).Distinct().Count() > 100); // eigene Periode je Stern
    }

    [Theory]
    [InlineData(4.0, 0.0)]
    [InlineData(4.0, 1.0)]
    [InlineData(4.0, 2.0)]
    [InlineData(3.0, 2.9)]
    [InlineData(5.5, 7.0)]
    public void Twinkle_IsSeamlessShiftedTriangle(double period, double phase)
    {
        var frames = NightSky.Twinkle(period, phase);
        Assert.Equal(0, frames[0].Time);
        Assert.Equal(period, frames[^1].Time, 6);
        Assert.Equal(frames[0].Value, frames[^1].Value, 6);
        Assert.True(frames.Zip(frames.Skip(1)).All(p => p.First.Time < p.Second.Time));
        Assert.All(frames, f => Assert.InRange(f.Value, 0.25, 1.0));
        Assert.Contains(frames, f => Math.Abs(f.Value - 1.0) < 1e-9);
        Assert.Contains(frames, f => Math.Abs(f.Value - 0.25) < 1e-9);
        Assert.InRange(frames.Count, 3, 4);
    }

    [Fact]
    public void Twinkle_PhaseZeroStartsLow_HalfStartsHigh()
    {
        Assert.Equal(new[] { new SkyKeyFrame(0, 0.25), new SkyKeyFrame(2, 1), new SkyKeyFrame(4, 0.25) }, NightSky.Twinkle(4, 0));
        Assert.Equal(new[] { new SkyKeyFrame(0, 1), new SkyKeyFrame(2, 0.25), new SkyKeyFrame(4, 1) }, NightSky.Twinkle(4, 2));
        var quarter = NightSky.Twinkle(4, 1);
        Assert.Equal(0.625, quarter[0].Value, 6);
        Assert.Equal(new SkyKeyFrame(1, 1), quarter[1]);
        Assert.Equal(new SkyKeyFrame(3, 0.25), quarter[2]);
    }

    [Fact]
    public void ShootingStarDelay_Between9And18Seconds()
    {
        var random = new Random(7);
        for (var i = 0; i < 200; i++) Assert.InRange(NightSky.NextShootingDelay(random).TotalSeconds, 9, 18);
    }

    [Fact]
    public void Setting_DefaultsOnAndPersists()
    {
        Assert.True(new AppSettings().NightSky);
        var path = Path.Combine(Path.GetTempPath(), "lumail-sky-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"themeMode\":\"dark\"}");
            Assert.True(AppSettings.Load(path).NightSky); // ältere Datei ohne Feld → an
            var settings = new AppSettings { NightSky = false };
            settings.Save(path);
            Assert.False(AppSettings.Load(path).NightSky);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>Ein einziger Thread mit eigener Nachrichtenschleife – wie der UI-Thread von WinUI.</summary>
internal sealed class SingleThreadContext : SynchronizationContext, IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

    public SingleThreadContext()
    {
        Thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
        }) { IsBackground = true, Name = "UI (Test)" };
        Thread.Start();
    }

    public Thread Thread { get; }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();
    public override SynchronizationContext CreateCopy() => this;

    /// <summary><paramref name="work"/> auf dem Kontext-Thread starten und auf das Ergebnis warten.</summary>
    public Task<T> Run<T>(Func<Task<T>> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try { done.SetResult(await work()); }
            catch (Exception ex) { done.SetException(ex); }
        }, null);
        return done.Task;
    }

    public void Dispose() => _queue.CompleteAdding();
}

public class SidebarCollapseSettingsTests
{
    [Fact]
    public void SetCollapsed_AddsAndRemovesOnce()
    {
        var list = new List<string>();
        Assert.True(AppSettings.SetCollapsed(list, "gmail", true));
        Assert.False(AppSettings.SetCollapsed(list, "gmail", true)); // schon zugeklappt
        Assert.Equal(new[] { "gmail" }, list);
        Assert.True(AppSettings.SetCollapsed(list, "gmail", false));
        Assert.False(AppSettings.SetCollapsed(list, "gmail", false));
        Assert.Empty(list);
    }

    [Fact]
    public void CollapsedState_PersistsAndDropsRemovedAccounts()
    {
        var settings = new AppSettings();
        Assert.Empty(settings.CollapsedProviders); // Standard: alles aufgeklappt
        Assert.Empty(settings.CollapsedAccounts);
        AppSettings.SetCollapsed(settings.CollapsedProviders, "gmail", true);
        AppSettings.SetCollapsed(settings.CollapsedAccounts, "a1", true);
        AppSettings.SetCollapsed(settings.CollapsedAccounts, "gone", true);

        Assert.True(settings.PruneCollapsedAccounts(new[] { "a1", "a2" }));
        Assert.False(settings.PruneCollapsedAccounts(new[] { "a1", "a2" }));
        Assert.Equal(new[] { "a1" }, settings.CollapsedAccounts);

        var path = Path.Combine(Path.GetTempPath(), "lumail-sidebar-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            settings.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.Equal(new[] { "gmail" }, loaded.CollapsedProviders);
            Assert.Equal(new[] { "a1" }, loaded.CollapsedAccounts);
            File.WriteAllText(path, "{}");
            Assert.Empty(AppSettings.Load(path).CollapsedAccounts); // ältere Datei → aufgeklappt
        }
        finally
        {
            File.Delete(path);
        }
    }
}
