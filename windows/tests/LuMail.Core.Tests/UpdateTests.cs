using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuMail.Core.Resources;
using LuMail.Core.Storage;
using LuMail.Core.Update;

namespace LuMail.Core.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v0.10.0", 0, 10, 0)]
    [InlineData("V2.0.11", 2, 0, 11)]
    [InlineData(" 3.4.5 ", 3, 4, 5)]
    public void ParsesValidVersions(string text, int major, int minor, int patch)
    {
        Assert.True(SemanticVersion.TryParse(text, out var v));
        Assert.Equal(new SemanticVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3-beta.1")]
    [InlineData("1.2.3+build")]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsInvalidVersions(string? text) => Assert.False(SemanticVersion.TryParse(text, out _));

    [Fact]
    public void ComparesNumericallyPerPart()
    {
        Assert.True(Parse("0.10.0") > Parse("0.9.9"));
        Assert.True(Parse("1.0.0") > Parse("0.99.99"));
        Assert.True(Parse("1.2.10") > Parse("1.2.9"));
        Assert.True(Parse("1.2.3") == Parse("v1.2.3"));
        Assert.True(Parse("1.2.3") <= Parse("1.2.3"));
    }

    [Theory]
    [InlineData("0.1.0+abc123def", null, "0.1.0")]
    [InlineData("1.4.2", null, "1.4.2")]
    [InlineData(null, "2.3.4.0", "2.3.4")]
    [InlineData("kaputt", null, "0.0.0")]
    [InlineData(null, null, "0.0.0")]
    public void FromAssemblyVersion(string? informational, string? assembly, string expected) =>
        Assert.Equal(expected, SemanticVersion.FromAssemblyVersion(informational, assembly is null ? null : Version.Parse(assembly)).ToString());

    private static SemanticVersion Parse(string s) => SemanticVersion.TryParse(s, out var v) ? v : throw new FormatException(s);
}

public class UpdateConfigTests
{
    [Fact]
    public void EmbeddedUpdateJsonIsLoaded()
    {
        Assert.Contains(SharedResources.Prefix + "update.json", SharedResources.ResourceNames);
        var config = UpdateConfig.LoadEmbedded();
        Assert.Equal("https://api.github.com/repos/Luna-OS/Lu-Mail/releases/latest", config.LatestReleaseApi);
        Assert.Equal("-Windows-x64-Setup.exe", config.AssetSuffixes[UpdatePlatforms.WindowsX64]);
        Assert.Equal("-Windows-arm64-Setup.exe", config.AssetSuffixes[UpdatePlatforms.WindowsArm64]);
        Assert.Equal("SHA256SUMS.txt", config.ChecksumsAsset);
        Assert.True(config.CheckInterval > TimeSpan.Zero);
        Assert.True(config.InitialDelay >= TimeSpan.Zero);
    }

    [Fact]
    public void PlatformFromArchitecture()
    {
        Assert.Equal("windows-arm64", UpdatePlatforms.ForWindows(Architecture.Arm64));
        Assert.Equal("windows-x64", UpdatePlatforms.ForWindows(Architecture.X64));
        Assert.Equal("windows-x64", UpdatePlatforms.ForWindows(Architecture.X86));
    }

    [Fact]
    public void Sha256SumsParsing()
    {
        var h1 = new string('a', 64);
        var h2 = new string('B', 64);
        var sums = Sha256Sums.Parse($"{h1}  Lu-Mail-1.0.0-Windows-x64-Setup.exe\r\n{h2} *Lu-Mail-1.0.0-macOS.dmg\n\nkaputte zeile\n123  zu-kurz.exe\n");
        Assert.Equal(2, sums.Count);
        Assert.Equal(h1, sums["Lu-Mail-1.0.0-Windows-x64-Setup.exe"]);
        Assert.Equal(h2.ToLowerInvariant(), sums["Lu-Mail-1.0.0-macOS.dmg"]);
    }

    [Fact]
    public void UpdateModeIsStoredAsString()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumail-upd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "settings.json");
            Assert.Equal(UpdateMode.Automatic, AppSettings.Load(path).Updates);
            new AppSettings { Updates = UpdateMode.NotifyOnly }.Save(path);
            Assert.Contains("\"updates\": \"NotifyOnly\"", File.ReadAllText(path));
            Assert.Equal(UpdateMode.NotifyOnly, AppSettings.Load(path).Updates);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

public class UpdateServiceTests : IDisposable
{
    private const string Api = "https://api.github.com/repos/Luna-OS/Lu-Mail/releases/latest";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumail-update-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] SetupX64 = Encoding.UTF8.GetBytes("MZ fake x64 setup " + new string('x', 300_000));
    private static readonly byte[] SetupArm64 = Encoding.UTF8.GetBytes("MZ fake arm64 setup");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>Simuliert GitHub: Release-JSON, SHA256SUMS.txt und die Pakete.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(Routes.TryGetValue(url, out var r) ? r() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static string Url(string name) => "https://github.com/Luna-OS/Lu-Mail/releases/download/v1.2.0/" + name;

    private static FakeGitHub NewGitHub(string tag = "v1.2.0", bool includeX64 = true, bool includeSums = true,
        bool includeX64Line = true, byte[]? servedX64 = null, bool prerelease = false)
    {
        var x64 = "Lu-Mail-1.2.0-Windows-x64-Setup.exe";
        var arm = "Lu-Mail-1.2.0-Windows-arm64-Setup.exe";
        var assets = new List<object>();
        if (includeX64) assets.Add(new { name = x64, browser_download_url = Url(x64), size = SetupX64.Length });
        assets.Add(new { name = arm, browser_download_url = Url(arm), size = SetupArm64.Length });
        assets.Add(new { name = "Lu-Mail-1.2.0-macOS.dmg", browser_download_url = Url("Lu-Mail-1.2.0-macOS.dmg"), size = 10 });
        if (includeSums) assets.Add(new { name = "SHA256SUMS.txt", browser_download_url = Url("SHA256SUMS.txt"), size = 300 });
        var release = JsonSerializer.Serialize(new
        {
            tag_name = tag, name = "Lu-Mail 1.2.0", body = "## Neu\n- Dinge", html_url = "https://github.com/Luna-OS/Lu-Mail/releases/tag/v1.2.0",
            draft = false, prerelease, assets,
        });
        var sums = new StringBuilder();
        if (includeX64Line) sums.Append(Sha(SetupX64)).Append("  ").Append(x64).Append('\n');
        sums.Append(Sha(SetupArm64)).Append(" *").Append(arm).Append('\n');

        var gh = new FakeGitHub();
        gh.Routes[Api] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(release, Encoding.UTF8, "application/json") };
        gh.Routes[Url("SHA256SUMS.txt")] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sums.ToString()) };
        gh.Routes[Url(x64)] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(servedX64 ?? SetupX64) };
        gh.Routes[Url(arm)] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupArm64) };
        return gh;
    }

    private UpdateService NewService(FakeGitHub gh, string current = "1.1.0", string platform = UpdatePlatforms.WindowsX64) =>
        new(new HttpClient(gh), UpdateConfig.LoadEmbedded(), SemanticVersion.TryParse(current, out var v) ? v : default, platform, _dir);

    [Fact]
    public async Task NewerVersion_ReturnsUpdateInfo_WithHeaders()
    {
        var gh = NewGitHub();
        var info = await NewService(gh).CheckAsync();

        Assert.NotNull(info);
        Assert.Equal("1.2.0", info!.Version.ToString());
        Assert.Equal("Lu-Mail-1.2.0-Windows-x64-Setup.exe", info.Asset.Name);
        Assert.Equal(Sha(SetupX64), info.Sha256);
        Assert.Equal("https://github.com/Luna-OS/Lu-Mail/releases/tag/v1.2.0", info.HtmlUrl);
        Assert.Contains("Dinge", info.Notes);

        var apiRequest = gh.Requests.First(r => r.RequestUri!.AbsoluteUri == Api);
        Assert.Equal("Lu-Mail/1.1.0", apiRequest.Headers.UserAgent.ToString());
        Assert.Contains(apiRequest.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
    }

    [Theory]
    [InlineData("1.2.0")]
    [InlineData("1.3.0")]
    [InlineData("2.0.0")]
    public async Task EqualOrOlderRelease_ReturnsNull(string current)
    {
        var gh = NewGitHub();
        Assert.Null(await NewService(gh, current).CheckAsync());
        Assert.Single(gh.Requests); // keine weiteren Downloads
    }

    [Theory]
    [InlineData("v1.3.0-beta.1")]
    [InlineData("nightly")]
    [InlineData("v1.3")]
    public async Task NonSemverTags_AreIgnored(string tag) =>
        Assert.Null(await NewService(NewGitHub(tag: tag)).CheckAsync());

    [Fact]
    public async Task PrereleaseFlag_IsIgnored() =>
        Assert.Null(await NewService(NewGitHub(prerelease: true)).CheckAsync());

    [Fact]
    public async Task NoReleaseYet_ReturnsNull()
    {
        var gh = new FakeGitHub();
        Assert.Null(await NewService(gh).CheckAsync());
    }

    [Fact]
    public async Task Arm64_SelectsArm64Asset()
    {
        var info = await NewService(NewGitHub(), platform: UpdatePlatforms.WindowsArm64).CheckAsync();
        Assert.Equal("Lu-Mail-1.2.0-Windows-arm64-Setup.exe", info!.Asset.Name);
        Assert.Equal(Sha(SetupArm64), info.Sha256); // Zeile im Format "<hex> *<name>"
    }

    [Fact]
    public async Task MissingAsset_Throws()
    {
        var ex = await Assert.ThrowsAsync<UpdateException>(() => NewService(NewGitHub(includeX64: false)).CheckAsync());
        Assert.Contains("kein Paket", ex.Message);
    }

    [Fact]
    public async Task MissingChecksumFile_Throws() =>
        await Assert.ThrowsAsync<UpdateException>(() => NewService(NewGitHub(includeSums: false)).CheckAsync());

    [Fact]
    public async Task MissingChecksumLine_Throws()
    {
        var ex = await Assert.ThrowsAsync<UpdateException>(() => NewService(NewGitHub(includeX64Line: false)).CheckAsync());
        Assert.Contains("Prüfsumme", ex.Message);
    }

    [Fact]
    public async Task Download_VerifiesChecksum_AndReportsProgress()
    {
        var service = NewService(NewGitHub());
        var info = await service.CheckAsync();
        var reports = new List<double>();
        var path = await service.DownloadAsync(info!, new SyncProgress(reports.Add));

        Assert.Equal(Path.Combine(_dir, "Lu-Mail-1.2.0-Windows-x64-Setup.exe"), path);
        Assert.Equal(SetupX64, File.ReadAllBytes(path));
        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1]);
        Assert.False(File.Exists(path + ".part"));

        // Zweiter Aufruf: Datei ist schon da und gültig → kein erneuter Download nötig.
        Assert.Equal(path, await service.DownloadAsync(info!));
    }

    [Fact]
    public async Task Download_ChecksumMismatch_DeletesFileAndThrows()
    {
        var tampered = (byte[])SetupX64.Clone();
        tampered[^1] ^= 0xFF;
        var service = NewService(NewGitHub(servedX64: tampered));
        var info = await service.CheckAsync();

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(info!));
        Assert.Contains("Prüfsumme", ex.Message);
        Assert.Empty(Directory.Exists(_dir) ? Directory.GetFiles(_dir) : Array.Empty<string>());
    }

    [Fact]
    public void DefaultDownloadDirectoryIsInTemp() =>
        Assert.Equal(Path.Combine(Path.GetTempPath(), "LuMail-Update"), UpdateService.DefaultDownloadDirectory);

    /// <summary>Progress&lt;T&gt; ruft asynchron auf – für Tests synchron.</summary>
    private sealed class SyncProgress : IProgress<double>
    {
        private readonly Action<double> _report;
        public SyncProgress(Action<double> report) => _report = report;
        public void Report(double value) => _report(value);
    }
}
