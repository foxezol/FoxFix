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

    /// <param name="savedCustom">The Zapret setting edited by hand; used only when the saved choice is a Zapret one and the text is usable.</param>
    public static (GoodbyeDpiProfile? Profile, string? Error) Resolve(string? savedChoice, string? savedCustom = null)
    {
        if (GoodbyeDpiProfiles.Find(savedChoice) is not { } profile)
        {
            return (null, Strings.Get(Strings.FoxDpiNeedsFoxFix));
        }

        return profile.Engine == DpiEngines.ZapretId && ZapretProfiles.Validate(savedCustom) is null
            ? (ZapretProfiles.Custom(savedCustom!), null)
            : (profile, null);
    }
}