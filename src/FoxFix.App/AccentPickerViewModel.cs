using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core;

namespace FoxFix.App;

/// <summary>
/// The colour wheel for the accent colour. Moving the wheel, the brightness slider or typing a hex code only changes the preview here;
/// the theme changes when "Uygula" is pressed, and "Vazgeç" changes nothing. The colour that is applied still goes through the theme's
/// own readability rules (see <see cref="ThemePalettes.Build"/>), so a very light or very dark choice is pulled into a readable range.
/// </summary>
public sealed partial class AccentPickerViewModel : ObservableObject
{
    public const double MinBrightness = 10;
    public const double MaxBrightness = 100;

    private readonly Func<string> _current;
    private readonly Action<string> _apply;

    // While one of the fields is being filled in from another, the others must not answer back (typing would jump).
    private bool _syncing;

    public AccentPickerViewModel(Func<string> current, Action<string> apply)
    {
        _current = current;
        _apply = apply;
        SetFrom(Color.FromRgb(0x9E, 0x20, 0xE6), writeHex: true);
    }

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>0 to 360: the angle on the wheel.</summary>
    [ObservableProperty]
    private double _hue;

    /// <summary>0 to 1: the distance from the middle of the wheel.</summary>
    [ObservableProperty]
    private double _saturation;

    /// <summary>10 to 100, the slider.</summary>
    [ObservableProperty]
    private double _brightness = MaxBrightness;

    [ObservableProperty]
    private string _hex = "";

    [ObservableProperty]
    private Brush _preview = Brushes.Transparent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = "";

    public bool HasError => Error.Length > 0;

    partial void OnHueChanged(double value) => FollowWheel();

    partial void OnSaturationChanged(double value) => FollowWheel();

    partial void OnBrightnessChanged(double value) => FollowWheel();

    private void FollowWheel()
    {
        if (_syncing)
        {
            return;
        }

        var color = ColorMath.FromHsv(Hue, Saturation, Brightness / 100);
        _syncing = true;
        Hex = ColorMath.ToHex(color);
        Preview = Freeze(color);
        Error = "";
        _syncing = false;
    }

    partial void OnHexChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        // A colour still being typed changes nothing; a whole one moves the wheel and the slider.
        if (ColorMath.TryParse(Normalize(value), out var color))
        {
            SetFrom(color, writeHex: false);
            Error = "";
        }
    }

    private void SetFrom(Color color, bool writeHex)
    {
        _syncing = true;
        var (hue, saturation, value) = ColorMath.ToHsv(color);

        // A grey has no hue of its own: the wheel keeps the one it had.
        if (saturation > 0.001)
        {
            Hue = hue;
        }

        Saturation = saturation;
        Brightness = Math.Clamp(Math.Round(value * 100), MinBrightness, MaxBrightness);
        Preview = Freeze(color);
        if (writeHex)
        {
            Hex = ColorMath.ToHex(color);
        }

        _syncing = false;
    }

    private static string Normalize(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > 0 && !trimmed.StartsWith('#') ? "#" + trimmed : trimmed;
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    [RelayCommand]
    private void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>Starts from the accent colour in use.</summary>
    public void Open()
    {
        if (ColorMath.TryParse(_current(), out var color))
        {
            SetFrom(color, writeHex: true);
        }

        Error = "";
        IsOpen = true;
    }

    /// <summary>Closes the panel without changing anything.</summary>
    [RelayCommand]
    public void Close() => IsOpen = false;

    [RelayCommand]
    private void Apply()
    {
        if (!ColorMath.TryParse(Normalize(Hex), out var color))
        {
            Error = Strings.Get(Strings.AccentInvalid);
            return;
        }

        IsOpen = false;
        _apply(ColorMath.ToHex(color));
    }
}
