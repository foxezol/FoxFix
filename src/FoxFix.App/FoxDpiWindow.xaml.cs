using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace FoxFix.App;

/// <summary>What the small FoxDPI window shows and can do.</summary>
public sealed class FoxDpiWindowViewModel
{
    public FoxDpiWindowViewModel(string profileTitle, Action openFoxFix, Action openSettings)
    {
        ProfileTitle = profileTitle;
        OpenFoxFixCommand = new RelayCommand(openFoxFix);
        OpenSettingsCommand = new RelayCommand(openSettings);
    }

    public string ProfileTitle { get; }

    public ICommand OpenFoxFixCommand { get; }

    public ICommand OpenSettingsCommand { get; }
}

public partial class FoxDpiWindow : Window
{
    public FoxDpiWindow(FoxDpiWindowViewModel viewModel, ThemeManager? theme = null)
    {
        InitializeComponent();
        DataContext = viewModel;

        if (theme is not null)
        {
            WindowTheme.Follow(this, theme);
        }
    }

    /// <summary>Brings the window back from the notification area.</summary>
    public void Restore()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Minimising sends it to the notification area instead of leaving a taskbar button.</summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }
}
