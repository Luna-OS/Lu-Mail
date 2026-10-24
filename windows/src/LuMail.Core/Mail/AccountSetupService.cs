using LuMail.Core.Auth;
using LuMail.Core.Login;
using LuMail.Core.Models;
using LuMail.Core.Storage;

namespace LuMail.Core.Mail;

/// <summary>
/// Konto hinzufügen/entfernen. Wählt OAuth, wenn der Anbieter es unterstützt und oauth.json eine Client-ID enthält,
/// sonst die Passwort-Anmeldung im Browser.
/// </summary>
public sealed class AccountSetupService
{
    private readonly BrowserLoginService _login;
    private readonly MailService _mail;
    private readonly AccountStore _accounts;
    private readonly ISecretStore _secrets;
    private readonly OAuthTokenManager _tokens;
    private readonly Func<OAuthClientConfig> _oauthConfig;

    public AccountSetupService(BrowserLoginService login, MailService mail, AccountStore accounts, ISecretStore secrets,
        OAuthTokenManager tokens, Func<OAuthClientConfig> oauthConfig)
    {
        _login = login;
        _mail = mail;
        _accounts = accounts;
        _secrets = secrets;
        _tokens = tokens;
        _oauthConfig = oauthConfig;
    }

    /// <summary>True, wenn für diesen Anbieter OAuth verwendet wird.</summary>
    public bool CanUseOAuth(Provider provider) => provider.SupportsOAuth && _oauthConfig().TryGet(provider.Id, out _);

    public async Task<Account> AddAccountAsync(Provider provider, string? emailHint, CancellationToken ct = default)
    {
        var hint = string.IsNullOrWhiteSpace(emailHint) ? null : emailHint.Trim();

        if (provider.SupportsOAuth && _oauthConfig().TryGet(provider.Id, out var client))
        {
            var result = await _login.LoginWithOAuthAsync(provider, client, hint, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(result.Tokens.RefreshToken))
                throw new MailServiceException($"{provider.Name} hat kein Refresh-Token geliefert. Bitte die Anmeldung wiederholen.");

            var account = FindOrCreate(provider, result.Email, result.DisplayName, Account.AuthOAuth2);
            await _mail.ForgetAccountAsync(account.Id).ConfigureAwait(false);
            _secrets.Remove(SecretKeys.Password(account.Id));
            _tokens.Seed(account.Id, result.Tokens); // speichert auch das Refresh-Token
            _accounts.Upsert(account);
            return account;
        }

        if (!provider.SupportsPassword)
            throw new MailServiceException(provider.SupportsOAuth
                ? $"{provider.Name} unterstützt nur OAuth. Bitte eine eigene Client-ID für „{provider.Id}“ in oauth.json eintragen."
                : $"{provider.Name} unterstützt keine Anmeldeart.");

        var password = await _login.LoginWithPasswordAsync(provider, hint,
            (email, pw, servers, token) => _mail.VerifyPasswordAsync(provider, email, pw, servers, token), ct).ConfigureAwait(false);

        var pwAccount = FindOrCreate(provider, password.Email, null, Account.AuthPassword);
        if (provider.CustomServers) pwAccount.CustomServers = password.Servers;
        await _mail.ForgetAccountAsync(pwAccount.Id).ConfigureAwait(false);
        _secrets.Remove(SecretKeys.RefreshToken(pwAccount.Id));
        _tokens.Invalidate(pwAccount.Id);
        _secrets.Set(SecretKeys.Password(pwAccount.Id), password.Password);
        _accounts.Upsert(pwAccount);
        return pwAccount;
    }

    /// <summary>Wird nach dem Entfernen eines Kontos aufgerufen (z. B. Cache löschen).</summary>
    public event EventHandler<Account>? AccountRemoved;

    public async Task RemoveAccountAsync(Account account)
    {
        await _mail.ForgetAccountAsync(account.Id).ConfigureAwait(false);
        _tokens.Invalidate(account.Id);
        _secrets.Remove(SecretKeys.Password(account.Id));
        _secrets.Remove(SecretKeys.RefreshToken(account.Id));
        _accounts.Remove(account.Id);
        AccountRemoved?.Invoke(this, account);
    }

    private Account FindOrCreate(Provider provider, string email, string? displayName, string authMethod)
    {
        var existing = _accounts.Load().FirstOrDefault(a =>
            string.Equals(a.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));
        var account = existing ?? new Account { ProviderId = provider.Id, Email = email };
        account.AuthMethod = authMethod;
        if (!string.IsNullOrWhiteSpace(displayName)) account.DisplayName = displayName!;
        if (string.IsNullOrWhiteSpace(account.DisplayName)) account.DisplayName = DefaultDisplayName(email);
        return account;
    }

    /// <summary>"max.mustermann@…" → "Max Mustermann".</summary>
    internal static string DefaultDisplayName(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return email;
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
