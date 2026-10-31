using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FoxFix.Core.Settings;
using Microsoft.Win32;
using ThemeMode = FoxFix.Core.Settings.ThemeMode;

namespace FoxFix.App;

public sealed record ThemePalette(string Name, IReadOnlyDictionary<string, Color> Colors);

/// <summary>WCAG 2.x contrast, and the small amount of colour arithmetic the accent colour needs.</summary>
public static class ColorMath
{
    public static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    public static double Contrast(Color a, Color b)
    {
        var (light, dark) = Luminance(a) >= Luminance(b) ? (Luminance(a), Luminance(b)) : (Luminance(b), Luminance(a));
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>0 gives <paramref name="from"/>, 1 gives <paramref name="to"/>.</summary>
    public static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    /// <summary>
    /// The colour closest to <paramref name="color"/> that still reaches <paramref name="minimum"/> against every surface:
    /// it is moved towards white (on a dark theme) or towards black (on a light one) just far enough.
    /// </summary>
    public static Color EnsureContrast(Color color, IReadOnlyList<Color> surfaces, double minimum, bool lighten)
    {
        var target = lighten ? Colors.White : Colors.Black;
        for (var step = 0; step <= 50; step++)
        {
            var candidate = Mix(color, target, step / 50.0);
            if (surfaces.All(s => Contrast(candidate, s) >= minimum))
            {
                return candidate;
            }
        }

        return target;
    }

    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (!AppSettings.IsValidColor(text))
        {
            return false;
        }

        color = Color.FromRgb(
            byte.Parse(text![1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(text[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(text[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return true;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

/// <summary>
/// Dark is the brand look (plan section 6). Light keeps the same brand purple for shapes, but text that
/// is purple on dark uses a lighter purple there and a deeper one on light ("AccentText"), and status colors are darkened so they stay
/// readable on white. A colour chosen by the person goes through the same rules: shapes keep at least 3:1 against the
/// surfaces, accent-coloured text 4.5:1, and the label on an accent button is whichever of white and black reads better.
/// Contrast ratios are verified in the tests.
/// </summary>
public static class ThemePalettes
{
    public static Color DefaultAccent { get; } = Rgb(0x9E20E6);

    private static readonly Dictionary<string, Color> DarkBase = new()
    {
        ["Background"] = Rgb(0x0F1115),
        ["Card"] = Rgb(0x16191F),
        ["Divider"] = Rgb(0x23272F),
        ["Foreground"] = Rgb(0xECEEF2),
        ["Muted"] = Rgb(0x9097A5),
        ["ButtonSecondary"] = Rgb(0x1E222A),
        ["StatusOk"] = Rgb(0x46D18B),
        ["StatusWarn"] = Rgb(0xFFC24D),
        ["StatusBad"] = Rgb(0xFF6B6B),
        ["StatusNeutral"] = Rgb(0x9097A5),
    };

    private static readonly Dictionary<string, Color> LightBase = new()
    {
        ["Background"] = Rgb(0xFAFAFB),
        ["Card"] = Rgb(0xFFFFFF),
        ["Divider"] = Rgb(0xE6E8EC),
        ["Foreground"] = Rgb(0x1B1E23),
        ["Muted"] = Rgb(0x56606D),
        ["ButtonSecondary"] = Rgb(0xEEF0F3),
        ["StatusOk"] = Rgb(0x17794A),
        ["StatusWarn"] = Rgb(0x9A5B00),
        ["StatusBad"] = Rgb(0xC0262D),
        ["StatusNeutral"] = Rgb(0x56606D),
    };

    public static ThemePalette Dark { get; } = Build(light: false, DefaultAccent);

    public static ThemePalette Light { get; } = Build(light: true, DefaultAccent);

    public static ThemePalette Build(bool light, Color accent)
    {
        var colors = new Dictionary<string, Color>(light ? LightBase : DarkBase);
        var surfaces = new[] { colors["Background"], colors["Card"] };

        var shape = ColorMath.EnsureContrast(accent, surfaces, 3.0, lighten: !light);

        // The brand purple keeps its hand-picked text colours; any other colour is worked out.
        var text = accent == DefaultAccent
            ? Rgb(light ? 0x7A12B8 : 0xC58AF9)
            : ColorMath.EnsureContrast(accent, surfaces, 4.5, lighten: !light);

        colors["Accent"] = shape;
        colors["AccentText"] = text;
        colors["AccentForeground"] = ColorMath.Contrast(Colors.White, shape) >= ColorMath.Contrast(Colors.Black, shape) ? Colors.White : Colors.Black;

        return new ThemePalette(light ? "açık" : "koyu", colors);
    }

    private static Color Rgb(int value) =>
        Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
}

/// <summary>Paints the one-colour fox logo in another colour, keeping its shape (the transparency) as it is.</summary>
public static class LogoTint
{
    public static BitmapSource Tint(BitmapSource source, Color color)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            // Straight (not premultiplied) alpha: only the colour changes, the edge softness stays.
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
        }

        var result = BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}

/// <summary>
/// Applies a palette to the application's brush resources and follows the Windows "app mode" setting live (unless the
/// person chose light or dark themselves). Brushes are replaced (not mutated), and the XAML uses DynamicResource,
/// so every control updates.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    public const string LogoKey = "FoxLogoImage";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly ResourceDictionary _resources;
    private readonly bool? _forcedLight;
    private readonly Func<BitmapSource?>? _logoLoader;

    private BitmapSource? _originalLogo;
    private Color? _appliedAccent;
    private bool _appliedLogoTint;

    public ThemeManager(ResourceDictionary resources, bool? forcedLight = null, Func<BitmapSource?>? logoLoader = null)
    {
        _resources = resources;
        _forcedLight = forcedLight;
        _logoLoader = logoLoader;

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsLight { get; private set; }

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    /// <summary>The colour the person asked for (the colour actually used may be nudged for readability).</summary>
    public Color Accent { get; private set; } = ThemePalettes.DefaultAccent;

    public bool RecolorLogo { get; private set; } = true;

    /// <summary>Raised after the palette or the accent colour changed; the argument is true for the light theme.</summary>
    public event Action<bool>? Changed;

    /// <summary>"AppsUseLightTheme": 1 = light, 0 = dark. Windows is light when the value is missing.</summary>
    public static bool IsLightFromRegistryValue(object? value) => value is not int mode || mode != 0;

    public static bool? ParseForcedTheme(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--tema=acik" or "--tema=açık" or "--theme=light":
                    return true;
                case "--tema=koyu" or "--theme=dark":
                    return false;
            }
        }

        return null;
    }

    /// <summary>Takes the person's choices and applies them at once.</summary>
    public void Configure(AppSettings settings)
    {
        Mode = settings.Theme;
        Accent = ColorMath.TryParse(settings.AccentColor, out var color) ? color : ThemePalettes.DefaultAccent;
        RecolorLogo = settings.RecolorLogo;
        Refresh();
    }

    public void Refresh()
    {
        var light = _forcedLight ?? Mode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => ReadSystemIsLight(),
        };

        var palette = ThemePalettes.Build(light, Accent);
        Apply(palette);

        var tint = RecolorLogo && palette.Colors["Accent"] != ThemePalettes.DefaultAccent;
        ApplyLogo(tint ? palette.Colors["Accent"] : null);

        var changed = light != IsLight || _appliedAccent != Accent || _appliedLogoTint != tint;
        IsLight = light;
        _appliedAccent = Accent;
        _appliedLogoTint = tint;
        if (changed)
        {
            Changed?.Invoke(light);
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void Apply(ThemePalette palette)
    {
        foreach (var (key, color) in palette.Colors)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            _resources[key] = brush;
        }
    }

    /// <summary>The logo in the sidebar, the pop-ups and the window's own icon: the original, or painted in the accent colour.</summary>
    private void ApplyLogo(Color? tint)
    {
        if (_logoLoader is null)
        {
            return;
        }

        _originalLogo ??= _logoLoader();
        if (_originalLogo is null)
        {
            return;
        }

        _resources[LogoKey] = tint is { } color ? LogoTint.Tint(_originalLogo, color) : _originalLogo;
    }

    private static bool ReadSystemIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return IsLightFromRegistryValue(key?.GetValue("AppsUseLightTheme"));
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // The person chose a theme: Windows' switch no longer matters.
        if (_forcedLight is not null || Mode != ThemeMode.System)
        {
            return;
        }

        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        // SystemEvents may call from a background thread; brushes and windows belong to the UI thread.
        Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }
}

/// <summary>Makes the window's title bar follow the theme (Windows 10 20H1+ and Windows 11).</summary>
public static class TitleBar
{
    private const int UseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void SetDark(IntPtr hwnd, bool dark)
    {
        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref value, sizeof(int));
    }
}
