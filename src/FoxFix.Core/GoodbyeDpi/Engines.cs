namespace FoxFix.Core.GoodbyeDpi;

/// <summary>A DPI program FoxFix can run: which file to start, how to tell its process, its ready-made settings and pinned files.</summary>
public sealed record DpiEngine(
    string Id,
    string ExeName,
    string ProcessName,
    IReadOnlyList<GoodbyeDpiProfile> Profiles,
    IReadOnlyDictionary<string, string> Hashes,
    Func<string, GoodbyeDpiProfile, string> BuildArguments)
{
    public string ExePath(string folder) => Path.Combine(folder, ExeName);
}

public static class DpiEngines
{
    public const string GoodbyeDpiId = "goodbyedpi";
    public const string ZapretId = "zapret";

    public static DpiEngine GoodbyeDpi { get; } = new(
        GoodbyeDpiId, GoodbyeDpiLayout.ExeName, "goodbyedpi", GoodbyeDpiProfiles.All, GoodbyeDpiFiles.Sha256, (_, profile) => profile.Arguments);

    public static DpiEngine Zapret { get; } = new(
        ZapretId, ZapretLayout.ExeName, "winws", ZapretProfiles.All, ZapretFiles.Sha256, ZapretLayout.BuildArguments);

    public static DpiEngine Of(GoodbyeDpiProfile profile) => profile.Engine == ZapretId ? Zapret : GoodbyeDpi;
}