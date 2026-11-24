using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FoxFix.Core.Settings;

/// <summary>Follow Windows, or always light, or always dark.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Where the first-use tour stands. The tour is offered once; "Skipped" also covers saying no at the question.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TourState
{
    NotOffered,
    Skipped,
    Completed,
}

/// <summary>What the person chose on the "Ayarlar" page. Every value has a safe default, so a missing or damaged file is harmless.</summary>
public sealed record AppSettings
{
    public const string DefaultAccent = "#9E20E6";

    /// <summary>Look for a newer version in the background each time FoxFix opens.</summary>
    public bool AutoCheckUpdates { get; init; } = true;

    public ThemeMode Theme { get; init; } = ThemeMode.System;

    /// <summary>"#RRGGBB".</summary>
    public string AccentColor { get; init; } = DefaultAccent;

    /// <summary>Paint the fox logo (sidebar, window icon) in the accent colour too.</summary>
    public bool RecolorLogo { get; init; } = true;

    /// <summary>"tr" or "en"; empty until the person has chosen one (the first start asks).</summary>
    public string Language { get; init; } = "";

    public TourState Tour { get; init; } = TourState.NotOffered;

    /// <summary>The short explanation of FoxDPI that shows the first time the DNS ve DPI page is opened after the tour was skipped.</summary>
    public bool DpiHintSeen { get; init; }

    /// <summary>Experimental: the "Ses ve kamera" page. Off until the person turns it on in Ayarlar.</summary>
    public bool ExperimentalDevices { get; init; }

    /// <summary>The game overlay (frames per second, graphics card and processor load): what to show, where, and its shortcut.</summary>
    public FoxFix.Core.Overlay.OverlaySettings Overlay { get; init; } = new();

    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public static bool IsValidColor(string? text) => text is not null && HexColor.IsMatch(text);

    /// <summary>The same settings with anything out of range put back to its default (a hand-edited file, for example).</summary>
    public AppSettings Sanitized() => this with
    {
        AccentColor = IsValidColor(AccentColor) ? AccentColor.ToUpperInvariant() : DefaultAccent,
        Theme = Enum.IsDefined(Theme) ? Theme : ThemeMode.System,
        Language = Strings.IsKnownLanguage(Language) ? Language.ToLowerInvariant() : "",
        Tour = Enum.IsDefined(Tour) ? Tour : TourState.NotOffered,
        Overlay = (Overlay ?? new FoxFix.Core.Overlay.OverlaySettings()).Sanitized(),
    };
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public AppSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.LocalRoot, "ayarlar.json");
    }

    /// <summary>The saved settings, or the defaults when there is no file or it cannot be read. Settings are a convenience, never a reason to stop.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings()).Sanitized();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <returns>False when the file could not be written, so the window can say so.</returns>
    public bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings.Sanitized(), Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
