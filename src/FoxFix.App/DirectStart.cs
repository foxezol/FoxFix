using FoxFix.Core;
using FoxFix.Core.GoodbyeDpi;

namespace FoxFix.App;

/// <summary>
/// FoxDPI.exe starts FoxDPI without opening FoxFix, but only with a setting FoxFix has saved. Opening FoxFix once is the
/// first run: it saves the setting. Without it FoxDPI.exe refuses and says what to do.
/// </summary>
public static class DirectStart
{
    public const string Argument = "--sadece-foxdpi";

    public const int ExitCodeNeedsFoxFix = 4;

    public static (GoodbyeDpiProfile? Profile, string? Error) Resolve(string? savedChoice) =>
        GoodbyeDpiProfiles.Find(savedChoice) is { } profile
            ? (profile, null)
            : (null, Strings.Get(Strings.FoxDpiNeedsFoxFix));
}