using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core;
using FoxFix.Core.Overlay;

namespace FoxFix.App;

/// <summary>What the overlay needs from the window system: a window to draw in, a global shortcut and a once-a-second beat.</summary>
public interface IOverlayHost
{
    void Show(OverlaySettings settings);

    void Hide();

    /// <summary>Draws the numbers; the settings say which ones and how.</summary>
    void Update(OverlayReading reading, OverlaySettings settings);

    /// <summary>Takes the shortcut for the whole system; false when another program already has it.</summary>
    bool RegisterHotkey(HotkeyModifiers modifiers, string key, Action pressed);

    void UnregisterHotkey();

    /// <summary>Calls <paramref name="tick"/> once a second until <see cref="StopTimer"/>.</summary>
    void StartTimer(Func<Task> tick);

    void StopTimer();

    /// <summary>
    /// Shows the strip with sample numbers so it can be dragged anywhere; <paramref name="placed"/> gets the new top left corner
    /// each time it is dropped. Clicks are not passed through while this lasts.
    /// </summary>
    void BeginPlacing(OverlaySettings settings, Action<double, double> placed);

    /// <summary>Back to a strip that lets every click through (and gone again if it was only shown for placing).</summary>
    void EndPlacing(bool keepShown);
}

/// <summary>
/// The "Oyun göstergesi" page: the choices (what to show, the background, the corner or a free place, the size, the shortcut) and the switch. While it
/// is switched on the shortcut shows and hides the small overlay; only while the overlay is visible is anything measured.
/// </summary>
public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly SettingsViewModel _settings;
    private readonly IOverlayHost _host;
    private readonly Func<OverlayService> _createService;

    private OverlayService? _service;

    // While the values are being filled in, nothing is saved or applied again.
    private bool _quiet = true;

    public OverlayViewModel(SettingsViewModel settings, IOverlayHost host, Func<OverlayService> createService)
    {
        _settings = settings;
        _host = host;
        _createService = createService;

        var initial = settings.Overlay;
        _enabled = initial.Enabled;
        _hotkey = initial.Hotkey;
        _showFps = initial.ShowFps;
        _showFrameTime = initial.ShowFrameTime;
        _showGpu = initial.ShowGpu;
        _showCpu = initial.ShowCpu;
        _background = initial.Background;
        _corner = initial.Corner;
        _scalePercent = Math.Round(initial.Scale * 100);
        _freeLeft = initial.FreeLeft;
        _freeTop = initial.FreeTop;
        _quiet = false;
    }

    /// <summary>Registers the shortcut when the overlay was left switched on; called once when the app has started.</summary>
    public void Start()
    {
        if (Enabled)
        {
            RegisterHotkey();
        }
    }

    public OverlaySettings Current => new()
    {
        Enabled = Enabled,
        Hotkey = Hotkey,
        ShowFps = ShowFps,
        ShowFrameTime = ShowFrameTime,
        ShowGpu = ShowGpu,
        ShowCpu = ShowCpu,
        Background = Background,
        Corner = Corner,
        Scale = ScalePercent / 100,
        FreeLeft = FreeLeft,
        FreeTop = FreeTop,
    };

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _hotkey = OverlaySettings.DefaultHotkey;

    [ObservableProperty]
    private bool _showFps;

    [ObservableProperty]
    private bool _showFrameTime;

    [ObservableProperty]
    private bool _showGpu;

    [ObservableProperty]
    private bool _showCpu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTranslucent), nameof(IsThemed), nameof(IsNoBackground))]
    private OverlayBackground _background;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopLeft), nameof(IsTopRight), nameof(IsBottomLeft), nameof(IsBottomRight), nameof(IsFree))]
    private OverlayCorner _corner;

    /// <summary>The size as a percentage (50 to 250), set with a slider like a volume.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScaleText))]
    private double _scalePercent = 100;

    public string ScaleText => Strings.Format(Strings.OverlayScaleValue, (int)ScalePercent);

    private double? _freeLeft;
    private double? _freeTop;

    private double? FreeLeft => _freeLeft;

    private double? FreeTop => _freeTop;

    /// <summary>The strip is on screen to be dragged to a place; nothing is measured meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceText))]
    private bool _isPlacing;

    public string PlaceText => Strings.Get(IsPlacing ? Strings.OverlayPlaceDone : Strings.OverlayPlace);

    /// <summary>The overlay is on screen right now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    private bool _isVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = "";

    public bool HasMessage => Message.Length > 0;

    public string ToggleText => Strings.Get(IsVisible ? Strings.OverlayHideNow : Strings.OverlayShowNow);

    public bool AnythingShown => ShowFps || ShowFrameTime || ShowGpu || ShowCpu;

    public bool IsTranslucent { get => Background == OverlayBackground.Translucent; set => Pick(value, () => Background = OverlayBackground.Translucent); }

    public bool IsThemed { get => Background == OverlayBackground.Themed; set => Pick(value, () => Background = OverlayBackground.Themed); }

    public bool IsNoBackground { get => Background == OverlayBackground.None; set => Pick(value, () => Background = OverlayBackground.None); }

    public bool IsTopLeft { get => Corner == OverlayCorner.TopLeft; set => Pick(value, () => Corner = OverlayCorner.TopLeft); }

    public bool IsTopRight { get => Corner == OverlayCorner.TopRight; set => Pick(value, () => Corner = OverlayCorner.TopRight); }

    public bool IsBottomLeft { get => Corner == OverlayCorner.BottomLeft; set => Pick(value, () => Corner = OverlayCorner.BottomLeft); }

    public bool IsBottomRight { get => Corner == OverlayCorner.BottomRight; set => Pick(value, () => Corner = OverlayCorner.BottomRight); }

    public bool IsFree { get => Corner == OverlayCorner.Free; set => Pick(value, () => Corner = OverlayCorner.Free); }

    private static void Pick(bool selected, Action choose)
    {
        if (selected)
        {
            choose();
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        Save();
        if (_quiet)
        {
            return;
        }

        Message = "";
        if (value)
        {
            RegisterHotkey();
        }
        else
        {
            _host.UnregisterHotkey();
            Hide();
        }
    }

    partial void OnShowFpsChanged(bool value) => Changed();

    partial void OnShowFrameTimeChanged(bool value) => Changed();

    partial void OnShowGpuChanged(bool value) => Changed();

    partial void OnShowCpuChanged(bool value) => Changed();

    partial void OnBackgroundChanged(OverlayBackground value) => Changed();

    partial void OnScalePercentChanged(double value) => Changed();

    partial void OnCornerChanged(OverlayCorner value)
    {
        // A fixed corner ends the dragging: the place is not free any more.
        if (value != OverlayCorner.Free && IsPlacing)
        {
            StopPlacing();
        }

        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(AnythingShown));
        Save();

        // The overlay on screen follows the choice at once.
        if (!_quiet && IsVisible)
        {
            _host.Show(Current);
        }
    }

    private void Save()
    {
        if (!_quiet)
        {
            _settings.SetOverlay(Current);
        }
    }

    /// <summary>A new shortcut from the recorder box. It is taken only when it is valid and free; otherwise the old one stays.</summary>
    public bool TrySetHotkey(string text)
    {
        if (!OverlayHotkey.TryParse(text, out var parsed))
        {
            Message = Strings.Get(Strings.OverlayHotkeyInvalid);
            return false;
        }

        var normal = OverlayHotkey.Format(parsed.Modifiers, parsed.Key);
        if (Enabled)
        {
            _host.UnregisterHotkey();
            if (!_host.RegisterHotkey(parsed.Modifiers, parsed.Key, OnHotkey))
            {
                // Back to the one that worked.
                RegisterHotkey();
                Message = Strings.Format(Strings.OverlayHotkeyTaken, normal);
                return false;
            }
        }

        Message = "";
        Hotkey = normal;
        Save();
        return true;
    }

    private void RegisterHotkey()
    {
        if (!OverlayHotkey.TryParse(Hotkey, out var parsed) || !_host.RegisterHotkey(parsed.Modifiers, parsed.Key, OnHotkey))
        {
            Message = Strings.Format(Strings.OverlayHotkeyTaken, Hotkey);
        }
    }

    private void OnHotkey() => ToggleVisible();

    /// <summary>"Yerleştir": the strip appears with sample numbers and can be dragged; pressing again keeps the place.</summary>
    [RelayCommand]
    private void Place()
    {
        if (IsPlacing)
        {
            StopPlacing();
            return;
        }

        // Placing it freely means the free place is the choice.
        Corner = OverlayCorner.Free;
        IsPlacing = true;
        _host.BeginPlacing(Current, (left, top) =>
        {
            _freeLeft = left;
            _freeTop = top;
            Save();
        });
    }

    /// <summary>Ends the dragging; the strip stays on screen only when it was already shown.</summary>
    public void StopPlacing()
    {
        if (!IsPlacing)
        {
            return;
        }

        IsPlacing = false;
        _host.EndPlacing(keepShown: IsVisible);
        if (IsVisible)
        {
            _host.Show(Current);
        }
    }

    /// <summary>The page's button and the shortcut: shows the overlay, or hides it and stops measuring.</summary>
    [RelayCommand]
    private void ToggleVisible()
    {
        if (IsVisible)
        {
            Hide();
        }
        else if (Enabled)
        {
            Show();
        }
        else
        {
            Message = Strings.Get(Strings.OverlayTurnOnFirst);
        }
    }

    private void Show()
    {
        StopPlacing();
        _service = _createService();
        _service.Start();
        Message = _service.FeedError is { } why ? Strings.Format(Strings.OverlayFeedFailed, ErrorCodes.OverlayMeasureFailed, why) : "";
        IsVisible = true;
        _host.Show(Current);
        _host.StartTimer(TickAsync);
    }

    private void Hide()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        _host.StopTimer();
        _host.Hide();
        _service?.Dispose();
        _service = null;
    }

    /// <summary>Called by the host once a second while the overlay is visible.</summary>
    public async Task TickAsync()
    {
        var service = _service;
        if (service is null || !IsVisible)
        {
            return;
        }

        var shown = Current;

        // Reading the graphics card can take a moment, so it is done off the window's thread.
        var reading = await Task.Run(() => service.Sample(shown));
        if (IsVisible && ReferenceEquals(service, _service))
        {
            _host.Update(reading, Current);
        }
    }

    /// <summary>The app is closing: the shortcut goes back to Windows and nothing keeps measuring.</summary>
    public void Shutdown()
    {
        StopPlacing();
        Hide();
        _host.UnregisterHotkey();
    }
}
