using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FoxFix.Core;
using FoxFix.Core.Overlay;

namespace FoxFix.App;

/// <summary>
/// The small strip in the corner of the screen: "FPS 144 | GPU 18 % | CPU 25 % | MS 6.9". It never takes focus and every click goes
/// through it to the game underneath. It is only seen over games running windowed or borderless.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;

    private OverlayReading _last = OverlayReading.Empty;
    private OverlaySettings _settings = new();

    public OverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            // Click-through and out of the Alt+Tab list: a window the game never notices.
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate);
        };

        SizeChanged += (_, _) => Place();
        MouseLeftButtonDown += OnDragStart;
    }

    private Action<double, double>? _placed;

    /// <summary>While the strip is being placed it takes clicks and can be dragged; otherwise every click goes through it.</summary>
    public void SetPlacing(bool placing, Action<double, double>? placed = null)
    {
        _placed = placing ? placed : null;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, placing ? style & ~WsExTransparent : style | WsExTransparent);
        Cursor = placing ? Cursors.SizeAll : Cursors.Arrow;
        Card.BorderBrush = placing ? (Application.Current.TryFindResource("AccentText") as Brush ?? Brushes.White) : Card.BorderBrush;
        Card.BorderThickness = placing ? new Thickness(2) : new Thickness(1);
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (_placed is null)
        {
            return;
        }

        DragMove();
        _placed(Left, Top);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    /// <summary>Applies the look (background, size) and draws the last numbers again.</summary>
    public void Apply(OverlaySettings settings)
    {
        _settings = settings;
        var dark = Application.Current.TryFindResource("Card") as SolidColorBrush;
        var foreground = Application.Current.TryFindResource("Foreground") as SolidColorBrush;
        var divider = Application.Current.TryFindResource("Divider") as SolidColorBrush;

        switch (settings.Background)
        {
            case OverlayBackground.Themed:
                Card.Background = dark;
                Card.BorderBrush = divider;
                Line.Foreground = foreground;
                Line.Effect = null;
                break;
            case OverlayBackground.Translucent:
                var color = dark?.Color ?? Colors.Black;
                Card.Background = new SolidColorBrush(Color.FromArgb(0xB0, color.R, color.G, color.B));
                Card.BorderBrush = Brushes.Transparent;
                Line.Foreground = foreground;
                Line.Effect = null;
                break;
            default:
                // No background: white numbers with a soft dark halo, readable over any picture.
                Card.Background = Brushes.Transparent;
                Card.BorderBrush = Brushes.Transparent;
                Line.Foreground = Brushes.White;
                Line.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 1 };
                break;
        }

        var scale = settings.Scale;
        Line.FontSize = 15 * scale;
        Card.CornerRadius = new CornerRadius(10 * scale);
        Card.Padding = settings.Background == OverlayBackground.None
            ? new Thickness(4 * scale, 2 * scale, 4 * scale, 2 * scale)
            : new Thickness(12 * scale, 6 * scale, 12 * scale, 6 * scale);
        Draw(_last);
    }

    public void Draw(OverlayReading reading)
    {
        _last = reading;
        Line.Inlines.Clear();
        var muted = Application.Current.TryFindResource("Muted") as Brush ?? Brushes.Gray;
        var labelBrush = _settings.Background == OverlayBackground.None ? new SolidColorBrush(Color.FromRgb(0xD0, 0xD5, 0xDF)) : muted;
        var first = true;

        void Add(string label, string value)
        {
            if (!first)
            {
                Line.Inlines.Add(new System.Windows.Documents.Run("  |  ") { Foreground = labelBrush });
            }

            first = false;
            Line.Inlines.Add(new System.Windows.Documents.Run(label + " ") { Foreground = labelBrush });
            Line.Inlines.Add(new System.Windows.Documents.Run(value) { FontWeight = FontWeights.Bold });
        }

        const string None = "N/A";
        if (_settings.ShowFps)
        {
            Add("FPS", reading.Fps is { } fps ? Math.Round(fps).ToString("0", System.Globalization.CultureInfo.InvariantCulture) : None);
        }

        if (_settings.ShowGpu)
        {
            Add("GPU", reading.GpuPercent is { } gpu ? Math.Round(gpu).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " %" : None);
        }

        if (_settings.ShowCpu)
        {
            Add("CPU", reading.CpuPercent is { } cpu ? Math.Round(cpu).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " %" : None);
        }

        if (_settings.ShowFrameTime)
        {
            Add("MS", reading.FrameMs is { } ms ? ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : None);
        }

        Place();
    }

    /// <summary>In the chosen corner of the main screen's work area (the task bar stays free).</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        const double gap = 16;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        if (_settings.Corner == OverlayCorner.Free && _settings is { FreeLeft: { } freeLeft, FreeTop: { } freeTop })
        {
            // Wherever it was dropped, but never off the screen (a screen may have been unplugged since).
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Left = Math.Clamp(freeLeft, screen.Left, Math.Max(screen.Left, screen.Right - width));
            Top = Math.Clamp(freeTop, screen.Top, Math.Max(screen.Top, screen.Bottom - height));
            return;
        }

        var left = _settings.Corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft or OverlayCorner.Free;
        var top = _settings.Corner is OverlayCorner.TopLeft or OverlayCorner.TopRight or OverlayCorner.Free;
        Left = left ? area.Left + gap : area.Right - width - gap;
        Top = top ? area.Top + gap : area.Bottom - height - gap;
    }
}

/// <summary>The real window system behind <see cref="IOverlayHost"/>: the strip, the global shortcut and a one-second timer.</summary>
public sealed class WpfOverlayHost : IOverlayHost
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4F58;
    private const int ModNoRepeat = 0x4000;

    private OverlayWindow? _window;
    private HwndSource? _source;
    private Action? _pressed;
    private DispatcherTimer? _timer;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public void Show(OverlaySettings settings)
    {
        _window ??= new OverlayWindow();
        _window.Apply(settings);
        if (!_window.IsVisible)
        {
            _window.Show();
        }
    }

    public void Hide()
    {
        _window?.Close();
        _window = null;
    }

    public void BeginPlacing(OverlaySettings settings, Action<double, double> placed)
    {
        Show(settings);
        _window!.Draw(new OverlayReading(144, 6.9, 18, 25));
        _window.SetPlacing(true, placed);
    }

    public void EndPlacing(bool keepShown)
    {
        _window?.SetPlacing(false);
        if (!keepShown)
        {
            Hide();
        }
    }

    public void Update(OverlayReading reading, OverlaySettings settings) => _window?.Draw(reading);

    public bool RegisterHotkey(HotkeyModifiers modifiers, string key, Action pressed)
    {
        if (ToWpfKey(key) is not { } wpfKey)
        {
            return false;
        }

        // A window that only receives messages: it is never seen and takes no focus.
        _source ??= CreateMessageWindow();
        _pressed = pressed;

        uint mods = ModNoRepeat;
        mods |= modifiers.HasFlag(HotkeyModifiers.Alt) ? 1u : 0;
        mods |= modifiers.HasFlag(HotkeyModifiers.Ctrl) ? 2u : 0;
        mods |= modifiers.HasFlag(HotkeyModifiers.Shift) ? 4u : 0;
        mods |= modifiers.HasFlag(HotkeyModifiers.Win) ? 8u : 0;

        return RegisterHotKey(_source.Handle, HotkeyId, mods, (uint)KeyInterop.VirtualKeyFromKey(wpfKey));
    }

    public void UnregisterHotkey()
    {
        if (_source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
        }

        _pressed = null;
    }

    public void StartTimer(Func<Task> tick)
    {
        StopTimer();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) =>
        {
            _timer?.Stop();
            try
            {
                await tick();
            }
            finally
            {
                // The next beat is one second after this one finished, so a slow reading never piles up.
                _timer?.Start();
            }
        };
        _timer.Start();
    }

    public void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private HwndSource CreateMessageWindow()
    {
        var source = new HwndSource(new HwndSourceParameters("FoxFixOverlayHotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        source.AddHook(Hook);
        return source;
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    /// <summary>The key of a shortcut text ("F10", "G", "1") as a WPF key, or null when there is none.</summary>
    public static Key? ToWpfKey(string name)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            return Key.D0 + (name[0] - '0');
        }

        return Enum.TryParse<Key>(name, ignoreCase: true, out var key) ? key : null;
    }

    /// <summary>The text for a pressed key ("F10", "G", "1"), or null for a key that cannot be part of a shortcut.</summary>
    public static string? ToKeyName(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString();
        }

        return key is >= Key.F1 and <= Key.F24 ? key.ToString() : null;
    }
}
