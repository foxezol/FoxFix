using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FoxFix.App;

public sealed record ThemePalette(string Name, IReadOnlyDictionary<string, Color> Colors);

/// <summary>
/// Dark is the brand look (plan section 6). Light keeps the same brand purple for shapes, but text that
/// is purple on dark uses a lighter purple there and a deeper one on light ("AccentText"), and status colors are darkened so they stay
/// readable on white. Contrast ratios are verified in the tests.
/// </summary>
public static class ThemePalettes
{
    public static ThemePalette Dark { get; } = new("koyu", new Dictionary<string, Color>
    {
        ["Background"] = Rgb(0x0F1115),
        ["Card"] = Rgb(0x16191F),
        ["Divider"] = Rgb(0x23272F),
        ["Foreground"] = Rgb(0xECEEF2),
        ["Muted"] = Rgb(0x9097A5),
        ["Accent"] = Rgb(0x9E20E6),
        ["AccentText"] = Rgb(0xC58AF9),
        ["ButtonSecondary"] = Rgb(0x1E222A),
        ["StatusOk"] = Rgb(0x46D18B),
        ["StatusWarn"] = Rgb(0xFFC24D),
        ["StatusBad"] = Rgb(0xFF6B6B),
        ["StatusNeutral"] = Rgb(0x9097A5),
    });

    public static ThemePalette Light { get; } = new("açık", new Dictionary<string, Color>
    {
        ["Background"] = Rgb(0xFAFAFB),
        ["Card"] = Rgb(0xFFFFFF),
        ["Divider"] = Rgb(0xE6E8EC),
        ["Foreground"] = Rgb(0x1B1E23),
        ["Muted"] = Rgb(0x56606D),
        ["Accent"] = Rgb(0x9E20E6),
        ["AccentText"] = Rgb(0x7A12B8),
        ["ButtonSecondary"] = Rgb(0xEEF0F3),
        ["StatusOk"] = Rgb(0x17794A),
        ["StatusWarn"] = Rgb(0x9A5B00),
        ["StatusBad"] = Rgb(0xC0262D),
        ["StatusNeutral"] = Rgb(0x56606D),
    });

    private static Color Rgb(int value) =>
        Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
}

/// <summary>
/// Applies a palette to the application's brush resources and follows the Windows "app mode" setting live.
/// Brushes are replaced (not mutated), and the XAML uses DynamicResource, so every control updates.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly ResourceDictionary _resources;
    private readonly bool? _forcedLight;

    public ThemeManager(ResourceDictionary resources, bool? forcedLight = null)
    {
        _resources = resources;
        _forcedLight = forcedLight;

        if (_forcedLight is null)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
    }

    public bool IsLight { get; private set; }

    /// <summary>Raised after the palette changed; the argument is true for the light theme.</summary>
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

    public void Refresh()
    {
        var light = _forcedLight ?? ReadSystemIsLight();
        Apply(light ? ThemePalettes.Light : ThemePalettes.Dark);

        var changed = light != IsLight;
        IsLight = light;
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

    private static bool ReadSystemIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return IsLightFromRegistryValue(key?.GetValue("AppsUseLightTheme"));
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
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
