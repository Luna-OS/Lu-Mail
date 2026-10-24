using LuMail.Core.Catalog;
using LuMail.Core.Classification;
using LuMail.Core.Mail;
using LuMail.Core.Models;

namespace LuMail.Core.Tests;

public class FolderRoleResolverTests
{
    private static readonly FolderRoleResolver Resolver = new(ProviderCatalog.LoadEmbedded().FolderRoles);

    [Fact]
    public void Gmail_SpecialUse_AllMailIsArchiveWhenNoArchiveFlag()
    {
        var folders = new[]
        {
            new FolderInfo("INBOX", '/'),
            new FolderInfo("[Gmail]", '/', Array.Empty<string>(), NoSelect: true),
            new FolderInfo("[Gmail]/Alle Nachrichten", '/', "\\All"),
            new FolderInfo("[Gmail]/Entwürfe", '/', "\\Drafts"),
            new FolderInfo("[Gmail]/Gesendet", '/', "\\Sent"),
            new FolderInfo("[Gmail]/Markiert", '/', "\\Flagged"),
            new FolderInfo("[Gmail]/Papierkorb", '/', "\\Trash"),
            new FolderInfo("[Gmail]/Spam", '/', "\\Junk"),
            new FolderInfo("[Gmail]/Wichtig", '/', "\\Important"),
            new FolderInfo("Rechnungen", '/'),
        };
        var roles = Resolver.Resolve(folders);
        Assert.Equal(FolderRole.Inbox, roles["INBOX"]);
        Assert.Equal(FolderRole.Archive, roles["[Gmail]/Alle Nachrichten"]);
        Assert.Equal(FolderRole.Drafts, roles["[Gmail]/Entwürfe"]);
        Assert.Equal(FolderRole.Sent, roles["[Gmail]/Gesendet"]);
        Assert.Equal(FolderRole.Trash, roles["[Gmail]/Papierkorb"]);
        Assert.Equal(FolderRole.Spam, roles["[Gmail]/Spam"]);
        Assert.False(roles.ContainsKey("[Gmail]"));
        Assert.False(roles.ContainsKey("[Gmail]/Markiert"));
        Assert.False(roles.ContainsKey("Rechnungen"));
    }

    [Fact]
    public void Gmail_ArchiveFlagBeatsAll()
    {
        var roles = Resolver.Resolve(new[]
        {
            new FolderInfo("[Gmail]/All Mail", '/', "\\All"),
            new FolderInfo("Archiv", '/', "\\Archive"),
        });
        Assert.Equal(FolderRole.Archive, roles["Archiv"]);
        Assert.False(roles.ContainsKey("[Gmail]/All Mail"));
    }

    [Fact]
    public void Gmail_EnglishNamesWithoutSpecialUse()
    {
        var roles = Resolver.Resolve(new[]
        {
            new FolderInfo("INBOX", '/'),
            new FolderInfo("[Gmail]/Sent Mail", '/'),
            new FolderInfo("[Gmail]/Drafts", '/'),
            new FolderInfo("[Gmail]/Trash", '/'),
            new FolderInfo("[Gmail]/Spam", '/'),
            new FolderInfo("[Gmail]/All Mail", '/'),
        });
        Assert.Equal(FolderRole.Sent, roles["[Gmail]/Sent Mail"]);
        Assert.Equal(FolderRole.Drafts, roles["[Gmail]/Drafts"]);
        Assert.Equal(FolderRole.Trash, roles["[Gmail]/Trash"]);
        Assert.Equal(FolderRole.Spam, roles["[Gmail]/Spam"]);
        Assert.Equal(FolderRole.Archive, roles["[Gmail]/All Mail"]);
    }

    [Fact]
    public void Hostinger_DotSeparatedNamesUnderInbox()
    {
        var roles = Resolver.Resolve(new[]
        {
            new FolderInfo("INBOX", '.'),
            new FolderInfo("INBOX.Sent", '.'),
            new FolderInfo("INBOX.Drafts", '.'),
            new FolderInfo("INBOX.Junk", '.'),
            new FolderInfo("INBOX.Trash", '.'),
            new FolderInfo("INBOX.Archive", '.'),
            new FolderInfo("INBOX.Projekte", '.'),
        });
        Assert.Equal(FolderRole.Inbox, roles["INBOX"]);
        Assert.Equal(FolderRole.Sent, roles["INBOX.Sent"]);
        Assert.Equal(FolderRole.Drafts, roles["INBOX.Drafts"]);
        Assert.Equal(FolderRole.Spam, roles["INBOX.Junk"]);
        Assert.Equal(FolderRole.Trash, roles["INBOX.Trash"]);
        Assert.Equal(FolderRole.Archive, roles["INBOX.Archive"]);
        Assert.False(roles.ContainsKey("INBOX.Projekte"));
    }

    [Fact]
    public void SeparatorAware_InboxSlashSentMatchesInboxDotSent() =>
        Assert.Equal(FolderRole.Sent, Resolver.Resolve(new[] { new FolderInfo("INBOX/Sent", '/') })["INBOX/Sent"]);

    [Fact]
    public void Names_AreCaseInsensitive_AndRussianNamesWork()
    {
        var roles = Resolver.Resolve(new[]
        {
            new FolderInfo("inbox", '/'),
            new FolderInfo("Отправленные", '/'),
            new FolderInfo("Черновики", '/'),
            new FolderInfo("Спам", '/'),
            new FolderInfo("Корзина", '/'),
            new FolderInfo("Архив", '/'),
        });
        Assert.Equal(FolderRole.Inbox, roles["inbox"]);
        Assert.Equal(FolderRole.Sent, roles["Отправленные"]);
        Assert.Equal(FolderRole.Drafts, roles["Черновики"]);
        Assert.Equal(FolderRole.Spam, roles["Спам"]);
        Assert.Equal(FolderRole.Trash, roles["Корзина"]);
        Assert.Equal(FolderRole.Archive, roles["Архив"]);
    }

    [Fact]
    public void SpecialUse_BeatsName_AndFolderGetsAtMostOneRole()
    {
        var roles = Resolver.Resolve(new[]
        {
            new FolderInfo("Sent", '/'),                 // Name passt, aber …
            new FolderInfo("Gesendete Objekte", '/', "\\Sent"), // … SPECIAL-USE gewinnt
            new FolderInfo("Trash", '/', "\\Trash", "\\Junk"),  // höchstens eine Rolle
            new FolderInfo("Junk", '/'),
        });
        Assert.Equal(FolderRole.Sent, roles["Gesendete Objekte"]);
        Assert.False(roles.ContainsKey("Sent"));
        Assert.Equal(FolderRole.Spam, roles["Trash"]); // spam wird vor trash geprüft
        Assert.False(roles.ContainsKey("Junk"));        // Rolle spam ist schon vergeben
        Assert.DoesNotContain(FolderRole.Trash, roles.Values);
    }

    [Fact]
    public void NoSelectFoldersNeverGetRoles() =>
        Assert.Empty(Resolver.Resolve(new[] { new FolderInfo("Sent", '/', new[] { "\\Sent" }, NoSelect: true) }));

    [Fact]
    public void DisplayPath_StripsInboxPrefix()
    {
        Assert.Equal("Projekte / 2024", MailService.DisplayPath("INBOX.Projekte.2024", '.'));
        Assert.Equal("Rechnungen", MailService.DisplayPath("Rechnungen", '/'));
        Assert.Equal("INBOX", MailService.DisplayPath("INBOX", '/'));
    }
}
