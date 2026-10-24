using System.Net;
using System.Text;
using System.Text.Json;
using LuMail.Core.Auth;
using LuMail.Core.Catalog;
using LuMail.Core.Models;
using LuMail.Core.Storage;
using MailKit.Security;

namespace LuMail.Core.Tests;

public class AuthTests
{
    private static readonly ProviderCatalog Providers = ProviderCatalog.LoadEmbedded();

    [Fact]
    public void Pkce_S256_KnownVector()
    {
        // SHA-256 des Verifiers, base64url ohne Padding (mit Python hashlib gegengeprüft).
        Assert.Equal("TKk0DSwlCkdDyHP0Hl3HapX-TPYSCEM_7iZWby8_xMg",
            Pkce.ComputeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gEVJWfIrw"));
    }

    [Fact]
    public void Pkce_Create_Produces64CharVerifierAndMatchingChallenge()
    {
        var pair = Pkce.Create();
        Assert.Equal(64, pair.Verifier.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", pair.Verifier);
        Assert.Equal(Pkce.ComputeChallenge(pair.Verifier), pair.Challenge);
        Assert.NotEqual(pair.Verifier, Pkce.Create().Verifier);
    }

    [Fact]
    public void State_Is32BytesBase64Url()
    {
        var state = Pkce.CreateState();
        Assert.Equal(43, state.Length);
        Assert.Equal(32, Base64Url.Decode(state).Length);
    }

    [Fact]
    public void AuthorizationUrl_Gmail_ContainsAllParameters()
    {
        var gmail = Providers.Get("gmail");
        var redirect = OAuthUrls.BuildRedirectUri(gmail.OAuth!.RedirectUriTemplate, 51234);
        Assert.Equal("http://127.0.0.1:51234/callback", redirect);

        var url = OAuthUrls.BuildAuthorizationUrl(gmail.OAuth, "my-client.apps.googleusercontent.com", redirect, "STATE123", "CHALLENGE", "max@gmail.com");
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url.AbsoluteUri);

        var q = OAuthUrls.ParseQuery(url.Query);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("my-client.apps.googleusercontent.com", q["client_id"]);
        Assert.Equal(redirect, q["redirect_uri"]);
        Assert.Equal("https://mail.google.com/ openid email", q["scope"]);
        Assert.Equal("STATE123", q["state"]);
        Assert.Equal("CHALLENGE", q["code_challenge"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("max@gmail.com", q["login_hint"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Equal("consent", q["prompt"]);
        Assert.Contains("scope=https%3A%2F%2Fmail.google.com%2F%20openid%20email", url.AbsoluteUri);
    }

    [Fact]
    public void AuthorizationUrl_MailRu_UsesLocalhostAndOmitsEmptyLoginHint()
    {
        var mailru = Providers.Get("mailru");
        var redirect = OAuthUrls.BuildRedirectUri(mailru.OAuth!.RedirectUriTemplate, 4000);
        Assert.Equal("http://localhost:4000/callback", redirect);
        var q = OAuthUrls.ParseQuery(OAuthUrls.BuildAuthorizationUrl(mailru.OAuth, "id", redirect, "s", "c", null).Query);
        Assert.False(q.ContainsKey("login_hint"));
        Assert.Equal("1", q["prompt_force"]);
        Assert.Equal("mail.imap userinfo", q["scope"]);
    }

    [Fact]
    public void XOAuth2_Format()
    {
        Assert.Equal("user=max@gmail.com\u0001auth=Bearer ya29.TOKEN\u0001\u0001", XOAuth2.BuildRaw("max@gmail.com", "ya29.TOKEN"));
        var b64 = XOAuth2.BuildInitialResponse("max@gmail.com", "ya29.TOKEN");
        Assert.Equal("user=max@gmail.com\u0001auth=Bearer ya29.TOKEN\u0001\u0001", Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
    }

    [Fact]
    public void XOAuth2_MatchesMailKitMechanism()
    {
        var sasl = new SaslMechanismOAuth2("max@gmail.com", "ya29.TOKEN");
        Assert.Equal(XOAuth2.BuildInitialResponse("max@gmail.com", "ya29.TOKEN"), sasl.Challenge(string.Empty));
    }

    private static string Jwt(object payload) =>
        Base64Url.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\"}")) + "."
        + Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(payload)) + ".sig";

    [Fact]
    public void IdToken_EmailClaim()
    {
        Assert.Equal("max@gmail.com", JwtDecoder.TryGetEmail(Jwt(new { sub = "1", email = "max@gmail.com", name = "Max" })));
        Assert.Equal("Max", JwtDecoder.TryGetClaim(Jwt(new { name = "Max" }), "name"));
        Assert.Null(JwtDecoder.TryGetEmail(Jwt(new { sub = "1" })));
        Assert.Null(JwtDecoder.TryGetEmail("not-a-jwt"));
        Assert.Null(JwtDecoder.TryGetEmail(null));
    }

    [Fact]
    public void OAuthConfig_IgnoresHintAndEmptyClientIds()
    {
        var example = Resources.SharedResources.ReadText(Resources.SharedResources.OAuthExampleFile);
        Assert.Empty(OAuthClientConfig.Parse(example).Clients);

        var config = OAuthClientConfig.Parse("""
            { "_hinweis": "x", "gmail": { "clientId": " abc ", "clientSecret": "" }, "mailru": { "clientId": "" } }
            """);
        Assert.True(config.TryGet("gmail", out var gmail));
        Assert.Equal("abc", gmail.ClientId);
        Assert.Null(gmail.ClientSecret);
        Assert.False(config.TryGet("mailru", out _));
        Assert.False(OAuthClientConfig.Load("/does/not/exist/oauth.json").TryGet("gmail", out _));
    }

    [Fact]
    public async Task TokenExchange_PostsSpecFormAndParsesResponse()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            """{"access_token":"AT","refresh_token":"RT","expires_in":3599,"id_token":"x.y.z","token_type":"Bearer"}"""));
        var client = new OAuthClient(new HttpClient(handler));
        var settings = Providers.Get("gmail").OAuth!;

        var tokens = await client.ExchangeCodeAsync(settings, new OAuthClientCredentials("CID", "SECRET"), "CODE", "http://127.0.0.1:1/callback", "VERIFIER");

        Assert.Equal("AT", tokens.AccessToken);
        Assert.Equal("RT", tokens.RefreshToken);
        Assert.True(tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(59));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://oauth2.googleapis.com/token", request.Uri);
        var form = OAuthUrls.ParseQuery(request.Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("CODE", form["code"]);
        Assert.Equal("http://127.0.0.1:1/callback", form["redirect_uri"]);
        Assert.Equal("CID", form["client_id"]);
        Assert.Equal("SECRET", form["client_secret"]);
        Assert.Equal("VERIFIER", form["code_verifier"]);
    }

    [Fact]
    public async Task TokenExchange_ErrorResponseThrows()
    {
        var client = new OAuthClient(new HttpClient(new FakeHandler(_ =>
            Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Bad Request"}"""))));
        var ex = await Assert.ThrowsAsync<OAuthException>(() =>
            client.RefreshAsync(Providers.Get("gmail").OAuth!, new OAuthClientCredentials("CID", null), "RT"));
        Assert.Equal("invalid_grant", ex.Error);
    }

    [Fact]
    public async Task UserInfo_PassesAccessTokenAsQueryParameter()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{"email":"ivan@mail.ru","name":"Ivan"}"""));
        var email = await new OAuthClient(new HttpClient(handler)).GetUserInfoEmailAsync("https://oauth.mail.ru/userinfo", "A B");
        Assert.Equal("ivan@mail.ru", email);
        Assert.Equal("https://oauth.mail.ru/userinfo?access_token=A%20B", handler.Requests[0].Uri);
    }

    [Fact]
    public async Task TokenManager_RefreshesSixtySecondsBeforeExpiry()
    {
        var time = new ManualTime(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var calls = 0;
        var handler = new FakeHandler(_ =>
        {
            calls++;
            return Json(HttpStatusCode.OK, $$"""{"access_token":"AT{{calls}}","expires_in":3600,"refresh_token":"RT{{calls}}"}""");
        });
        var secrets = new InMemorySecretStore();
        var account = new Account { Id = "A1", ProviderId = "gmail", Email = "max@gmail.com", AuthMethod = Account.AuthOAuth2 };
        secrets.Set(SecretKeys.RefreshToken("A1"), "RT0");
        var config = OAuthClientConfig.Parse("""{"gmail":{"clientId":"CID"}}""");
        var manager = new OAuthTokenManager(new OAuthClient(new HttpClient(handler), time), secrets, () => config, time);
        var gmail = Providers.Get("gmail");

        Assert.Equal("AT1", await manager.GetAccessTokenAsync(account, gmail));
        Assert.Equal("RT1", secrets.Get(SecretKeys.RefreshToken("A1"))); // rotiertes Refresh-Token gespeichert
        time.Now = time.Now.AddSeconds(3600 - 61);
        Assert.Equal("AT1", await manager.GetAccessTokenAsync(account, gmail));
        time.Now = time.Now.AddSeconds(2); // jetzt < 60 s vor Ablauf
        Assert.Equal("AT2", await manager.GetAccessTokenAsync(account, gmail));
        Assert.Equal(2, calls);
    }

    internal static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now;
    public ManualTime(DateTimeOffset now) => Now = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<(string Uri, string Body)> Requests { get; } = new();

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!.AbsoluteUri, body));
        return _respond(request);
    }
}
