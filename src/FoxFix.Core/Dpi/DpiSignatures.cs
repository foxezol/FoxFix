namespace FoxFix.Core.Dpi;

/// <summary>
/// The allowlist of things FoxFix recognizes. Everything is an exact, case-insensitive match:
/// no regex, no substring (plan section 4). Matching on the executable/driver file name makes
/// detection survive a renamed service (plan section 7, item 1).
/// </summary>
internal static class DpiSignatures
{
    public const string GoodbyeDpi = "goodbyedpi";
    public const string WinDivert = "windivert";
    public const string Zapret = "zapret";
    public const string ByeDpi = "byedpi";
    public const string ProxiFyre = "proxifyre";
    public const string DnsCrypt = "dnscrypt";

    /// <summary>Service names distinctive enough to flag on their own (as Possible). "monkey" is deliberately absent.</summary>
    public static readonly IReadOnlyDictionary<string, string> ServiceNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GoodbyeDPI"] = GoodbyeDpi,
            ["WinDivert"] = WinDivert,
            ["WinDivert14"] = WinDivert,
            ["WinDivert1.4"] = WinDivert,
            ["dnscrypt-proxy"] = DnsCrypt,
        };

    /// <summary>Executable or driver file names. Zapret's renamed WinDivert driver is Monkey64.sys.</summary>
    public static readonly IReadOnlyDictionary<string, string> FileNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["goodbyedpi.exe"] = GoodbyeDpi,
            ["winws.exe"] = Zapret,
            ["winws2.exe"] = Zapret,
            ["monkey64.sys"] = Zapret,
            ["windivert.sys"] = WinDivert,
            ["windivert32.sys"] = WinDivert,
            ["windivert64.sys"] = WinDivert,
            ["ciadpi.exe"] = ByeDpi,
            ["proxifyre.exe"] = ProxiFyre,
            ["dnscrypt-proxy.exe"] = DnsCrypt,
        };

    /// <summary>Process names without the .exe extension.</summary>
    public static readonly IReadOnlyDictionary<string, string> ProcessNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["goodbyedpi"] = GoodbyeDpi,
            ["winws"] = Zapret,
            ["winws2"] = Zapret,
            ["ciadpi"] = ByeDpi,
            ["proxifyre"] = ProxiFyre,
            ["dnscrypt-proxy"] = DnsCrypt,
        };

    public static string DnsCryptServiceName => "dnscrypt-proxy";

    public static IReadOnlyList<string> DefaultSplitWireFolders()
    {
        return
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SplitWire-Turkey"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplitWire-Turkey"),
        ];
    }
}
