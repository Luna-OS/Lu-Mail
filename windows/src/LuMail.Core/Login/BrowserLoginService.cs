using System.Security.Cryptography;
using System.Text;
using LuMail.Core.Auth;
using LuMail.Core.Mail;
using LuMail.Core.Models;

namespace LuMail.Core.Login;

/// <summary>Ergebnis der OAuth-Anmeldung.</summary>
public sealed record OAuthLoginResult(string Email, string? DisplayName, OAuthTokenResponse Tokens);

/// <summary>Ergebnis der Passwort-Anmeldung (Zugangsdaten bereits per IMAP geprüft).</summary>
public sealed record PasswordLoginResult(string Email, string Password, CustomServerSettings? Servers = null);

/// <summary>Prüft Zugangsdaten (IMAP LOGIN); liefert null bei Erfolg, sonst eine deutsche Fehlermeldung.</summary>
/// <param name="servers">Nur bei „Andere (IMAP)“: die eingegebenen Server.</param>
public delegate Task<string?> CredentialVerifier(string email, string password, CustomServerSettings? servers, CancellationToken ct);

/// <summary>
/// Anmeldung immer im Standardbrowser (ARCHITEKTUR.md „Anmeldung – immer über den Browser“).
/// Startet einen einmaligen Loopback-HTTP-Server und öffnet den Browser über die injizierte Aktion.
/// </summary>
public sealed class BrowserLoginService
{
    private readonly Action<Uri> _openBrowser;
    private readonly OAuthClient _oauth;

    /// <summary>Gesamt-Timeout eines Anmeldeversuchs (Spezifikation: 10 Minuten).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    public BrowserLoginService(Action<Uri> openBrowser, OAuthClient oauth)
    {
        _openBrowser = openBrowser;
        _oauth = oauth;
    }

    // ------------------------------------------------------------------ OAuth

    /// <summary>
    /// OAuth 2.0 mit PKCE: Browser → authorizationEndpoint, Rückkehr auf <c>GET /callback?code&amp;state</c>,
    /// Token-Tausch, E-Mail-Adresse aus id_token bzw. UserInfo (Fallback: <paramref name="loginHint"/>).
    /// </summary>
    public async Task<OAuthLoginResult> LoginWithOAuthAsync(Provider provider, OAuthClientCredentials client,
        string? loginHint, CancellationToken ct = default)
    {
        var settings = provider.OAuth ?? throw new InvalidOperationException($"{provider.Name} unterstützt kein OAuth.");
        var pkce = Pkce.Create();
        var state = Pkce.CreateState();
        var stateUsed = 0;
        var redirectUri = "";
        var completion = new TaskCompletionSource<OAuthLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<LoopbackResponse> Handle(LoopbackRequest request, CancellationToken rct)
        {
            if (request.Path != "/callback") return NotFound();
            if (request.Method != "GET") return LoopbackResponse.Text(405, "Method Not Allowed");

            request.Query.TryGetValue("state", out var receivedState);
            if (!StateEquals(receivedState, state) || Volatile.Read(ref stateUsed) == 1)
                return Done(400, "Ungültiger Anmeldeversuch",
                    "Dieser Anmelde-Link ist ungültig oder wurde bereits verwendet. Bitte starte die Anmeldung in Lu-Mail erneut.");
            if (Interlocked.Exchange(ref stateUsed, 1) == 1)
                return Done(400, "Ungültiger Anmeldeversuch", "Dieser Anmelde-Link wurde bereits verwendet.");

            if (request.Query.TryGetValue("error", out var error))
            {
                request.Query.TryGetValue("error_description", out var description);
                var text = error == "access_denied"
                    ? "Die Anmeldung wurde abgebrochen."
                    : $"Der Anbieter meldet einen Fehler: {error}{(string.IsNullOrEmpty(description) ? "" : " – " + description)}";
                completion.TrySetException(new OAuthException(text, error));
                return Done(200, "Anmeldung abgebrochen", text + " Du kannst dieses Fenster schließen.");
            }

            if (!request.Query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            {
                completion.TrySetException(new OAuthException("Der Anbieter hat keinen Autorisierungscode geliefert."));
                return Done(400, "Anmeldung fehlgeschlagen", "Der Anbieter hat keinen Autorisierungscode geliefert.");
            }

            try
            {
                var tokens = await _oauth.ExchangeCodeAsync(settings, client, code, redirectUri, pkce.Verifier, rct).ConfigureAwait(false);
                var email = await ResolveEmailAsync(settings, tokens, loginHint, rct).ConfigureAwait(false);
                var name = JwtDecoder.TryGetClaim(tokens.IdToken, "name");
                completion.TrySetResult(new OAuthLoginResult(email, name, tokens));
                return Done(200, "Anmeldung erfolgreich",
                    $"{email} ist jetzt mit Lu-Mail verbunden. Du kannst dieses Fenster schließen und zu Lu-Mail zurückkehren.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                completion.TrySetException(ex);
                return Done(200, "Anmeldung fehlgeschlagen", ex.Message);
            }
        }

        await using var server = LoopbackHttpServer.Start(Handle);
        redirectUri = OAuthUrls.BuildRedirectUri(settings.RedirectUriTemplate, server.Port);
        var url = OAuthUrls.BuildAuthorizationUrl(settings, client.ClientId, redirectUri, state, pkce.Challenge, loginHint);
        _openBrowser(url);
        return await WaitAsync(completion.Task, ct).ConfigureAwait(false);
    }

    private async Task<string> ResolveEmailAsync(OAuthSettings settings, OAuthTokenResponse tokens, string? hint, CancellationToken ct)
    {
        string? email = null;
        if (string.Equals(settings.EmailSource, "idToken", StringComparison.OrdinalIgnoreCase))
        {
            email = JwtDecoder.TryGetEmail(tokens.IdToken);
            // Microsoft (Outlook) liefert die Adresse oft nur als preferred_username.
            if (string.IsNullOrWhiteSpace(email) && JwtDecoder.TryGetClaim(tokens.IdToken, "preferred_username") is { } pu && pu.Contains('@'))
                email = pu;
        }
        else if (string.Equals(settings.EmailSource, "userInfo", StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrEmpty(settings.UserInfoEndpoint))
        {
            try
            {
                email = await _oauth.GetUserInfoEmailAsync(settings.UserInfoEndpoint, tokens.AccessToken, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OAuthException or HttpRequestException)
            {
                email = null; // Fallback auf die eingegebene Adresse.
            }
        }
        if (string.IsNullOrWhiteSpace(email)) email = hint;
        if (string.IsNullOrWhiteSpace(email))
            throw new OAuthException("Die E-Mail-Adresse konnte nicht ermittelt werden. Bitte gib sie in Lu-Mail ein und versuche es erneut.");
        return email.Trim();
    }

    // --------------------------------------------------------------- Passwort

    /// <summary>
    /// Passwort-Anmeldung über die lokale Seite <c>GET /login/{state}</c> und <c>POST /login</c>.
    /// Die Zugangsdaten werden sofort mit <paramref name="verify"/> geprüft; bei Fehler erscheint das Formular erneut mit {{ERROR}}.
    /// </summary>
    public async Task<PasswordLoginResult> LoginWithPasswordAsync(Provider provider, string? emailHint,
        CredentialVerifier verify, CancellationToken ct = default, CustomServerSettings? initialServers = null)
    {
        var state = Pkce.CreateState();
        var stateUsed = 0;
        var port = 0;
        var verifyLock = new SemaphoreSlim(1, 1);
        var completion = new TaskCompletionSource<PasswordLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<LoopbackResponse> Handle(LoopbackRequest request, CancellationToken rct)
        {
            if (request.Method == "GET" && request.Path.StartsWith("/login/", StringComparison.Ordinal))
            {
                var pathState = request.Path["/login/".Length..];
                if (!StateEquals(pathState, state) || Volatile.Read(ref stateUsed) == 1)
                    return Done(404, "Link ungültig",
                        "Dieser Anmelde-Link ist ungültig oder abgelaufen. Bitte starte die Anmeldung in Lu-Mail erneut.");
                return LoopbackResponse.Html(200, LoginTemplates.RenderLogin(provider, state, emailHint, null, servers: initialServers));
            }

            if (request.Path == "/login")
            {
                if (request.Method != "POST") return LoopbackResponse.Text(405, "Method Not Allowed");
                if (!IsAllowedOrigin(request.Header("Origin"), port))
                    return LoopbackResponse.Text(403, "Forbidden: invalid Origin");

                var form = request.Form;
                form.TryGetValue("state", out var formState);
                if (!StateEquals(formState, state) || Volatile.Read(ref stateUsed) == 1)
                    return Done(400, "Ungültiger Anmeldeversuch",
                        "Dieser Anmeldeversuch ist ungültig oder wurde bereits abgeschlossen. Bitte starte die Anmeldung in Lu-Mail erneut.");

                form.TryGetValue("email", out var email);
                form.TryGetValue("password", out var password);
                email = email?.Trim() ?? "";
                CustomServerSettings? servers = null;
                if (provider.CustomServers)
                {
                    var (parsed, serverError) = LoginTemplates.ParseCustomServers(form);
                    servers = parsed;
                    if (serverError is not null)
                        return LoopbackResponse.Html(200, LoginTemplates.RenderLogin(provider, state, email, serverError, servers: servers));
                }
                if (email.Length == 0 || string.IsNullOrEmpty(password) || !email.Contains('@'))
                    return LoopbackResponse.Html(200,
                        LoginTemplates.RenderLogin(provider, state, email, "Bitte gib deine vollständige E-Mail-Adresse und das Passwort ein.", servers: servers));

                await verifyLock.WaitAsync(rct).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref stateUsed) == 1)
                        return Done(400, "Bereits angemeldet", "Diese Anmeldung wurde bereits abgeschlossen.");

                    string? error;
                    try
                    {
                        error = await verify(email, password, servers, rct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        error = ex.Message;
                    }
                    if (error is not null)
                        return LoopbackResponse.Html(200, LoginTemplates.RenderLogin(provider, state, email, error, servers: servers));

                    Interlocked.Exchange(ref stateUsed, 1);
                    completion.TrySetResult(new PasswordLoginResult(email, password, servers));
                    return Done(200, "Anmeldung erfolgreich",
                        $"{email} ist jetzt mit Lu-Mail verbunden. Du kannst dieses Fenster schließen und zu Lu-Mail zurückkehren.");
                }
                finally
                {
                    verifyLock.Release();
                }
            }

            return NotFound();
        }

        await using var server = LoopbackHttpServer.Start(Handle);
        port = server.Port;
        _openBrowser(new Uri($"http://127.0.0.1:{server.Port}/login/{state}"));
        return await WaitAsync(completion.Task, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Helfer

    private async Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return await task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Zeitüberschreitung: Die Anmeldung im Browser wurde nicht innerhalb von "
                                       + $"{(int)Timeout.TotalMinutes} Minuten abgeschlossen.");
        }
    }

    private static bool StateEquals(string? received, string expected)
    {
        if (string.IsNullOrEmpty(received)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(received), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>Browser senden bei POST einen Origin-Header; fehlt er, entscheidet allein der state.</summary>
    private static bool IsAllowedOrigin(string? origin, int port)
    {
        if (string.IsNullOrEmpty(origin)) return true;
        return string.Equals(origin, $"http://127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
               || string.Equals(origin, $"http://localhost:{port}", StringComparison.OrdinalIgnoreCase);
    }

    private static LoopbackResponse Done(int status, string title, string message) =>
        LoopbackResponse.Html(status, LoginTemplates.RenderDone(title, message));

    private static LoopbackResponse NotFound() => Done(404, "Nicht gefunden", "Diese Seite gibt es nicht.");
}
