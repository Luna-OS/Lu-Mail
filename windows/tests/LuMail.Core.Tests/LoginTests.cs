using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LuMail.Core.Auth;
using LuMail.Core.Catalog;
using LuMail.Core.Login;
using LuMail.Core.Models;

namespace LuMail.Core.Tests;

public class LoginTemplateTests
{
    [Fact]
    public void Html_EscapesAllSpecialCharacters() =>
        Assert.Equal("&lt;a href=&quot;x&quot; onclick=&#39;y&#39;&gt;&amp;&lt;/a&gt;", Html.Escape("<a href=\"x\" onclick='y'>&</a>"));

    [Fact]
    public void LoginPage_ReplacesAllPlaceholdersEscaped()
    {
        var provider = new Provider
        {
            Name = "Evil <b>Mail</b>", Color = "#123456", PasswordLabel = "App-Passwort",
            PasswordHelp = "Hilfe & mehr", HelpUrl = "https://example.org/?a=1&b=\"2\"",
        };
        var html = LoginTemplates.RenderLogin(provider, "STATE-1", "max\"@x.de", "<script>alert(1)</script>");

        Assert.DoesNotContain("{{", html);
        Assert.Contains("Evil &lt;b&gt;Mail&lt;/b&gt;", html);
        Assert.Contains("--provider: #123456", html);
        Assert.Contains("Hilfe &amp; mehr", html);
        Assert.Contains("href=\"https://example.org/?a=1&amp;b=&quot;2&quot;\"", html);
        Assert.Contains("name=\"state\" value=\"STATE-1\"", html);
        Assert.Contains("value=\"max&quot;@x.de\"", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void DonePage_ReplacesTitleAndMessage()
    {
        var html = LoginTemplates.RenderDone("Fertig <1>", "Alles & gut");
        Assert.DoesNotContain("{{", html);
        Assert.Contains("<h1>Fertig &lt;1&gt;</h1>", html);
        Assert.Contains("Alles &amp; gut", html);
    }
}

public class LoopbackLoginTests
{
    private static readonly ProviderCatalog Providers = ProviderCatalog.LoadEmbedded();

    private static HttpClient NewHttp() => new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private static async Task<Uri> WaitForUrl(TaskCompletionSource<Uri> opened) =>
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));

    private static FormUrlEncodedContent Form(params (string, string)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)));

    [Fact]
    public async Task PasswordFlow_WrongState_WrongPassword_ThenSuccess()
    {
        var opened = new TaskCompletionSource<Uri>();
        var service = new BrowserLoginService(u => opened.TrySetResult(u), new OAuthClient(new HttpClient()));
        var hostinger = Providers.Get("hostinger");
        var verifyCalls = 0;

        var login = service.LoginWithPasswordAsync(hostinger, "max@meinedomain.de", (email, password, _, _) =>
        {
            verifyCalls++;
            return Task.FromResult(password == "richtig" ? null : "Anmeldung fehlgeschlagen: Passwort falsch.");
        });

        var url = await WaitForUrl(opened);
        Assert.Equal("127.0.0.1", url.Host);
        var state = url.AbsolutePath["/login/".Length..];
        var origin = $"http://127.0.0.1:{url.Port}";
        using var http = NewHttp();

        // GET /login/{state} → Formular mit Platzhaltern
        var page = await http.GetStringAsync(url);
        Assert.Contains("Bei Hostinger Mail anmelden", page);
        Assert.Contains($"value=\"{state}\"", page);
        Assert.Contains("value=\"max@meinedomain.de\"", page);
        Assert.Contains("Postfach-Passwort", page);

        // GET mit falschem state → 404
        var wrongGet = await http.GetAsync($"{origin}/login/falsch");
        Assert.Equal(HttpStatusCode.NotFound, wrongGet.StatusCode);

        // POST mit falschem state → 400, Prüfung wird nicht aufgerufen
        var wrongState = await http.PostAsync($"{origin}/login", Form(("state", "falsch"), ("email", "max@meinedomain.de"), ("password", "richtig")));
        Assert.Equal(HttpStatusCode.BadRequest, wrongState.StatusCode);
        Assert.Equal(0, verifyCalls);

        // POST mit falschem Passwort → Formular erneut mit {{ERROR}}
        var wrongPassword = await http.PostAsync($"{origin}/login", Form(("state", state), ("email", "max@meinedomain.de"), ("password", "falsch")));
        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        var errorPage = await wrongPassword.Content.ReadAsStringAsync();
        Assert.Contains("Anmeldung fehlgeschlagen: Passwort falsch.", errorPage);
        Assert.Contains("<form", errorPage);
        Assert.False(login.IsCompleted);

        // POST korrekt → done.html und Ergebnis
        var ok = await http.PostAsync($"{origin}/login", Form(("state", state), ("email", "max@meinedomain.de"), ("password", "richtig")));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Anmeldung erfolgreich", await ok.Content.ReadAsStringAsync());

        var result = await login.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("max@meinedomain.de", result.Email);
        Assert.Equal("richtig", result.Password);
        Assert.Equal(2, verifyCalls);

        // Server ist danach gestoppt.
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync(url));
    }

    [Fact]
    public async Task PasswordFlow_WrongHostHeaderRejected()
    {
        var opened = new TaskCompletionSource<Uri>();
        var service = new BrowserLoginService(u => opened.TrySetResult(u), new OAuthClient(new HttpClient()));
        using var cts = new CancellationTokenSource();
        var login = service.LoginWithPasswordAsync(Providers.Get("proton"), null, (_, _, _, _) => Task.FromResult<string?>(null), cts.Token);

        var url = await WaitForUrl(opened);
        using var http = NewHttp();

        var evil = new HttpRequestMessage(HttpMethod.Get, url);
        evil.Headers.Host = $"evil.example:{url.Port}";
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(evil)).StatusCode);

        var wrongPort = new HttpRequestMessage(HttpMethod.Get, url);
        wrongPort.Headers.Host = $"127.0.0.1:{url.Port + 1}";
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(wrongPort)).StatusCode);

        // localhost:<port> ist erlaubt
        var local = new HttpRequestMessage(HttpMethod.Get, url);
        local.Headers.Host = $"localhost:{url.Port}";
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(local)).StatusCode);

        // Fremder Origin beim POST → 403
        var state = url.AbsolutePath["/login/".Length..];
        var post = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{url.Port}/login")
        {
            Content = Form(("state", state), ("email", "a@proton.me"), ("password", "x")),
        };
        post.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(post)).StatusCode);
        Assert.False(login.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
    }

    [Fact]
    public async Task OAuthFlow_StateMismatchRejected_ThenSuccess()
    {
        var idToken = Base64Url.Encode("{}"u8) + "."
                      + Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new { email = "max@gmail.com", name = "Max Muster" })) + ".x";
        var tokenHandler = new FakeHandler(_ => AuthTests.Json(HttpStatusCode.OK,
            $$"""{"access_token":"AT","refresh_token":"RT","expires_in":3600,"id_token":"{{idToken}}"}"""));
        var opened = new TaskCompletionSource<Uri>();
        var service = new BrowserLoginService(u => opened.TrySetResult(u), new OAuthClient(new HttpClient(tokenHandler)));
        var gmail = Providers.Get("gmail");

        var login = service.LoginWithOAuthAsync(gmail, new OAuthClientCredentials("CID", null), null);
        var authUrl = await WaitForUrl(opened);
        var q = OAuthUrls.ParseQuery(authUrl.Query);
        var redirect = q["redirect_uri"];
        var state = q["state"];
        Assert.StartsWith("http://127.0.0.1:", redirect);
        using var http = NewHttp();

        var mismatch = await http.GetAsync($"{redirect}?code=abc&state=falsch");
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.False(login.IsCompleted);
        Assert.Empty(tokenHandler.Requests);

        var ok = await http.GetAsync($"{redirect}?code=abc&state={Uri.EscapeDataString(state)}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Anmeldung erfolgreich", await ok.Content.ReadAsStringAsync());

        var result = await login.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("max@gmail.com", result.Email);
        Assert.Equal("Max Muster", result.DisplayName);
        Assert.Equal("RT", result.Tokens.RefreshToken);
        var form = OAuthUrls.ParseQuery(Assert.Single(tokenHandler.Requests).Body);
        Assert.Equal("abc", form["code"]);
        Assert.Equal(redirect, form["redirect_uri"]);
        Assert.Equal(q["code_challenge"], Pkce.ComputeChallenge(form["code_verifier"]));
    }

    [Fact]
    public async Task OAuthFlow_MailRuLocalhostRedirect_UserInfoEmail_StateIsOneTime()
    {
        var handler = new FakeHandler(req => req.RequestUri!.AbsoluteUri.Contains("userinfo")
            ? AuthTests.Json(HttpStatusCode.OK, """{"email":"ivan@mail.ru"}""")
            : AuthTests.Json(HttpStatusCode.OK, """{"access_token":"AT","refresh_token":"RT","expires_in":3600}"""));
        var opened = new TaskCompletionSource<Uri>();
        var service = new BrowserLoginService(u => opened.TrySetResult(u), new OAuthClient(new HttpClient(handler)));

        var login = service.LoginWithOAuthAsync(Providers.Get("mailru"), new OAuthClientCredentials("CID", "SEC"), null);
        var q = OAuthUrls.ParseQuery((await WaitForUrl(opened)).Query);
        var redirect = new Uri(q["redirect_uri"]);
        Assert.Equal("localhost", redirect.Host);
        using var http = NewHttp();

        // Explizit über 127.0.0.1 verbinden, Host-Header bleibt localhost:<port>.
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{redirect.Port}/callback?code=c1&state={Uri.EscapeDataString(q["state"])}");
        request.Headers.Host = $"localhost:{redirect.Port}";
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(request)).StatusCode);

        var result = await login.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("ivan@mail.ru", result.Email);
        Assert.Contains(handler.Requests, r => r.Uri == "https://oauth.mail.ru/userinfo?access_token=AT");
    }

    [Fact]
    public async Task OAuthFlow_ProviderErrorFailsLogin()
    {
        var opened = new TaskCompletionSource<Uri>();
        var service = new BrowserLoginService(u => opened.TrySetResult(u), new OAuthClient(new HttpClient()));
        var login = service.LoginWithOAuthAsync(Providers.Get("gmail"), new OAuthClientCredentials("CID", null), null);
        var q = OAuthUrls.ParseQuery((await WaitForUrl(opened)).Query);
        using var http = NewHttp();
        var response = await http.GetAsync($"{q["redirect_uri"]}?error=access_denied&state={Uri.EscapeDataString(q["state"])}");
        Assert.Contains("Anmeldung abgebrochen", await response.Content.ReadAsStringAsync());
        var ex = await Assert.ThrowsAsync<OAuthException>(() => login.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("access_denied", ex.Error);
    }

    [Fact]
    public async Task Login_TimesOut()
    {
        var service = new BrowserLoginService(_ => { }, new OAuthClient(new HttpClient())) { Timeout = TimeSpan.FromMilliseconds(200) };
        await Assert.ThrowsAsync<TimeoutException>(() =>
            service.LoginWithPasswordAsync(Providers.Get("proton"), null, (_, _, _, _) => Task.FromResult<string?>(null)));
    }

    [Fact]
    public async Task Server_ListensOnIPv4AndIPv6LoopbackOnSamePort()
    {
        await using var server = LoopbackHttpServer.Start((_, _) => Task.FromResult(LoopbackResponse.Text(200, "ok")));
        using var http = NewHttp();
        Assert.Equal("ok", await http.GetStringAsync($"http://127.0.0.1:{server.Port}/"));
        if (server.ListensOnIPv6)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://[::1]:{server.Port}/");
            req.Headers.Host = $"localhost:{server.Port}";
            Assert.Equal("ok", await (await http.SendAsync(req)).Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Server_RejectsMalformedRequests()
    {
        await using var server = LoopbackHttpServer.Start((_, _) => Task.FromResult(LoopbackResponse.Text(200, "ok")));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, server.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("GARBAGE\r\n\r\n"));
        var buffer = new byte[1024];
        var n = await stream.ReadAsync(buffer);
        Assert.StartsWith("HTTP/1.1 400", Encoding.ASCII.GetString(buffer, 0, n));
    }
}
