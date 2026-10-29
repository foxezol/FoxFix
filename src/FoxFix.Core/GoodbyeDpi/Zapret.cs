namespace FoxFix.Core.GoodbyeDpi;

/// <summary>Where FoxFix keeps its own copy of Zapret (winws.exe), next to the GoodbyeDPI copy.</summary>
public static class ZapretLayout
{
    public const string ExeName = "winws.exe";
    public const string ExcludeListName = "splitwire-exclude.txt";
    public const string ServiceName = "FoxFixZapret";
    public const string InstallFixId = "zapret-install";

    public static string DefaultFolder => Path.Combine(AppPaths.SharedRoot, "Zapret");

    /// <summary>The setting, plus the list of hosts that are left alone (games, update servers) so they are not broken by the trick.</summary>
    public static string BuildArguments(string folder, GoodbyeDpiProfile profile) =>
        $"{profile.Arguments} --hostlist-exclude=\"{Path.Combine(folder, ExcludeListName)}\"";
}

/// <summary>Zapret's files, pinned like GoodbyeDPI's: the ones that were reviewed are the only ones that run.</summary>
public static class ZapretFiles
{
    public static readonly IReadOnlyDictionary<string, string> Sha256 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["winws.exe"] = "a14bff1df6234ea555d2e0c61b589f0707c0b12d6c9b7eeccda76012154996e8",
        ["WinDivert.dll"] = "c1e060ee19444a259b2162f8af0f3fe8c4428a1c6f694dce20de194ac8d7d9a2",
        ["WinDivert64.sys"] = "8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2",
        ["cygwin1.dll"] = "103104a52e5293ce418944725df19e2bf81ad9269b9a120d71d39028e821499b",
        ["splitwire-exclude.txt"] = "f5c723d7c83fa9bcb514a141e43f4d62d190f0e91668f66e9dc4155c4452f387",
    };
}

/// <summary>Zapret's ready-made settings per internet provider, taken from SplitWire-Turkey's preset list.</summary>
public static class ZapretProfiles
{
    private const string Ports = "--wf-tcp=80,443 --wf-udp=443,50000,50100";

    public static IReadOnlyList<GoodbyeDpiProfile> All { get; } =
    [
        Make("zapret-turk-telekom", Strings.ZapretProfileTurkTelekom, $"{Ports} --dpi-desync=fake --dpi-desync-ttl=4"),
        Make("zapret-turk-telekom-alt", Strings.ZapretProfileTurkTelekomAlt, $"{Ports} --dpi-desync=fake --dpi-desync-ttl=3"),
        Make("zapret-superonline", Strings.ZapretProfileSuperonline, $"{Ports} --dpi-desync=fake --dpi-desync-fooling=md5sig"),
        Make("zapret-superonline-alt", Strings.ZapretProfileSuperonlineAlt, "--wf-tcp=80,443 --wf-udp=443,50000-50099 --dpi-desync=fake --dpi-desync-fooling=md5sig --dpi-desync-ttl=3"),
        Make("zapret-kablonet", Strings.ZapretProfileKablonet, $"{Ports} --dpi-desync=fake --dpi-desync-ttl=4"),
        Make("zapret-turkcell", Strings.ZapretProfileTurkcell, $"{Ports} --dpi-desync=fake --dpi-desync-ttl=1 --dpi-desync-autottl=3"),
        Make("zapret-vodafone", Strings.ZapretProfileVodafone, $"{Ports} --dpi-desync=multisplit --dpi-desync-split-pos=2"),
    ];

    public const string CustomId = "zapret-custom";

    /// <summary>A setting the user edited by hand. It is not one of the ready-made ones, so it has its own id.</summary>
    public static GoodbyeDpiProfile Custom(string arguments) =>
        new(CustomId, Strings.ZapretProfileCustom, arguments.Trim(), RedirectsDns: false, Engine: DpiEngines.ZapretId);

    /// <summary>Null when the text can be used; otherwise the reason in plain words. winws needs at least one --wf- filter or it does nothing.</summary>
    public static string? Validate(string? arguments)
    {
        var text = arguments?.Trim() ?? "";
        var ok = text.Length is > 0 and <= 1000
            && text.StartsWith("--", StringComparison.Ordinal)
            && !text.Any(char.IsControl)
            && (text.Contains("--wf-tcp", StringComparison.Ordinal) || text.Contains("--wf-udp", StringComparison.Ordinal));
        return ok ? null : Strings.Get(Strings.ZapretArgumentsInvalid);
    }

    private static GoodbyeDpiProfile Make(string id, string titleKey, string arguments) =>
        new(id, titleKey, arguments, RedirectsDns: false, Engine: DpiEngines.ZapretId);
}