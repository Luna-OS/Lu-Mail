using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using LuMail.Core.Models;

namespace LuMail.Core.Mail;

/// <summary>Verbindungsaufbau und Authentifizierung für IMAP/SMTP (MailKit).</summary>
public static class MailConnector
{
    public static SecureSocketOptions ToSocketOptions(string? security) => (security ?? "").Trim().ToLowerInvariant() switch
    {
        "tls" or "ssl" => SecureSocketOptions.SslOnConnect,
        "starttls" => SecureSocketOptions.StartTls,
        "none" or "plain" => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto,
    };

    /// <summary>Nur 127.0.0.1, ::1 und localhost gelten als Loopback.</summary>
    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var h = host.Trim().Trim('[', ']');
        if (string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(h, out var ip) && (ip.Equals(IPAddress.Loopback) || ip.Equals(IPAddress.IPv6Loopback));
    }

    /// <summary>
    /// Zertifikatsprüfung: normal gültige Zertifikate immer; beliebige (selbstsignierte) nur,
    /// wenn der Anbieter <c>allowInsecureLoopbackCertificate</c> setzt UND der Host eine Loopback-Adresse ist.
    /// </summary>
    public static bool ValidateCertificate(Provider provider, string host, SslPolicyErrors errors) =>
        errors == SslPolicyErrors.None || (provider.AllowInsecureLoopbackCertificate && IsLoopbackHost(host));

    public static async Task<ImapClient> ConnectImapAsync(Provider provider, ServerEndpoint endpoint, MailCredential credential, TimeSpan timeout, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = (int)timeout.TotalMilliseconds };
        var host = endpoint.Host;
        if (string.IsNullOrWhiteSpace(host)) throw new MailServiceException("Für dieses Konto ist kein IMAP-Server eingetragen.");
        client.ServerCertificateValidationCallback = (_, _, _, errors) => ValidateCertificate(provider, host, errors);
        try
        {
            await client.ConnectAsync(host, endpoint.Port, ToSocketOptions(endpoint.Security), ct).ConfigureAwait(false);
            await AuthenticateAsync(client, credential, ct).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public static async Task<SmtpClient> ConnectSmtpAsync(Provider provider, ServerEndpoint endpoint, MailCredential credential, TimeSpan timeout, CancellationToken ct)
    {
        var client = new SmtpClient { Timeout = (int)timeout.TotalMilliseconds };
        var host = endpoint.Host;
        if (string.IsNullOrWhiteSpace(host)) throw new MailServiceException("Für dieses Konto ist kein SMTP-Server eingetragen.");
        client.ServerCertificateValidationCallback = (_, _, _, errors) => ValidateCertificate(provider, host, errors);
        try
        {
            await client.ConnectAsync(host, endpoint.Port, ToSocketOptions(endpoint.Security), ct).ConfigureAwait(false);
            await AuthenticateAsync(client, credential, ct).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>XOAUTH2 (SaslMechanismOAuth2) oder Benutzername/Passwort.</summary>
    private static Task AuthenticateAsync(IMailService client, MailCredential credential, CancellationToken ct) =>
        credential.IsOAuth
            ? client.AuthenticateAsync(new SaslMechanismOAuth2(credential.UserName, credential.Secret), ct)
            : client.AuthenticateAsync(credential.UserName, credential.Secret, ct);

    /// <summary>Netzwerk-/Protokollfehler, nach denen eine neue Verbindung sinnvoll ist.</summary>
    public static bool IsConnectionError(Exception ex) => ex is IOException or SocketException or ImapProtocolException
        or SmtpProtocolException or ServiceNotConnectedException or ServiceNotAuthenticatedException or ObjectDisposedException;

    /// <summary>Übersetzt Ausnahmen in deutsche, anzeigbare Meldungen.</summary>
    public static MailServiceException Translate(Exception ex, Provider provider, Account? account = null)
    {
        var who = account?.Email ?? provider.Name;
        var bridgeHint = provider.AllowInsecureLoopbackCertificate
            ? " Läuft die Proton Mail Bridge und ist der Verbindungsmodus auf „SSL“ gestellt?"
            : "";
        return ex switch
        {
            MailServiceException m => m,
            AuthenticationException => new MailServiceException(
                $"Anmeldung für {who} fehlgeschlagen. Bitte Zugangsdaten prüfen oder das Konto neu anmelden.", ex),
            SslHandshakeException => new MailServiceException(
                $"Keine sichere Verbindung zu {provider.Name} möglich (Zertifikat/TLS).{bridgeHint}", ex),
            SocketException => new MailServiceException(
                $"Der Server von {provider.Name} ist nicht erreichbar.{bridgeHint}", ex),
            Auth.OAuthException o => new MailServiceException($"OAuth-Fehler bei {provider.Name}: {o.Message}", ex),
            ImapCommandException c => new MailServiceException($"Der Server von {provider.Name} meldet: {c.ResponseText}", ex),
            SmtpCommandException s => new MailServiceException($"Versand fehlgeschlagen: {s.Message}", ex),
            OperationCanceledException => new MailServiceException("Vorgang abgebrochen.", ex),
            TimeoutException => new MailServiceException($"Zeitüberschreitung bei der Verbindung zu {provider.Name}.{bridgeHint}", ex),
            _ when IsConnectionError(ex) => new MailServiceException(
                $"Die Verbindung zu {provider.Name} wurde unterbrochen.{bridgeHint}", ex),
            _ => new MailServiceException($"Fehler bei {provider.Name}: {ex.Message}", ex),
        };
    }
}
