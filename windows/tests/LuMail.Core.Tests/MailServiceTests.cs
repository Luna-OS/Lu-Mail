using System.Net;
using System.Net.Sockets;
using LuMail.Core.Auth;
using LuMail.Core.Catalog;
using LuMail.Core.Mail;
using LuMail.Core.Models;
using LuMail.Core.Storage;
using MailKit;
using MailService = LuMail.Core.Mail.MailService;
using MailKit.Security;

namespace LuMail.Core.Tests;

public class MailServiceTests
{
    [Theory]
    [InlineData("tls", SecureSocketOptions.SslOnConnect)]
    [InlineData("TLS", SecureSocketOptions.SslOnConnect)]
    [InlineData("starttls", SecureSocketOptions.StartTls)]
    [InlineData("none", SecureSocketOptions.None)]
    [InlineData("", SecureSocketOptions.Auto)]
    public void SocketOptions(string security, SecureSocketOptions expected) =>
        Assert.Equal(expected, MailConnector.ToSocketOptions(security));

    [Fact]
    public void SpecialUseFromMailKitAttributes()
    {
        var su = MailService.SpecialUse(FolderAttributes.HasNoChildren | FolderAttributes.Sent | FolderAttributes.All);
        Assert.Contains("\\Sent", su);
        Assert.Contains("\\All", su);
        Assert.DoesNotContain("\\Trash", su);
        Assert.Contains("\\Junk", MailService.SpecialUse(FolderAttributes.Junk));
    }

    private static MailService NewService(out InMemorySecretStore secrets)
    {
        secrets = new InMemorySecretStore();
        var store = secrets;
        var tokens = new OAuthTokenManager(new OAuthClient(new HttpClient()), store, () => OAuthClientConfig.Empty);
        return new MailService(ProviderCatalog.LoadEmbedded(), CategoryCatalog.LoadEmbedded(),
            new StoredCredentialSource(store, tokens), RuleStore.InMemory(),
            new MailServiceOptions { Timeout = TimeSpan.FromSeconds(5) });
    }

    private static int ClosedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task VerifyPassword_UnreachableServer_ReturnsGermanMessage()
    {
        await using var service = NewService(out _);
        var provider = new Provider
        {
            Id = "test", Name = "Proton Mail", AllowInsecureLoopbackCertificate = true,
            Imap = new ServerEndpoint { Host = "127.0.0.1", Port = ClosedPort(), Security = "tls" },
        };
        var message = await service.VerifyPasswordAsync(provider, "a@proton.me", "pw");
        Assert.NotNull(message);
        Assert.Contains("Proton Mail", message);
        Assert.Contains("Bridge", message); // Hinweis auf die Proton Mail Bridge
    }

    [Fact]
    public async Task MissingPassword_GivesGermanError()
    {
        await using var service = NewService(out _);
        var account = new Account { Id = "X", ProviderId = "hostinger", Email = "me@example.org", AuthMethod = Account.AuthPassword };
        var ex = await Assert.ThrowsAsync<MailServiceException>(() => service.ListFoldersAsync(account));
        Assert.Contains("kein Passwort gespeichert", ex.Message);
    }
}
