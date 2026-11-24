using FoxFix.Core;
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
        (Strings.Get(Strings.SwatchPurple), AppSettings.DefaultAccent),
        (Strings.Get(Strings.SwatchBlue), "#2F7DF6"),
        (Strings.Get(Strings.SwatchCyan), "#14A3C7"),
        (Strings.Get(Strings.SwatchGreen), "#1FA971"),
        (Strings.Get(Strings.SwatchOrange), "#F28C28"),
        (Strings.Get(Strings.SwatchRed), "#E5484D"),
        (Strings.Get(Strings.SwatchPink), "#E5509F"),
    ];

    private readonly AppSettingsStore _store;
    private readonly Action<AppSettings> _apply;

    // While the values are being filled in (or one field follows another) nothing is saved or applied again.
    private bool _quiet = true;

    private readonly Action? _restart;

    public SettingsViewModel(AppSettingsStore store, AppSettings initial, Action<AppSettings> apply, UpdateViewModel? updates = null, Action? restart = null)
    {
        _store = store;
        _apply = apply;
        _restart = restart;
        Updates = updates;

        var settings = initial.Sanitized();
        _autoCheckUpdates = settings.AutoCheckUpdates;
        _theme = settings.Theme;
        _accent = settings.AccentColor;
        _recolorLogo = settings.RecolorLogo;
        _language = settings.Language.Length > 0 ? settings.Language : Strings.Language;
        Tour = settings.Tour;
        DpiHintSeen = settings.DpiHintSeen;
        _experimentalDevices = settings.ExperimentalDevices;
        Overlay = settings.Overlay;

        foreach (var (name, hex) in Presets)
        {
            Swatches.Add(new AccentSwatch(name, hex) { IsSelected = hex.Equals(_accent, StringComparison.OrdinalIgnoreCase) });
        }

        Picker = new AccentPickerViewModel(() => Accent, hex => Accent = hex);
        Picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccentPickerViewModel.IsOpen))
            {
                OnPropertyChanged(nameof(CustomSwatchSelected));
            }
        };

        _quiet = false;
    }

    /// <summary>The update checks; null in tests that do not need them.</summary>
    public UpdateViewModel? Updates { get; }

    public ObservableCollection<AccentSwatch> Swatches { get; } = [];

    /// <summary>The colour wheel under the ready-made colours: previews first, applies on "Uygula".</summary>
    public AccentPickerViewModel Picker { get; }

    /// <summary>True when the colour in use is none of the ready-made ones: the wheel's circle shows it.</summary>
    public bool IsCustomAccent => !Swatches.Any(s => s.IsSelected);

    /// <summary>The wheel's circle is ringed while a custom colour is in use or its panel is open.</summary>
    public bool CustomSwatchSelected => IsCustomAccent || Picker.IsOpen;

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
        Language = Language,
        Tour = Tour,
        DpiHintSeen = DpiHintSeen,
        ExperimentalDevices = ExperimentalDevices,
        Overlay = Overlay,
    };

    /// <summary>"tr" or "en". Saved at once; it is used when FoxFix opens the next time (a restart is offered).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTurkish), nameof(IsEnglish))]
    private string _language = Strings.Turkish;

    public bool IsTurkish
    {
        get => Language == Strings.Turkish;
        set => ChooseLanguage(value, Strings.Turkish);
    }

    public bool IsEnglish
    {
        get => Language == Strings.English;
        set => ChooseLanguage(value, Strings.English);
    }

    private void ChooseLanguage(bool selected, string code)
    {
        if (selected)
        {
            Language = code;
        }
    }

    /// <summary>Set after the language was changed: "FoxFix has to reopen" with Reopen now / Later.</summary>
    [ObservableProperty]
    private bool _isRestartPromptOpen;

    partial void OnLanguageChanged(string value)
    {
        Persist();
        if (!_quiet)
        {
            IsRestartPromptOpen = true;
        }
    }

    [RelayCommand]
    private void RestartNow()
    {
        IsRestartPromptOpen = false;
        _restart?.Invoke();
    }

    [RelayCommand]
    private void RestartLater() => IsRestartPromptOpen = false;

    /// <summary>Where the first-use tour stands (not shown on the page; kept with the other settings).</summary>
    public TourState Tour { get; private set; }

    public bool DpiHintSeen { get; private set; }

    /// <summary>The game overlay's choices; the overlay page keeps them and saves them here.</summary>
    public FoxFix.Core.Overlay.OverlaySettings Overlay { get; private set; }

    public void SetOverlay(FoxFix.Core.Overlay.OverlaySettings overlay)
    {
        Overlay = overlay;
        Persist();
    }

    public void SetTour(TourState state)
    {
        Tour = state;
        Persist();
    }

    public void MarkDpiHintSeen()
    {
        DpiHintSeen = true;
        Persist();
    }

    /// <summary>Experimental settings: the "Ses ve kamera" page and its menu item exist only while this is on.</summary>
    [ObservableProperty]
    private bool _experimentalDevices;

    partial void OnExperimentalDevicesChanged(bool value) => Persist();

    partial void OnAutoCheckUpdatesChanged(bool value) => Persist();

    partial void OnThemeChanged(ThemeMode value) => Persist();

    partial void OnRecolorLogoChanged(bool value) => Persist();

    partial void OnAccentChanged(string value)
    {
        foreach (var swatch in Swatches)
        {
            swatch.IsSelected = swatch.Hex.Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        OnPropertyChanged(nameof(IsCustomAccent));
        OnPropertyChanged(nameof(CustomSwatchSelected));
        Picker?.Close();
        Persist();
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
            : Strings.Get(Strings.SettingsSaveFailed);
    }
}
