using LuMail.Core.Auth;
using LuMail.Core.Models;
using LuMail.Core.Storage;

namespace LuMail.Core.Mail;

/// <summary>Zugangsdaten für IMAP/SMTP: Passwort oder OAuth-Access-Token.</summary>
public sealed record MailCredential(string UserName, string Secret, bool IsOAuth);

/// <summary>Liefert Zugangsdaten für ein Konto.</summary>
public interface IMailCredentialSource
{
    /// <param name="forceRefresh">OAuth: Access-Token auf jeden Fall erneuern (z. B. nach Authentifizierungsfehler).</param>
    Task<MailCredential> GetCredentialAsync(Account account, Provider provider, bool forceRefresh, CancellationToken ct);
}

/// <summary>Standard: Passwort aus dem <see cref="ISecretStore"/>, OAuth über den <see cref="OAuthTokenManager"/>.</summary>
public sealed class StoredCredentialSource : IMailCredentialSource
{
    private readonly ISecretStore _secrets;
    private readonly OAuthTokenManager _tokens;

    public StoredCredentialSource(ISecretStore secrets, OAuthTokenManager tokens)
    {
        _secrets = secrets;
        _tokens = tokens;
    }

    public async Task<MailCredential> GetCredentialAsync(Account account, Provider provider, bool forceRefresh, CancellationToken ct)
    {
        if (account.IsOAuth)
        {
            var token = await _tokens.GetAccessTokenAsync(account, provider, forceRefresh, ct).ConfigureAwait(false);
            return new MailCredential(account.Email, token, true);
        }
        var password = _secrets.Get(SecretKeys.Password(account.Id));
        if (string.IsNullOrEmpty(password))
            throw new MailServiceException($"Für {account.Email} ist kein Passwort gespeichert. Bitte das Konto neu anmelden.");
        return new MailCredential(account.Email, password, false);
    }
}

/// <summary>Fehler mit deutscher, anzeigbarer Meldung.</summary>
public sealed class MailServiceException : Exception
{
    public MailServiceException(string message, Exception? inner = null) : base(message, inner) { }
}
