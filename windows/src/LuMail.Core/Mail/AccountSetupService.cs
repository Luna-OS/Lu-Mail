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

    /// <param name="servers">„Andere (IMAP)“: erkannte Server zum Vorausfüllen der Anmeldeseite.</param>
    public async Task<Account> AddAccountAsync(Provider provider, string? emailHint, CancellationToken ct = default,
        CustomServerSettings? servers = null)
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
            (email, pw, entered, token) => _mail.VerifyPasswordAsync(provider, email, pw, entered, token), ct, servers).ConfigureAwait(false);

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

    /// <summary>
    /// Web-Konto anlegen (FUNKTIONEN §20): keine Geheimnisse, nur das WebView-Profil mit den Cookies des Anbieters.
    /// Ohne bekannte Adresse wird vorerst der Anbietername eingetragen (später aus dem Seitentitel ersetzt).
    /// </summary>
    public Account AddWebAccount(Provider provider, string accountId, string? email)
    {
        var address = string.IsNullOrWhiteSpace(email) ? provider.Name : email.Trim();
        var account = new Account
        {
            Id = accountId,
            ProviderId = provider.Id,
            Email = address,
            DisplayName = address.Contains('@') ? DefaultDisplayName(address) : provider.Name,
            AuthMethod = Account.AuthWeb,
        };
        _accounts.Upsert(account);
        return account;
    }

    public async Task RemoveAccountAsync(Account account)
    {
        await _mail.ForgetAccountAsync(account.Id).ConfigureAwait(false);
        _tokens.Invalidate(account.Id);
        _secrets.Remove(SecretKeys.Password(account.Id));
        _secrets.Remove(SecretKeys.RefreshToken(account.Id));
        _accounts.Remove(account.Id);
        AccountRemoved?.Invoke(this, account);
    }

    /// <summary>
    /// §20 „Jetzt umstellen“: Das neue IMAP-Konto <paramref name="replacement"/> ersetzt das Web-Konto
    /// <paramref name="web"/> – das Web-Konto wird entfernt (AccountRemoved → WebView2-Profil/Cookies löschen).
    /// </summary>
    public async Task ReplaceWebAccountAsync(Account web, Account replacement)
    {
        if (!web.IsWeb) throw new ArgumentException("Nur Web-Konten lassen sich so ersetzen.", nameof(web));
        if (replacement.IsWeb || string.Equals(replacement.Id, web.Id, StringComparison.Ordinal))
            throw new ArgumentException("Das Ersatzkonto muss ein eigenes IMAP-Konto sein.", nameof(replacement));
        await RemoveAccountAsync(web).ConfigureAwait(false);
    }

    /// <summary>
    /// Vorhandenes Konto gleicher Art (IMAP bzw. Web) mit derselben Adresse wiederverwenden, sonst neu anlegen.
    /// Ein Web-Konto wird nie zu einem IMAP-Konto umgebaut – sonst blieben sein WebView2-Profil und der
    /// Web-Eintrag unter derselben Id hängen.
    /// </summary>
    internal Account FindOrCreate(Provider provider, string email, string? displayName, string authMethod)
    {
        var web = string.Equals(authMethod, Account.AuthWeb, StringComparison.OrdinalIgnoreCase);
        var existing = _accounts.Load().FirstOrDefault(a =>
            string.Equals(a.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase)
            && a.IsWeb == web);
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
