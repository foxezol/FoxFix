using System.Globalization;
using System.Net;
using System.Text.Json;

namespace FoxFix.Core.Updates;

/// <summary>The newest published release: its version, the page to download it from and what the author wrote about it.</summary>
/// <param name="Sha256">The file's SHA-256 as GitHub published it, or null when there is none.</param>
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

public sealed record ReleaseInfo(Version Version, string Tag, string Url, string Notes, IReadOnlyList<ReleaseAsset>? Files = null)
{
    public IReadOnlyList<ReleaseAsset> Assets { get; } = Files ?? [];
}

/// <summary>Where the newest release comes from. Fakes stand in for it in tests.</summary>
public interface IReleaseSource
{
    /// <summary>Null when nothing has been published yet.</summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct);
}

public enum UpdateStatus
{
    UpToDate,
    Available,
    Failed,
}

/// <param name="Message">A plain sentence the window can show as it is.</param>
public sealed record UpdateCheckResult(UpdateStatus Status, ReleaseInfo? Latest, string Message);

public static class ReleaseVersion
{
    /// <summary>"v0.9.15", "0.9.15-beta" and "0.10" all read as versions; anything else does not.</summary>
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var text = tag?.Trim().TrimStart('v', 'V') ?? "";

        var cut = text.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        var parts = text.Split('.');
        if (parts.Length is < 2 or > 3 || parts.Any(p => !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return false;
        }

        var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        version = new Version(numbers[0], numbers[1], numbers.Length > 2 ? numbers[2] : 0);
        return true;
    }

    /// <summary>Major.Minor.Build with a missing build as 0, so "0.9" and "0.9.0" are the same version.</summary>
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}

/// <summary>
/// Asks GitHub for the newest release. This is the only address FoxFix contacts on its own, and only when the person has
/// left the check switched on or presses the button. Nothing about the computer is sent: just a plain GET.
/// </summary>
public sealed class GitHubReleaseSource : IReleaseSource
{
    /// <summary>The newest releases. Not "/latest": a release whose tag is not a version number (say "Pre-release") would make that answer useless.</summary>
    public const string ListUrl = "https://api.github.com/repos/foxezol/FoxFix/releases?per_page=20";

    private readonly HttpClient _http;

    public GitHubReleaseSource(HttpClient http)
    {
        _http = http;
    }

    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // GitHub refuses requests without a User-Agent. It says only what the program is, not which version or whose.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FoxFix");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(ListUrl, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return ParseNewest(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// The highest version among the published releases. Drafts and pre-releases are skipped, and so is any release whose tag is not a
    /// version number: one oddly named release must not stop the check. Null when none is usable.
    /// Throws <see cref="JsonException"/> when the answer is not a list.
    /// </summary>
    public static ReleaseInfo? ParseNewest(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(Strings.Get(Strings.ReleaseListExpected));
        }

        ReleaseInfo? newest = null;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (Flag(item, "draft") || Flag(item, "prerelease") || !ReleaseVersion.TryParse(Text(item, "tag_name"), out var version))
            {
                continue;
            }

            if (newest is null || version > ReleaseVersion.Normalize(newest.Version))
            {
                newest = Parse(item.GetRawText());
            }
        }

        return newest;
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Reads GitHub's answer. Throws <see cref="JsonException"/> when it is not what was expected.</summary>
    public static ReleaseInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = Text(root, "tag_name");
        if (!ReleaseVersion.TryParse(tag, out var version))
        {
            throw new JsonException(Strings.Format(Strings.ReleaseTagUnreadable, tag));
        }

        return new ReleaseInfo(version, tag, Text(root, "html_url"), Text(root, "body"), ReadAssets(root));
    }

    private static List<ReleaseAsset> ReadAssets(JsonElement root)
    {
        var assets = new List<ReleaseAsset>();
        if (!root.TryGetProperty("assets", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return assets;
        }

        foreach (var item in list.EnumerateArray())
        {
            // GitHub states "sha256:<hex>" for every uploaded file; without it a file cannot be checked.
            var digest = Text(item, "digest");
            var sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
            var size = item.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : 0;
            assets.Add(new ReleaseAsset(Text(item, "name"), Text(item, "browser_download_url"), size, sha));
        }

        return assets;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}

public sealed class UpdateChecker
{
    private readonly IReleaseSource _source;

    public UpdateChecker(IReleaseSource source)
    {
        _source = source;
    }

    /// <summary>A failed check is a result, not an exception: there is nothing a person could do about it in the middle of starting up.</summary>
    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct)
    {
        try
        {
            var latest = await _source.GetLatestAsync(ct);
            if (latest is null || ReleaseVersion.Normalize(latest.Version) <= ReleaseVersion.Normalize(current))
            {
                return new UpdateCheckResult(UpdateStatus.UpToDate, latest, Strings.Get(Strings.UpdateUpToDate));
            }

            return new UpdateCheckResult(
                UpdateStatus.Available, latest, Strings.Format(Strings.UpdateAvailable, FormatVersion(latest.Version)));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
            || (ex is TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested))
        {
            // A timeout surfaces as a cancellation; only the caller's own cancellation is allowed to escape.
            return new UpdateCheckResult(
                UpdateStatus.Failed, null, ErrorCodes.Tag(ErrorCodes.UpdateCheckFailed) + Strings.Format(Strings.UpdateFailed, ex.Message));
        }
    }

    public static string FormatVersion(Version version) => $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
