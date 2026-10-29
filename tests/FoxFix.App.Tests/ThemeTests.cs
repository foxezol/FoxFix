using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FoxFix.App;
using Xunit;

namespace FoxFix.App.Tests;

public class ThemePaletteTests
{
    private static readonly string[] RequiredKeys =
    [
        "Background", "Card", "Divider", "Foreground", "Muted", "Accent", "AccentText",
        "ButtonSecondary", "StatusOk", "StatusWarn", "StatusBad", "StatusNeutral",
    ];

    public static TheoryData<string> Names => ["koyu", "açık"];

    private static ThemePalette Palette(string name) => name == "koyu" ? ThemePalettes.Dark : ThemePalettes.Light;

    [Fact]
    public void Both_palettes_define_exactly_the_same_keys()
    {
        ThemePalettes.Dark.Colors.Keys.Should().BeEquivalentTo(RequiredKeys);
        ThemePalettes.Light.Colors.Keys.Should().BeEquivalentTo(RequiredKeys);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Body_text_is_readable_on_every_surface(string name)
    {
        var c = Palette(name).Colors;

        Contrast(c["Foreground"], c["Background"]).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(c["Foreground"], c["Card"]).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(c["Muted"], c["Background"]).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(c["Muted"], c["Card"]).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Accent_text_is_readable_on_every_surface(string name)
    {
        var c = Palette(name).Colors;

        Contrast(c["AccentText"], c["Background"]).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(c["AccentText"], c["Card"]).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Button_labels_are_readable(string name)
    {
        var c = Palette(name).Colors;

        Contrast(Colors.White, c["Accent"]).Should().BeGreaterThanOrEqualTo(4.5, "primary buttons use white text");
        Contrast(c["Foreground"], c["ButtonSecondary"]).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Status_symbols_stand_out_from_the_background(string name)
    {
        var c = Palette(name).Colors;

        foreach (var key in new[] { "StatusOk", "StatusWarn", "StatusBad", "StatusNeutral" })
        {
            Contrast(c[key], c["Background"]).Should().BeGreaterThanOrEqualTo(4.5, $"{key} on {name} background");
            Contrast(c[key], c["Card"]).Should().BeGreaterThanOrEqualTo(4.5, $"{key} on {name} card");
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_three_statuses_are_told_apart(string name)
    {
        var c = Palette(name).Colors;

        new[] { c["StatusOk"], c["StatusWarn"], c["StatusBad"] }.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void Light_is_actually_light_and_dark_is_actually_dark()
    {
        Luminance(ThemePalettes.Light.Colors["Background"]).Should().BeGreaterThan(0.8);
        Luminance(ThemePalettes.Dark.Colors["Background"]).Should().BeLessThan(0.05);
    }

    /// <summary>WCAG 2.x contrast ratio.</summary>
    private static double Contrast(Color a, Color b)
    {
        var (light, dark) = Luminance(a) >= Luminance(b) ? (Luminance(a), Luminance(b)) : (Luminance(b), Luminance(a));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}

public class ThemeManagerTests
{
    private static Color BackgroundOf(ResourceDictionary resources) => ((SolidColorBrush)resources["Background"]).Color;

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public void The_windows_app_mode_value_decides(int value, bool expectedLight)
    {
        ThemeManager.IsLightFromRegistryValue(value).Should().Be(expectedLight);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Windows_counts_as_light_when_the_setting_is_missing_or_odd(object? value)
    {
        ThemeManager.IsLightFromRegistryValue(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("--tema=acik", true)]
    [InlineData("--tema=açık", true)]
    [InlineData("--theme=light", true)]
    [InlineData("--TEMA=KOYU", false)]
    [InlineData("--theme=dark", false)]
    public void A_theme_can_be_forced_from_the_command_line(string arg, bool expectedLight)
    {
        ThemeManager.ParseForcedTheme([arg]).Should().Be(expectedLight);
    }

    [Fact]
    public void Without_a_flag_the_system_decides()
    {
        ThemeManager.ParseForcedTheme([]).Should().BeNull();
        ThemeManager.ParseForcedTheme(["--baska"]).Should().BeNull();
    }

    [Fact]
    public void A_forced_light_theme_replaces_every_brush_with_the_light_color()
    {
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources, forcedLight: true);

        theme.Refresh();

        theme.IsLight.Should().BeTrue();
        BackgroundOf(resources).Should().Be(ThemePalettes.Light.Colors["Background"]);
        foreach (var key in ThemePalettes.Light.Colors.Keys)
        {
            ((SolidColorBrush)resources[key]).Color.Should().Be(ThemePalettes.Light.Colors[key], key);
        }
    }

    [Fact]
    public void A_forced_dark_theme_uses_the_dark_palette()
    {
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources, forcedLight: false);

        theme.Refresh();

        theme.IsLight.Should().BeFalse();
        BackgroundOf(resources).Should().Be(ThemePalettes.Dark.Colors["Background"]);
    }

    [Fact]
    public void Brushes_are_replaced_not_edited_and_are_frozen()
    {
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources, forcedLight: true);

        theme.Refresh();

        ((SolidColorBrush)resources["Background"]).IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void The_changed_event_fires_only_when_the_theme_really_changes()
    {
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources, forcedLight: true);
        var calls = new List<bool>();
        theme.Changed += calls.Add;

        theme.Refresh();
        theme.Refresh();

        calls.Should().Equal(true);
    }
}
