using System.Net;
using System.Security.Cryptography;
using System.Text;
using WindexBar.Core.Config;
using WindexBar.Core.Updates;

namespace WindexBar.Core.Tests;

public sealed class AppUpdateServiceTests
{
    [Theory]
    [InlineData("v1.6", 1, 6, 0)]
    [InlineData("WindexBar 2.3.4", 2, 3, 4)]
    public void ParsesReleaseVersions(string value, int major, int minor, int patch)
    {
        Assert.True(AppVersion.TryParse(value, out var version));
        Assert.Equal(new AppVersion(major, minor, patch), version);
    }

    [Fact]
    public async Task UsesFreshCheckTimestampWithoutNetworkRequest()
    {
        var now = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
        var source = new FakeReleaseSource(Release("1.6.0"));
        using var client = new HttpClient(new StaticHandler(new Dictionary<string, byte[]>()));
        var service = new AppUpdateService(client, source, new FakeVerifier(true), () => now);
        var config = new AppUpdateConfig { LastCheckedAt = now.AddMinutes(-1) };

        var result = await service.CheckAndStageAsync(
            config,
            new AppVersion(1, 5, 5),
            TemporaryDirectory(),
            false,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void ChecksForAReleaseEveryFifteenMinutesWhileRunning()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), AppUpdateService.CheckInterval);
    }

    [Fact]
    public async Task ForcedLaunchCheckIgnoresFreshTimestamp()
    {
        var now = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
        var source = new FakeReleaseSource(Release("1.5.5"));
        using var client = new HttpClient(new StaticHandler(new Dictionary<string, byte[]>()));
        var service = new AppUpdateService(client, source, new FakeVerifier(true), () => now);
        var config = new AppUpdateConfig { LastCheckedAt = now.AddMinutes(-1) };

        var result = await service.CheckAndStageAsync(
            config,
            new AppVersion(1, 5, 5),
            TemporaryDirectory(),
            true,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, source.Calls);
        Assert.Equal(now, config.LastCheckedAt);
    }

    [Fact]
    public async Task StagesNewReleaseAfterHashAndSignatureVerification()
    {
        var payload = Encoding.UTF8.GetBytes("signed installer bytes");
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var responses = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["https://example.test/WindexBarSetup.exe"] = payload,
            ["https://example.test/update.json"] = Manifest("1.6.0", hash),
            ["https://example.test/update.sig"] = Encoding.ASCII.GetBytes(Convert.ToBase64String([1, 2, 3]))
        };
        using var client = new HttpClient(new StaticHandler(responses));
        var verifier = new FakeVerifier(true);
        var service = new AppUpdateService(client, new FakeReleaseSource(Release("1.6.0")), verifier);
        var root = TemporaryDirectory();

        var result = await service.CheckAndStageAsync(
            new AppUpdateConfig(),
            new AppVersion(1, 5, 5),
            root,
            false,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new AppVersion(1, 6, 0), result.Version);
        Assert.Equal(hash, result.Sha256);
        Assert.True(File.Exists(result.InstallerPath));
        Assert.NotNull(verifier.VerifiedManifest);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task RejectsAndDeletesInstallerWhenChecksumDoesNotMatch()
    {
        var responses = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["https://example.test/WindexBarSetup.exe"] = Encoding.UTF8.GetBytes("installer"),
            ["https://example.test/update.json"] = Manifest("1.6.0", new string('A', 64)),
            ["https://example.test/update.sig"] = Encoding.ASCII.GetBytes(Convert.ToBase64String([1, 2, 3]))
        };
        using var client = new HttpClient(new StaticHandler(responses));
        var service = new AppUpdateService(client, new FakeReleaseSource(Release("1.6.0")), new FakeVerifier(true));
        var root = TemporaryDirectory();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAndStageAsync(
            new AppUpdateConfig(),
            new AppVersion(1, 5, 5),
            root,
            false,
            CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(root, "1.6.0", "WindexBarSetup.exe")));
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task ParsesInstallerAndSignedManifestAssetsFromLatestGithubRelease()
    {
        const string json = """
            {
              "tag_name": "v1.6.0",
              "draft": false,
              "prerelease": false,
              "assets": [
                { "name": "WindexBarSetup.exe", "browser_download_url": "https://example.test/setup" },
                { "name": "update.json", "browser_download_url": "https://example.test/manifest" },
                { "name": "update.sig", "browser_download_url": "https://example.test/signature" }
              ]
            }
            """;
        using var client = new HttpClient(new StaticHandler(
            Encoding.UTF8.GetBytes(json),
            "https://api.github.com/repos/myagmb28Dev/WindexBar/releases/latest"));

        var release = await new GithubAppReleaseSource(client).FetchLatestStableReleaseAsync(CancellationToken.None);

        Assert.Equal(new AppVersion(1, 6, 0), release.Version);
        Assert.Equal("https://example.test/setup", release.InstallerUri.AbsoluteUri);
        Assert.Equal("https://example.test/manifest", release.ManifestUri.AbsoluteUri);
        Assert.Equal("https://example.test/signature", release.SignatureUri.AbsoluteUri);
    }

    [Fact]
    public void RsaVerifierAcceptsOnlyMatchingManifestSignature()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var manifest = Manifest("1.6.0", new string('A', 64));
        var signature = rsa.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var verifier = new RsaAppUpdateManifestVerifier(publicKey);

        Assert.True(verifier.IsTrustedManifest(manifest, signature, out var validError));
        Assert.Null(validError);

        manifest[0] ^= 1;
        Assert.False(verifier.IsTrustedManifest(manifest, signature, out var invalidError));
        Assert.NotNull(invalidError);
    }

    private static AppReleaseInfo Release(string version)
    {
        Assert.True(AppVersion.TryParse(version, out var parsed));
        return new AppReleaseInfo(
            parsed,
            new Uri("https://example.test/WindexBarSetup.exe"),
            new Uri("https://example.test/update.json"),
            new Uri("https://example.test/update.sig"));
    }

    private static byte[] Manifest(string version, string sha256) => Encoding.UTF8.GetBytes(
        $"{{\"schemaVersion\":1,\"version\":\"{version}\",\"installer\":\"WindexBarSetup.exe\",\"sha256\":\"{sha256}\"}}");

    private static string TemporaryDirectory() =>
        Path.Combine(Path.GetTempPath(), "WindexBarTests", Guid.NewGuid().ToString("N"));

    private sealed class FakeReleaseSource(AppReleaseInfo release) : IAppReleaseSource
    {
        public int Calls { get; private set; }

        public Task<AppReleaseInfo> FetchLatestStableReleaseAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(release);
        }
    }

    private sealed class FakeVerifier(bool trusted) : IAppUpdateManifestVerifier
    {
        public byte[]? VerifiedManifest { get; private set; }

        public bool IsTrustedManifest(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signature, out string? error)
        {
            VerifiedManifest = manifest.ToArray();
            error = trusted ? null : "untrusted";
            return trusted;
        }
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> _responses;

        public StaticHandler(IReadOnlyDictionary<string, byte[]> responses) => _responses = responses;

        public StaticHandler(byte[] response, string uri) : this(
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [uri] = response })
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null
                && _responses.TryGetValue(request.RequestUri.AbsoluteUri, out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(response)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
