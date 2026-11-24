using FoxFix.Core;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FoxFix.App;

/// <summary>The "Bilgi" pop-up: the version and where the projects FoxDPI is built on live.</summary>
public sealed partial class InfoViewModel : ObservableObject
{
    public const string GoodbyeDpiUrl = "https://github.com/ValdikSS/GoodbyeDPI";
    public const string TurkeyForkUrl = "https://github.com/cagritaskn/GoodbyeDPI-Turkey";

    /// <summary>The project the Windows settings come from (MIT).</summary>
    public const string Win11DebloatUrl = "https://github.com/Raphire/Win11Debloat";

    public const string ErrorCodesUrl = "https://github.com/foxezol/FoxFix/blob/main/docs/HATA-KODLARI.md";

    public const string EnglishErrorCodesUrl = "https://github.com/foxezol/FoxFix/blob/main/docs/ERROR-CODES.md";

    private readonly Action<string> _openLink;

    public InfoViewModel(Action<string> openLink)
    {
        _openLink = openLink;
    }

    [ObservableProperty]
    private bool _isOpen;

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" : "0.1.0";

    public string VersionText => Strings.Format(Strings.VersionText, Version);

    [RelayCommand]
    private void Open() => IsOpen = true;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void OpenGoodbyeDpi() => _openLink(GoodbyeDpiUrl);

    [RelayCommand]
    private void OpenTurkeyFork() => _openLink(TurkeyForkUrl);

    [RelayCommand]
    private void OpenErrorCodes() => _openLink(Strings.IsEnglish ? EnglishErrorCodesUrl : ErrorCodesUrl);

    [RelayCommand]
    private void OpenWin11Debloat() => _openLink(Win11DebloatUrl);
}
