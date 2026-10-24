using LuMail.Core.Mail;
using LuMail.Core.Models;
using MimeKit;

namespace LuMail.Core.Tests;

public class HtmlPreparerTests
{
    [Fact]
    public void Csp_IsInjectedRightAfterDoctype()
    {
        var html = HtmlPreparer.Prepare("<!DOCTYPE html><html><head><title>x</title></head><body><img src=\"https://tracker/x.gif\"></body></html>", null, false);
        Assert.StartsWith("<!DOCTYPE html><meta http-equiv=\"Content-Security-Policy\" content=\"default-src &#39;none&#39;; img-src data: cid:; style-src &#39;unsafe-inline&#39;\">", html);
        Assert.Contains("<meta name=\"referrer\" content=\"no-referrer\">", html);
    }

    [Fact]
    public void Csp_IsInjectedAtStartForFragments_EvenWithFakeHeadInComment()
    {
        var html = HtmlPreparer.Prepare("<!-- <head> --><p>Hallo</p>", null, false);
        Assert.StartsWith("<meta http-equiv=\"Content-Security-Policy\"", html);
    }

    [Fact]
    public void RelaxedCsp_WhenExternalContentAllowed()
    {
        var html = HtmlPreparer.Prepare("<p>x</p>", null, true);
        Assert.Contains("img-src data: cid: http: https:", html);
        Assert.DoesNotContain("script-src", html);
    }

    [Fact]
    public void PlainText_IsEscapedAndLinkified()
    {
        var html = HtmlPreparer.Prepare(null, "Hallo <b>Welt</b> & Co.\nSiehe https://example.org/a?b=1&c=2.", false);
        Assert.Contains("Hallo &lt;b&gt;Welt&lt;/b&gt; &amp; Co.", html);
        Assert.Contains("<a href=\"https://example.org/a?b=1&amp;c=2\">https://example.org/a?b=1&amp;c=2</a>.", html);
        Assert.Contains("<pre>", html);
        Assert.StartsWith("<!doctype html><meta http-equiv=\"Content-Security-Policy\"", html);
    }

    [Fact]
    public void CidReferences_BecomeDataUrls()
    {
        var inline = new Dictionary<string, InlineResource>(StringComparer.OrdinalIgnoreCase)
        {
            ["logo@x"] = new("image/png", new byte[] { 1, 2, 3 }),
        };
        var html = HtmlPreparer.Prepare("<img src=\"cid:logo@x\"><img src=\"cid:missing\">", null, false, inline);
        Assert.Contains("src=\"data:image/png;base64,AQID\"", html);
        Assert.Contains("src=\"cid:missing\"", html);
    }

    [Fact]
    public void ExternalContentDetection()
    {
        Assert.True(HtmlPreparer.HasExternalContent("<img src=\"https://x/y.png\">"));
        Assert.True(HtmlPreparer.HasExternalContent("<td style=\"background:url('http://x/y.png')\">"));
        Assert.False(HtmlPreparer.HasExternalContent("<a href=\"https://x\">Link</a><img src=\"data:image/png;base64,AA\">"));
    }

    [Fact]
    public void HtmlToPlainText()
    {
        var text = HtmlPreparer.HtmlToPlainText("<html><head><style>p{}</style></head><body><p>Hallo&nbsp;Max,</p><p>Zeile&amp;2<br>Ende</p><script>x()</script></body></html>");
        Assert.Equal("Hallo Max,\nZeile&2\nEnde", text);
    }
}

public class MessageComposerTests
{
    private static readonly Account Me = new() { Email = "me@example.org", DisplayName = "Ich" };

    private static MessageDetail Sample() => new()
    {
        Subject = "Projekt",
        From = new MailAddress("Anna", "anna@example.org"),
        To = new[] { new MailAddress(null, "me@example.org"), new MailAddress("Bob", "bob@example.org") },
        Cc = new[] { new MailAddress(null, "carl@example.org"), new MailAddress(null, "ANNA@example.org") },
        Date = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero),
        MessageId = "abc@example.org",
        References = new[] { "root@example.org" },
        TextBody = "Hallo\nWelt",
    };

    [Theory]
    [InlineData("Hallo", "Re: Hallo")]
    [InlineData("Re: Hallo", "Re: Hallo")]
    [InlineData("AW: Hallo", "AW: Hallo")]
    [InlineData("", "Re: ")]
    public void ReplySubject(string input, string expected) => Assert.Equal(expected, MessageComposer.ReplySubject(input));

    [Theory]
    [InlineData("Hallo", "Fwd: Hallo")]
    [InlineData("WG: Hallo", "WG: Hallo")]
    [InlineData("fwd: x", "fwd: x")]
    public void ForwardSubject(string input, string expected) => Assert.Equal(expected, MessageComposer.ForwardSubject(input));

    [Fact]
    public void Reply_QuotesBodyAndSetsThreadHeaders()
    {
        var reply = MessageComposer.CreateReply(Sample(), Me, replyAll: false);
        Assert.Equal("Anna <anna@example.org>", reply.To);
        Assert.Equal("", reply.Cc);
        Assert.Equal("Re: Projekt", reply.Subject);
        Assert.Contains("schrieb Anna <anna@example.org>:\n> Hallo\n> Welt", reply.Body);
        Assert.Equal("abc@example.org", reply.InReplyTo);
        Assert.Equal(new[] { "root@example.org", "abc@example.org" }, reply.References);
    }

    [Fact]
    public void ReplyAll_ExcludesOwnAddressAndDuplicates()
    {
        var reply = MessageComposer.CreateReply(Sample(), Me, replyAll: true);
        Assert.Equal("Anna <anna@example.org>", reply.To);
        Assert.Equal("Bob <bob@example.org>, carl@example.org", reply.Cc);
    }

    [Fact]
    public void Forward_ContainsOriginalHeaders()
    {
        var fwd = MessageComposer.CreateForward(Sample());
        Assert.Equal("Fwd: Projekt", fwd.Subject);
        Assert.Equal("", fwd.To);
        Assert.Contains("---------- Weitergeleitete Nachricht ----------", fwd.Body);
        Assert.Contains("Von: Anna <anna@example.org>", fwd.Body);
        Assert.Contains("Betreff: Projekt", fwd.Body);
        Assert.EndsWith("Hallo\nWelt", fwd.Body);
    }

    [Fact]
    public void BuildMimeMessage_ParsesRecipientsAndThreading()
    {
        var mime = MailService.BuildMimeMessage(Me, new OutgoingMessage
        {
            To = "Anna <anna@example.org>; bob@example.org",
            Cc = "carl@example.org",
            Subject = "Re: Projekt",
            Body = "Text",
            InReplyTo = "abc@example.org",
            References = { "root@example.org", "abc@example.org" },
        });
        Assert.Equal(2, mime.To.Mailboxes.Count());
        Assert.Single(mime.Cc.Mailboxes);
        Assert.Equal("me@example.org", mime.From.Mailboxes.Single().Address);
        Assert.Equal("abc@example.org", mime.InReplyTo);
        Assert.Equal(2, mime.References.Count);
        Assert.EndsWith("@example.org", mime.MessageId);
        Assert.Equal("Text", mime.TextBody);
    }

    [Fact]
    public void BuildMimeMessage_RequiresRecipient() =>
        Assert.Throws<MailServiceException>(() => MailService.BuildMimeMessage(Me, new OutgoingMessage { Subject = "x" }));

    [Fact]
    public void ToDetail_ExtractsBodiesAttachmentsAndInlineImages()
    {
        var builder = new BodyBuilder { HtmlBody = "<img src=\"cid:img1\"><p>Hi</p>", TextBody = "Hi" };
        var image = builder.LinkedResources.Add("bild.png", new byte[] { 9, 9 }, new ContentType("image", "png"));
        image.ContentId = "img1";
        builder.Attachments.Add("rechnung.pdf", new byte[] { 1, 2, 3, 4 }, new ContentType("application", "pdf"));
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Anna", "anna@example.org"));
        message.To.Add(new MailboxAddress(null, "me@example.org"));
        message.Subject = "Test";
        message.Body = builder.ToMessageBody();

        var detail = MailService.ToDetail("A1", "INBOX", 42, message);
        Assert.Equal("Test", detail.Subject);
        Assert.Equal("Anna", detail.From!.Name);
        Assert.Contains("<p>Hi</p>", detail.HtmlBody);
        var attachment = Assert.Single(detail.Attachments);
        Assert.Equal("rechnung.pdf", attachment.FileName);
        Assert.Equal(4, attachment.Size);
        Assert.Equal(new byte[] { 9, 9 }, detail.InlineResources["img1"].Data);
    }

    [Fact]
    public void DefaultDisplayName() =>
        Assert.Equal("Max Mustermann", AccountSetupService.DefaultDisplayName("max.mustermann@gmail.com"));
}
