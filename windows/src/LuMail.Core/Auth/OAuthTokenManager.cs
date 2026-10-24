using System.Collections.Concurrent;
using LuMail.Core.Mail;
using LuMail.Core.Models;
using LuMail.Core.Storage;

namespace LuMail.Core.Auth;

/// <summary>
/// Hält Access-Tokens im Speicher und erneuert sie per Refresh-Token (60 s vor Ablauf).
/// Das Refresh-Token liegt im <see cref="ISecretStore"/>.
/// </summary>
public sealed class OAuthTokenManager
{
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly OAuthClient _client;
    private readonly ISecretStore _secrets;
    private readonly Func<OAuthClientConfig> _config;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> _cache = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public OAuthTokenManager(OAuthClient client, ISecretStore secrets, Func<OAuthClientConfig> config, TimeProvider? time = null)
    {
        _client = client;
        _secrets = secrets;
        _config = config;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Übernimmt frisch erhaltene Tokens (nach der Anmeldung).</summary>
    public void Seed(string accountId, OAuthTokenResponse tokens)
    {
        _cache[accountId] = (tokens.AccessToken, tokens.ExpiresAt);
        if (!string.IsNullOrEmpty(tokens.RefreshToken)) _secrets.Set(SecretKeys.RefreshToken(accountId), tokens.RefreshToken);
    }

    public void Invalidate(string accountId) => _cache.TryRemove(accountId, out _);

    public async Task<string> GetAccessTokenAsync(Account account, Provider provider, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && TryGetCached(account.Id, out var cached)) return cached;

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && TryGetCached(account.Id, out cached)) return cached;

            var refreshToken = _secrets.Get(SecretKeys.RefreshToken(account.Id));
            if (string.IsNullOrEmpty(refreshToken))
                throw new MailServiceException($"Für {account.Email} ist kein Refresh-Token gespeichert. Bitte das Konto neu anmelden.");
            if (provider.OAuth is null || !_config().TryGet(provider.Id, out var credentials))
                throw new MailServiceException(
                    $"Für {provider.Name} ist keine OAuth-Client-ID in oauth.json hinterlegt. Bitte oauth.json ergänzen oder das Konto neu anmelden.");

            var response = await _client.RefreshAsync(provider.OAuth, credentials, refreshToken, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(response.RefreshToken) && response.RefreshToken != refreshToken)
                _secrets.Set(SecretKeys.RefreshToken(account.Id), response.RefreshToken);
            _cache[account.Id] = (response.AccessToken, response.ExpiresAt);
            return response.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool TryGetCached(string accountId, out string token)
    {
        if (_cache.TryGetValue(accountId, out var entry) && entry.ExpiresAt - RefreshMargin > _time.GetUtcNow())
        {
            token = entry.Token;
            return true;
        }
        token = "";
        return false;
    }
}
