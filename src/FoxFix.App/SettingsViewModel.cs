using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core.Settings;

namespace FoxFix.App;

/// <summary>One ready-made colour on the Ayarlar page.</summary>
public sealed partial class AccentSwatch : ObservableObject
{
    public AccentSwatch(string name, string hex)
    {
        Name = name;
        Hex = hex;
        ColorMath.TryParse(hex, out var color);
        Fill = new SolidColorBrush(color);
        Fill.Freeze();
    }

    public string Name { get; }

    public string Hex { get; }

    public Brush Fill { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// The "Ayarlar" page: update checks, the look of the app (light or dark, accent colour, logo colour) and removing FoxFix.
/// Every change is applied at once and saved at once; there is no "Kaydet" button to forget.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static IReadOnlyList<(string Name, string Hex)> Presets { get; } =
    [
        ("Mor", AppSettings.DefaultAccent),
        ("Mavi", "#2F7DF6"),
        ("Camgöbeği", "#14A3C7"),
        ("Yeşil", "#1FA971"),
        ("Turuncu", "#F28C28"),
        ("Kırmızı", "#E5484D"),
        ("Pembe", "#E5509F"),
    ];

    private readonly AppSettingsStore _store;
    private readonly Action<AppSettings> _apply;

    // While the values are being filled in (or one field follows another) nothing is saved or applied again.
    private bool _quiet = true;

    public SettingsViewModel(AppSettingsStore store, AppSettings initial, Action<AppSettings> apply, UpdateViewModel? updates = null)
    {
        _store = store;
        _apply = apply;
        Updates = updates;

        var settings = initial.Sanitized();
        _autoCheckUpdates = settings.AutoCheckUpdates;
        _theme = settings.Theme;
        _accent = settings.AccentColor;
        _accentInput = settings.AccentColor;
        _recolorLogo = settings.RecolorLogo;

        foreach (var (name, hex) in Presets)
        {
            Swatches.Add(new AccentSwatch(name, hex) { IsSelected = hex.Equals(_accent, StringComparison.OrdinalIgnoreCase) });
        }

        _quiet = false;
    }

    /// <summary>The update checks; null in tests that do not need them.</summary>
    public UpdateViewModel? Updates { get; }

    public ObservableCollection<AccentSwatch> Swatches { get; } = [];

    [ObservableProperty]
    private bool _autoCheckUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme))]
    private ThemeMode _theme;

    public bool IsSystemTheme
    {
        get => Theme == ThemeMode.System;
        set => Choose(value, ThemeMode.System);
    }

    public bool IsLightTheme
    {
        get => Theme == ThemeMode.Light;
        set => Choose(value, ThemeMode.Light);
    }

    public bool IsDarkTheme
    {
        get => Theme == ThemeMode.Dark;
        set => Choose(value, ThemeMode.Dark);
    }

    private void Choose(bool selected, ThemeMode mode)
    {
        if (selected)
        {
            Theme = mode;
        }
    }

    /// <summary>The accent colour in use, "#RRGGBB".</summary>
    [ObservableProperty]
    private string _accent = AppSettings.DefaultAccent;

    /// <summary>What is typed in the colour box. It becomes the accent colour as soon as it is a complete colour.</summary>
    [ObservableProperty]
    private string _accentInput = AppSettings.DefaultAccent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccentError))]
    private string _accentError = "";

    public bool HasAccentError => AccentError.Length > 0;

    [ObservableProperty]
    private bool _recolorLogo;

    /// <summary>Set when the settings file could not be written, so the choice would be lost on the next start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    private string _saveError = "";

    public bool HasSaveError => SaveError.Length > 0;

    public AppSettings Current => new()
    {
        AutoCheckUpdates = AutoCheckUpdates,
        Theme = Theme,
        AccentColor = Accent,
        RecolorLogo = RecolorLogo,
    };

    partial void OnAutoCheckUpdatesChanged(bool value) => Persist();

    partial void OnThemeChanged(ThemeMode value) => Persist();

    partial void OnRecolorLogoChanged(bool value) => Persist();

    partial void OnAccentChanged(string value)
    {
        foreach (var swatch in Swatches)
        {
            swatch.IsSelected = swatch.Hex.Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        if (!AccentInput.Equals(value, StringComparison.OrdinalIgnoreCase))
        {
            AccentInput = value;
        }

        Persist();
    }

    partial void OnAccentInputChanged(string value)
    {
        var text = value.Trim();
        if (text.Length > 0 && !text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (AppSettings.IsValidColor(text))
        {
            AccentError = "";
            Accent = text.ToUpperInvariant();
        }
        else
        {
            // Not while somebody is still typing: the message only appears once a whole colour's worth of characters is there.
            AccentError = text.Length >= 7 ? "Renk #RRGGBB biçiminde olmalıdır. Örnek: #2F7DF6" : "";
        }
    }

    [RelayCommand]
    private void PickAccent(string? hex)
    {
        if (AppSettings.IsValidColor(hex))
        {
            Accent = hex!.ToUpperInvariant();
        }
    }

    [RelayCommand]
    private void ResetAccent() => Accent = AppSettings.DefaultAccent;

    private void Persist()
    {
        if (_quiet)
        {
            return;
        }

        var settings = Current;
        _apply(settings);
        SaveError = _store.Save(settings)
            ? ""
            : "Ayarlar kaydedilemedi. Seçiminiz bu oturumda geçerli olur ama sonraki açılışta kaybolabilir.";
    }
}
