using System.Windows;
using System.Windows.Threading;
using FoxFix.Core.GoodbyeDpi;

namespace FoxFix.App;

/// <summary>
/// The small FoxDPI window and its notification-area icon. They exist exactly while FoxDPI runs: closing the window,
/// "Çıkış" in the icon's menu, or the program ending on its own all end both.
/// </summary>
public sealed class FoxDpiHost
{
    private readonly FoxDpiSession _session;
    private readonly Action _openFoxFix;
    private readonly Action _openSettings;
    private readonly Action<string, string> _notify;
    private readonly Action _ended;
    private readonly ThemeManager _theme;

    private FoxDpiWindow? _window;
    private System.Windows.Forms.NotifyIcon? _tray;
    private DispatcherTimer? _watch;
    private bool _ending;
    private bool _toldAboutTray;
    private bool _windowClosing;

    public FoxDpiHost(FoxDpiSession session, ThemeManager theme, Action openFoxFix, Action openSettings, Action<string, string> notify, Action ended)
    {
        _theme = theme;
        _session = session;
        _openFoxFix = openFoxFix;
        _openSettings = openSettings;
        _notify = notify;
        _ended = ended;
    }

    public bool IsActive => _window is not null && !_ending;

    public void Show(GoodbyeDpiProfile profile)
    {
        if (_window is not null)
        {
            _window.Restore();
            return;
        }

        _ending = false;
        _windowClosing = false;
        _toldAboutTray = false;

        var viewModel = new FoxDpiWindowViewModel(profile.Title, _openFoxFix, _openSettings);
        _window = new FoxDpiWindow(viewModel, _theme);
        _window.Closing += (_, _) =>
        {
            _windowClosing = true;
            _ = EndAsync(null);
        };
        _window.StateChanged += (_, _) => TellAboutTray();

        _tray = CreateTray();

        // Someone may stop GoodbyeDPI from FoxFix's own page, or it may die: then the window has nothing left to say.
        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _watch.Tick += (_, _) =>
        {
            if (!_session.IsRunning)
            {
                // A stopped session has no profile any more; a dead one does.
                _ = EndAsync(_session.Profile is null ? null : "FoxDPI beklenmedik şekilde durdu. Yeniden başlatabilirsin.");
            }
        };
        _watch.Start();

        _window.Show();
    }

    private System.Windows.Forms.NotifyIcon CreateTray()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/FoxDpi.ico")).Stream;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) => _window?.Restore());
        menu.Items.Add("FoxFix'i aç", null, (_, _) => _openFoxFix());
        menu.Items.Add("DPI Ayarları", null, (_, _) => _openSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => _ = EndAsync(null));

        var tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = "FoxDPI aktif",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseDoubleClick += (_, _) => _window?.Restore();
        return tray;
    }

    private void TellAboutTray()
    {
        if (_window is { WindowState: WindowState.Minimized } && !_toldAboutTray)
        {
            _toldAboutTray = true;
            _tray?.ShowBalloonTip(2500, "FoxDPI", "FoxDPI arka planda çalışıyor.", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    /// <summary>Ends GoodbyeDPI, then the window and the icon. Calling it twice is harmless.</summary>
    private async Task EndAsync(string? message)
    {
        if (_ending)
        {
            return;
        }

        _ending = true;
        _watch?.Stop();

        await _session.StopAsync();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        var window = _window;
        _window = null;
        if (!_windowClosing)
        {
            window?.Close();
        }

        if (message is not null)
        {
            _notify("FoxDPI durdu", message);
        }

        _ended();
    }
}
