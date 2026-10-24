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
        var count = 0;
        using var d = new Debouncer(TimeSpan.FromMilliseconds(150), () => Interlocked.Increment(ref count));
        for (var i = 0; i < 5; i++) { d.Trigger(); await Task.Delay(30); }
        await Task.Delay(400);
        Assert.Equal(1, count);
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
