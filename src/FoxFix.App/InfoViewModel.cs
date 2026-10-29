using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FoxFix.App;

/// <summary>The "Bilgi" pop-up: the version and where the GoodbyeDPI project lives.</summary>
public sealed partial class InfoViewModel : ObservableObject
{
    public const string GoodbyeDpiUrl = "https://github.com/ValdikSS/GoodbyeDPI";
    public const string TurkeyForkUrl = "https://github.com/cagritaskn/GoodbyeDPI-Turkey";

    private readonly Action<string> _openLink;

    public InfoViewModel(Action<string> openLink)
    {
        _openLink = openLink;
    }

    [ObservableProperty]
    private bool _isOpen;

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" : "0.1.0";

    public string VersionText => "Sürüm " + Version;

    [RelayCommand]
    private void Open() => IsOpen = true;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void OpenGoodbyeDpi() => _openLink(GoodbyeDpiUrl);

    [RelayCommand]
    private void OpenTurkeyFork() => _openLink(TurkeyForkUrl);
}
