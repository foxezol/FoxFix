using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FoxFix.Core.GoodbyeDpi;

namespace FoxFix.Core.Updates;

/// <summary>Fetches one file. A fake stands in for it in tests.</summary>
public interface IPackageDownloader
{
    /// <param name="progress">0 to 1, when the size is known.</param>
    Task DownloadAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct);
}

public sealed class HttpPackageDownloader : IPackageDownloader
{
    private readonly HttpClient _http;

    public HttpPackageDownloader(HttpClient http)
    {
        _http = http;
    }

    /// <summary>No overall timeout: a package takes as long as the connection needs. Waiting for the first answer is still limited by the caller's token.</summary>
    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FoxFix");
        return http;
    }

    public async Task DownloadAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;

            // A server that keeps sending past the limit is not a FoxFix package.
            if (done > SelfUpdater.MaxPackageBytes)
            {
                throw new InvalidDataException("Dosya beklenenden büyük.");
            }

            if (total is > 0)
            {
                progress?.Report((double)done / total.Value);
            }
        }
    }
}

/// <param name="Command">What the detached finishing command runs once FoxFix has closed; null when preparing failed.</param>
public sealed record SelfUpdateResult(bool Success, string Message, string? Command = null);

/// <summary>
/// Installs a newer version from inside the app: downloads the package of the release, checks its SHA-256 against the one
/// GitHub published for that file, and prepares the command that replaces the program once FoxFix has closed and starts it again.
/// Nothing is run unless the file matches. An installed copy gets FoxFixSetup.exe run silently over itself; a ZIP (portable) copy
/// gets its two exe files replaced and keeps its <c>Veri</c> folder. Anything else (a copy run from a build folder, say) is
/// not updated here, only pointed to the release page.
/// </summary>
public sealed class SelfUpdater
{
    public const string SetupAssetName = "FoxFixSetup.exe";

    public const long MaxPackageBytes = 400L * 1024 * 1024;

    /// <summary>Seconds the finishing command waits, so FoxFix has certainly closed before its files are replaced.</summary>
    public const int WaitSeconds = 4;

    public const string WorkFolderName = "guncelleme";

    private const string DownloadPrefix = "/foxezol/FoxFix/releases/download/";

    private static readonly Regex PortableAsset = new(@"^FoxFix-\d+\.\d+\.\d+-tasinabilir\.zip$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IPackageDownloader _downloader;
    private readonly UninstallKind _kind;
    private readonly string? _programFolder;
    private readonly string _workFolder;

    /// <param name="workFolder">Where the package is kept; it must be a folder only administrators can write to, since it is run from there.</param>
    public SelfUpdater(IPackageDownloader downloader, UninstallKind kind, string? programFolder, string workFolder)
    {
        _downloader = downloader;
        _kind = kind;
        _programFolder = programFolder;
        _workFolder = workFolder;
    }

    public ReleaseAsset? PickAsset(ReleaseInfo release) => _kind switch
    {
        UninstallKind.Installed => release.Assets.FirstOrDefault(a => a.Name.Equals(SetupAssetName, StringComparison.OrdinalIgnoreCase)),
        UninstallKind.Portable => release.Assets.FirstOrDefault(a => PortableAsset.IsMatch(a.Name)),
        _ => null,
    };

    /// <summary>True when this copy can be updated here: its kind is known, the release has the right file, and that file can be checked.</summary>
    public bool CanUpdate(ReleaseInfo release) => _programFolder is not null && PickAsset(release) is { } asset && IsVerifiable(asset);

    /// <summary>The file must come from this project's own releases and carry a SHA-256 to be checked against.</summary>
    public static bool IsVerifiable(ReleaseAsset asset) =>
        Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase)
        && asset.Sha256 is not null && Sha256Hex.IsMatch(asset.Sha256)
        && asset.Size is > 0 and <= MaxPackageBytes;

    public async Task<SelfUpdateResult> PrepareAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        if (_programFolder is null || PickAsset(release) is not { } asset)
        {
            return Plain(Strings.UpdateManualOnly);
        }

        if (!IsVerifiable(asset))
        {
            return Fail(Strings.UpdateNoDigest);
        }

        var packageFolder = Path.Combine(_workFolder, UpdateChecker.FormatVersion(release.Version));
        var package = Path.Combine(packageFolder, asset.Name);

        try
        {
            ResetWorkFolder();
            Directory.CreateDirectory(packageFolder);

            await _downloader.DownloadAsync(asset.Url, package, progress, ct);

            if (!await HashMatchesAsync(package, asset.Sha256!, ct))
            {
                ResetWorkFolder();
                return Fail(Strings.UpdateHashMismatch);
            }

            var command = _kind == UninstallKind.Installed
                ? InstallCommand(package)
                : ExtractAndBuildCommand(package, packageFolder);

            return command is null
                ? Fail(Strings.UpdatePathUnsafe)
                : new SelfUpdateResult(true, Strings.Get(Strings.UpdateRestarting), command);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Fail(Strings.UpdateDownloadFailed, ex.Message);
        }
    }

    /// <summary>Waits for FoxFix to close, runs the silent setup and lets it open the new FoxFix when it is done (see FoxFix.iss, /YENIDEN).</summary>
    private string? InstallCommand(string setup) =>
        IsSafe(setup)
            ? $"ping -n {WaitSeconds + 1} 127.0.0.1 >nul & \"{setup}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /YENIDEN=1"
            : null;

    private string? ExtractAndBuildCommand(string zip, string packageFolder)
    {
        var extracted = Path.Combine(packageFolder, "paket");
        ZipFile.ExtractToDirectory(zip, extracted, overwriteFiles: true);

        var newFolder = Path.Combine(extracted, "FoxFix");
        var files = new[] { "FoxFix.exe", "FoxDPI.exe" };
        var missing = files.FirstOrDefault(f => !File.Exists(Path.Combine(newFolder, f)));
        if (missing is not null)
        {
            throw new InvalidDataException($"Pakette {missing} yok.");
        }

        if (!IsSafe(newFolder) || !IsSafe(_programFolder!))
        {
            return null;
        }

        // Only these two files are replaced: Veri, the marker, the licences and anything else beside the exe stay as they are.
        var steps = new List<string> { $"ping -n {WaitSeconds + 1} 127.0.0.1 >nul" };
        steps.AddRange(files.Select(f => $"copy /y \"{Path.Combine(newFolder, f)}\" \"{Path.Combine(_programFolder!, f)}\" >nul"));
        steps.Add($"start \"\" \"{Path.Combine(_programFolder!, "FoxFix.exe")}\"");
        return string.Join(" & ", steps);
    }

    /// <summary>cmd treats these specially even inside quotes; such a path is not used at all.</summary>
    private static bool IsSafe(string path) => path.IndexOfAny(['%', '^', '"', '!']) < 0;

    private static async Task<bool> HashMatchesAsync(string file, string expected, CancellationToken ct)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Only a folder called "guncelleme" inside FoxFix's own data folder is ever emptied.</summary>
    private void ResetWorkFolder()
    {
        var full = Path.GetFullPath(_workFolder).TrimEnd('\\', '/');
        var parent = Path.GetFileName(Path.GetDirectoryName(full) ?? "");
        var ownName = Path.GetFileName(full).Equals(WorkFolderName, StringComparison.OrdinalIgnoreCase);
        var ownParent = parent.Equals("FoxFix", StringComparison.OrdinalIgnoreCase)
            || parent.Equals(AppPaths.PortableFolderName, StringComparison.OrdinalIgnoreCase);

        if (!ownName || !ownParent)
        {
            throw new InvalidOperationException($"Beklenmeyen çalışma klasörü: {full}");
        }

        if (Directory.Exists(full))
        {
            Directory.Delete(full, recursive: true);
        }
    }

    private static SelfUpdateResult Fail(string key, params object[] args) =>
        new(false, args.Length == 0 ? Strings.Get(key) : Strings.Format(key, args));

    private static SelfUpdateResult Plain(string key) => new(false, Strings.Get(key));
}
