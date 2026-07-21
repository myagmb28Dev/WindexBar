using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WindexBar.Core.Config;

namespace WindexBar.Core.Updates;

public readonly partial record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    [GeneratedRegex(@"(?<!\d)(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?!\d)")]
    private static partial Regex VersionPattern();

    public static bool TryParse(string? value, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionPattern().Match(value);
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["minor"].Value, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        var patch = 0;
        if (match.Groups["patch"].Success
            && !int.TryParse(match.Groups["patch"].Value, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }

        version = new AppVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;
}

public sealed record AppReleaseInfo(
    AppVersion Version,
    Uri InstallerUri,
    Uri ManifestUri,
    Uri SignatureUri);

public sealed record StagedAppUpdate(
    AppVersion Version,
    string InstallerPath,
    string Sha256);

public interface IAppReleaseSource
{
    Task<AppReleaseInfo> FetchLatestStableReleaseAsync(CancellationToken cancellationToken);
}

public interface IAppUpdateManifestVerifier
{
    bool IsTrustedManifest(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signature, out string? error);
}

public sealed class RsaAppUpdateManifestVerifier : IAppUpdateManifestVerifier
{
    private readonly RSAParameters _publicKey;

    public RsaAppUpdateManifestVerifier(string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        _publicKey = rsa.ExportParameters(false);
    }

    public RsaAppUpdateManifestVerifier(string modulusBase64, string exponentBase64)
    {
        _publicKey = new RSAParameters
        {
            Modulus = Convert.FromBase64String(modulusBase64),
            Exponent = Convert.FromBase64String(exponentBase64)
        };
    }

    public bool IsTrustedManifest(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signature, out string? error)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(_publicKey);
            var valid = rsa.VerifyData(
                manifest,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            error = valid ? null : "The WindexBar update manifest signature is invalid.";
            return valid;
        }
        catch (CryptographicException exception)
        {
            error = $"The WindexBar update manifest signature could not be verified: {exception.Message}";
            return false;
        }
    }
}

public sealed class GithubAppReleaseSource(HttpClient client) : IAppReleaseSource
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/myagmb28Dev/WindexBar/releases/latest");

    public async Task<AppReleaseInfo> FetchLatestStableReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.UserAgent.ParseAdd("WindexBar-AppUpdater");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
        {
            throw new InvalidDataException("The latest GitHub release is not a stable published release.");
        }

        if (!root.TryGetProperty("tag_name", out var tag)
            || !AppVersion.TryParse(tag.GetString(), out var version)
            || !root.TryGetProperty("assets", out var assets))
        {
            throw new InvalidDataException("The latest GitHub release metadata is incomplete.");
        }

        Uri? installerUri = null;
        Uri? manifestUri = null;
        Uri? signatureUri = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement)
                || !asset.TryGetProperty("browser_download_url", out var urlElement)
                || !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var assetUri))
            {
                continue;
            }

            switch (nameElement.GetString())
            {
                case "WindexBarSetup.exe":
                    installerUri = assetUri;
                    break;
                case "update.json":
                    manifestUri = assetUri;
                    break;
                case "update.sig":
                    signatureUri = assetUri;
                    break;
            }
        }

        return installerUri is not null && manifestUri is not null && signatureUri is not null
            ? new AppReleaseInfo(version, installerUri, manifestUri, signatureUri)
            : throw new InvalidDataException("The release does not contain the installer and signed update manifest assets.");
    }
}

public sealed class AppUpdateService(
    HttpClient client,
    IAppReleaseSource releaseSource,
    IAppUpdateManifestVerifier manifestVerifier,
    Func<DateTimeOffset>? now = null)
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FailureRetryDelay = TimeSpan.FromHours(24);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    public async Task<StagedAppUpdate?> CheckAndStageAsync(
        AppUpdateConfig config,
        AppVersion currentVersion,
        string stagingRoot,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!config.AutomaticallyUpdate
            || config.RetryAfter is { } retryAfter && retryAfter > _now()
            || !force && config.LastCheckedAt is { } checkedAt && _now() - checkedAt < CheckInterval)
        {
            return null;
        }

        var release = await releaseSource.FetchLatestStableReleaseAsync(cancellationToken).ConfigureAwait(false);
        config.LastCheckedAt = _now();
        config.LatestVersion = release.Version.ToString();
        if (release.Version <= currentVersion)
        {
            return null;
        }

        var versionDirectory = Path.Combine(stagingRoot, release.Version.ToString());
        Directory.CreateDirectory(versionDirectory);
        var installerPath = Path.Combine(versionDirectory, "WindexBarSetup.exe");
        var manifestBytes = await DownloadSmallFileAsync(release.ManifestUri, 16 * 1024, cancellationToken)
            .ConfigureAwait(false);
        var signatureText = Encoding.UTF8.GetString(
            await DownloadSmallFileAsync(release.SignatureUri, 4 * 1024, cancellationToken).ConfigureAwait(false));
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureText.Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The WindexBar update manifest signature asset is invalid.", exception);
        }

        if (!manifestVerifier.IsTrustedManifest(manifestBytes, signature, out var signatureError))
        {
            throw new InvalidDataException(signatureError ?? "The WindexBar update manifest signature is not trusted.");
        }

        var manifest = ParseManifest(manifestBytes);
        if (manifest.Version != release.Version)
        {
            throw new InvalidDataException("The signed update manifest version does not match the GitHub release tag.");
        }

        await DownloadFileAsync(release.InstallerUri, installerPath, cancellationToken).ConfigureAwait(false);

        var actualHash = await ComputeSha256Async(installerPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(manifest.Sha256, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(installerPath);
            throw new InvalidDataException("The downloaded WindexBar installer SHA-256 does not match the signed update manifest.");
        }

        return new StagedAppUpdate(release.Version, installerPath, actualHash);
    }

    public void RecordFailure(AppUpdateConfig config, string error)
    {
        config.LastFailure = error;
        config.RetryAfter = _now().Add(FailureRetryDelay);
        config.PendingVersion = null;
    }

    private async Task<byte[]> DownloadSmallFileAsync(
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maximumBytes)
        {
            throw new InvalidDataException("A WindexBar update metadata asset is unexpectedly large.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException("A WindexBar update metadata asset is unexpectedly large.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static SignedUpdateManifest ParseManifest(ReadOnlySpan<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes.ToArray());
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schemaVersion)
            || schemaVersion.GetInt32() != 1
            || !root.TryGetProperty("version", out var versionElement)
            || !AppVersion.TryParse(versionElement.GetString(), out var version)
            || !root.TryGetProperty("installer", out var installerElement)
            || !string.Equals(installerElement.GetString(), "WindexBarSetup.exe", StringComparison.Ordinal)
            || !root.TryGetProperty("sha256", out var hashElement))
        {
            throw new InvalidDataException("The signed WindexBar update manifest is invalid.");
        }

        var sha256 = hashElement.GetString();
        if (sha256 is null || sha256.Length != 64 || sha256.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new InvalidDataException("The signed WindexBar update manifest SHA-256 is invalid.");
        }

        return new SignedUpdateManifest(version, sha256.ToUpperInvariant());
    }

    private sealed record SignedUpdateManifest(AppVersion Version, string Sha256);

    private async Task DownloadFileAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + ".download";
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
