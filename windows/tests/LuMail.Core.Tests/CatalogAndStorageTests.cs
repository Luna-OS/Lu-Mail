using System.Net.Security;
using System.Xml.Linq;
using LuMail.Core.Catalog;
using LuMail.Core.Mail;
using LuMail.Core.Models;
using LuMail.Core.Resources;
using LuMail.Core.Storage;

namespace LuMail.Core.Tests;

public class ProviderCatalogTests
{
    private static readonly ProviderCatalog Catalog = ProviderCatalog.LoadEmbedded();

    [Fact]
    public void LoadsAllFourProvidersInOrder()
    {
        Assert.Equal(new[] { "gmail", "proton", "mailru", "hostinger" }, Catalog.Providers.Select(p => p.Id));
        Assert.All(Catalog.Providers, p =>
        {
            Assert.False(string.IsNullOrEmpty(p.Name));
            Assert.Matches("^#[0-9A-Fa-f]{6}$", p.Color);
            Assert.False(string.IsNullOrEmpty(p.Imap.Host));
            Assert.True(p.Imap.Port > 0 && p.Smtp.Port > 0);
            Assert.NotEmpty(p.AuthMethods);
            Assert.False(string.IsNullOrEmpty(p.PasswordHelp));
        });
    }

    [Fact]
    public void ProviderDetails()
    {
        var gmail = Catalog.Get("gmail");
        Assert.True(gmail.SupportsOAuth);
        Assert.True(gmail.UsesGmailCategories);
        Assert.Equal("idToken", gmail.OAuth!.EmailSource);
        Assert.False(gmail.SaveSentViaImap);

        var proton = Catalog.Get("proton");
        Assert.False(proton.SupportsOAuth);
        Assert.True(proton.AllowInsecureLoopbackCertificate);
        Assert.Equal(1143, proton.Imap.Port);
        Assert.Equal(1025, proton.Smtp.Port);

        var mailru = Catalog.Get("mailru");
        Assert.Equal("userInfo", mailru.OAuth!.EmailSource);
        Assert.Equal("https://oauth.mail.ru/userinfo", mailru.OAuth.UserInfoEndpoint);

        Assert.True(Catalog.Get("hostinger").SaveSentViaImap);
        Assert.Equal(6, Catalog.FolderRoles.Count);
    }

    [Theory]
    [InlineData("max@gmail.com", "gmail")]
    [InlineData("max@GoogleMail.com", "gmail")]
    [InlineData("a@pm.me", "proton")]
    [InlineData("ivan@inbox.ru", "mailru")]
    [InlineData("ivan@bk.ru", "mailru")]
    [InlineData("me@meinedomain.de", null)]
    [InlineData("", null)]
    public void FindByEmailDomain(string email, string? expected) =>
        Assert.Equal(expected, Catalog.FindByEmailDomain(email)?.Id);

    [Fact]
    public void AllSharedFilesAreEmbedded()
    {
        var names = SharedResources.ResourceNames.ToHashSet();
        foreach (var file in new[] { "providers.json", "categories.json", "lu-theme.json", "login.html", "done.html", "oauth.example.json" })
            Assert.Contains(SharedResources.Prefix + file, names);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("imap.gmail.com", false)]
    [InlineData("127.0.0.1.evil.com", false)]
    [InlineData("10.0.0.1", false)]
    public void LoopbackHostDetection(string host, bool expected) => Assert.Equal(expected, MailConnector.IsLoopbackHost(host));

    [Fact]
    public void InsecureCertificateOnlyForLoopbackAndFlaggedProviders()
    {
        var proton = Catalog.Get("proton");
        var gmail = Catalog.Get("gmail");
        const SslPolicyErrors bad = SslPolicyErrors.RemoteCertificateChainErrors;
        Assert.True(MailConnector.ValidateCertificate(proton, "127.0.0.1", bad));
        Assert.False(MailConnector.ValidateCertificate(proton, "bridge.example.com", bad));
        Assert.False(MailConnector.ValidateCertificate(gmail, "127.0.0.1", bad));
        Assert.True(MailConnector.ValidateCertificate(gmail, "imap.gmail.com", SslPolicyErrors.None));
    }
}

public class ThemeTests
{
    private static string FindRepoFile(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join('/', relative));
    }

    /// <summary>Die WinUI-Farbressourcen in App.xaml müssen exakt lu-theme.json entsprechen.</summary>
    [Fact]
    public void AppXamlColorsMatchLuTheme()
    {
        var theme = ThemeCatalog.LoadEmbedded();
        var xaml = XDocument.Load(FindRepoFile("windows", "src", "LuMail.App", "App.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Dictionary<string, string> BrushesIn(XElement scope) => scope.Descendants()
            .Where(e => e.Name.LocalName == "SolidColorBrush" && e.Attribute(x + "Key") is not null)
            .GroupBy(e => e.Attribute(x + "Key")!.Value)
            .ToDictionary(g => g.Key, g => g.First().Attribute("Color")!.Value.ToUpperInvariant());

        // Dunkles Theme liegt unter "Default" (Fallback auch für Kontrastdesigns) oder "Dark".
        XElement ThemeDictionary(params string[] keys) => xaml.Descendants()
            .First(e => e.Name.LocalName == "ResourceDictionary" && keys.Contains((string?)e.Attribute(x + "Key")));

        foreach (var (themeKeys, tokens) in new[] { (new[] { "Dark", "Default" }, theme.Dark), (new[] { "Light" }, theme.Light) })
        {
            var themeKey = themeKeys[0];
            var brushes = BrushesIn(ThemeDictionary(themeKeys));
            foreach (var (token, hex) in tokens)
            {
                var key = ThemeCatalog.ResourceKey(token);
                Assert.True(brushes.ContainsKey(key), $"{themeKey}: {key} fehlt in App.xaml");
                Assert.Equal(hex.ToUpperInvariant(), brushes[key]);
            }
        }

        var all = BrushesIn(xaml.Root!);
        foreach (var (id, hex) in theme.ProviderColors)
            Assert.Equal(hex.ToUpperInvariant(), all[ThemeCatalog.ResourceKey(id, "LuProvider")]);
        foreach (var (id, hex) in theme.CategoryColors)
            Assert.Equal(hex.ToUpperInvariant(), all[ThemeCatalog.ResourceKey(id, "LuCategory")]);

        var radii = xaml.Descendants().Where(e => e.Name.LocalName == "CornerRadius")
            .ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Value.Trim());
        foreach (var (name, value) in theme.Radius)
            Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), radii[ThemeCatalog.ResourceKey(name, "LuRadius")]);
    }

    [Fact]
    public void ProviderColorsMatchProvidersJson()
    {
        var theme = ThemeCatalog.LoadEmbedded();
        foreach (var p in ProviderCatalog.LoadEmbedded().Providers)
            Assert.Equal(p.Color.ToUpperInvariant(), theme.ProviderColors[p.Id].ToUpperInvariant());
    }

    [Fact]
    public void ParseHex() => Assert.Equal(((byte)255, (byte)0xEA, (byte)0x43, (byte)0x35), ThemeCatalog.ParseHex("#EA4335"));
}

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumail-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void AppPaths_UseInjectedBaseDirectory()
    {
        var paths = new AppPaths(_dir).EnsureCreated();
        Assert.True(Directory.Exists(_dir));
        Assert.Equal(Path.Combine(_dir, "accounts.json"), paths.AccountsFile);
        Assert.Equal(Path.Combine(_dir, "rules.json"), paths.RulesFile);
        Assert.Equal(Path.Combine(_dir, "oauth.json"), paths.OAuthFile);
        Assert.Equal(Path.Combine(_dir, "secrets.bin"), paths.SecretsFile);
    }

    [Fact]
    public void AccountStore_RoundTripUsesSpecJsonFormat()
    {
        var paths = new AppPaths(_dir);
        var store = new AccountStore(paths.AccountsFile);
        Assert.Empty(store.Load());
        store.Upsert(new Account { Id = "ID1", ProviderId = "gmail", Email = "max@gmail.com", DisplayName = "Max", AuthMethod = "oauth2" });
        store.Upsert(new Account { Id = "ID2", ProviderId = "proton", Email = "a@proton.me", DisplayName = "A", AuthMethod = "password" });

        var json = File.ReadAllText(paths.AccountsFile);
        Assert.Contains("\"providerId\": \"gmail\"", json);
        Assert.Contains("\"authMethod\": \"oauth2\"", json);
        Assert.DoesNotContain("isOAuth", json, StringComparison.OrdinalIgnoreCase);

        var loaded = new AccountStore(paths.AccountsFile).Load();
        Assert.Equal(2, loaded.Count);
        Assert.True(loaded[0].IsOAuth);
        store.Remove("ID1");
        Assert.Equal("ID2", Assert.Single(store.Load()).Id);
    }

    [Fact]
    public void SenderRuleStore_PersistsFlatObject_AndReadsAlternativeFormats()
    {
        var path = Path.Combine(_dir, "rules.json");
        var store = new SenderRuleStore(path);
        store.Set("News@Shop.de", MessageCategory.Promotions);
        Assert.Contains("\"news@shop.de\": \"promotions\"", File.ReadAllText(path));
        Assert.Equal(MessageCategory.Promotions, new SenderRuleStore(path).Lookup("news@shop.de"));
        Assert.True(store.Remove("news@shop.de"));
        Assert.Null(new SenderRuleStore(path).Lookup("news@shop.de"));

        Assert.Equal(MessageCategory.Social, SenderRuleStore.Parse("""{"rules":{"x.org":"social"}}""")["x.org"]);
        Assert.Equal(MessageCategory.Updates, SenderRuleStore.Parse("""[{"sender":"a@b.de","category":"updates"}]""")["a@b.de"]);
        Assert.Empty(SenderRuleStore.Parse("kaputt"));
    }

    [Fact]
    public void AppSettings_Defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        var settings = AppSettings.Load(path);
        Assert.Equal(3, settings.RefreshIntervalMinutes);
        Assert.True(settings.SeparateCategories);
        Assert.False(settings.AlwaysLoadExternalImages);
        settings.RefreshIntervalMinutes = 10;
        settings.Save(path);
        Assert.Equal(10, AppSettings.Load(path).RefreshIntervalMinutes);
    }
}
