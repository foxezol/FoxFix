using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.Settings;
using Xunit;
using ThemeMode = FoxFix.Core.Settings.ThemeMode;

namespace FoxFix.App.Tests;

public class ThemePaletteTests
{
    private static readonly string[] RequiredKeys =
    [
        "Background", "Card", "Divider", "Foreground", "Muted", "Accent", "AccentText", "AccentForeground",
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

        Contrast(c["AccentForeground"], c["Accent"]).Should().BeGreaterThanOrEqualTo(4.5, "primary buttons label");
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

public class AccentColorTests
{
    public static TheoryData<string> Samples =>
    [
        "#9E20E6", "#2F7DF6", "#14A3C7", "#1FA971", "#F28C28", "#E5484D", "#E5509F",
        "#000000", "#FFFFFF", "#FFFF00", "#000080", "#808080", "#0F1115", "#FAFAFB",
    ];

    private static Color Parse(string hex)
    {
        ColorMath.TryParse(hex, out var color).Should().BeTrue();
        return color;
    }

    [Fact]
    public void The_brand_purple_keeps_exactly_the_hand_picked_palettes()
    {
        ThemePalettes.Build(false, ThemePalettes.DefaultAccent).Colors["Accent"].Should().Be(Parse("#9E20E6"));
        ThemePalettes.Dark.Colors["AccentText"].Should().Be(Parse("#C58AF9"));
        ThemePalettes.Light.Colors["AccentText"].Should().Be(Parse("#7A12B8"));
        ThemePalettes.Dark.Colors["AccentForeground"].Should().Be(Colors.White);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Any_colour_gives_readable_text_buttons_and_shapes_on_both_themes(string hex)
    {
        foreach (var light in new[] { false, true })
        {
            var c = ThemePalettes.Build(light, Parse(hex)).Colors;
            var where = $"{hex} on {(light ? "light" : "dark")}";

            ColorMath.Contrast(c["AccentText"], c["Background"]).Should().BeGreaterThanOrEqualTo(4.5, where);
            ColorMath.Contrast(c["AccentText"], c["Card"]).Should().BeGreaterThanOrEqualTo(4.5, where);
            ColorMath.Contrast(c["Accent"], c["Background"]).Should().BeGreaterThanOrEqualTo(3.0, where);
            ColorMath.Contrast(c["Accent"], c["Card"]).Should().BeGreaterThanOrEqualTo(3.0, where);
            ColorMath.Contrast(c["AccentForeground"], c["Accent"]).Should().BeGreaterThanOrEqualTo(4.5, where);
        }
    }

    [Fact]
    public void A_bright_colour_gets_a_dark_button_label_and_a_dark_colour_a_white_one()
    {
        ThemePalettes.Build(false, Parse("#FFFF00")).Colors["AccentForeground"].Should().Be(Colors.Black);
        ThemePalettes.Build(true, Parse("#000080")).Colors["AccentForeground"].Should().Be(Colors.White);
    }

    [Fact]
    public void A_colour_that_is_already_readable_is_not_changed()
    {
        ThemePalettes.Build(false, Parse("#2F7DF6")).Colors["Accent"].Should().Be(Parse("#2F7DF6"));
    }

    [Theory]
    [InlineData("#2f7df6", true, 0x2F, 0x7D, 0xF6)]
    [InlineData("#000000", true, 0, 0, 0)]
    [InlineData("2F7DF6", false, 0, 0, 0)]
    [InlineData("#2F7DF", false, 0, 0, 0)]
    [InlineData(null, false, 0, 0, 0)]
    public void Hex_codes_are_read_strictly(string? text, bool valid, int r, int g, int b)
    {
        ColorMath.TryParse(text, out var color).Should().Be(valid);
        if (valid)
        {
            color.Should().Be(Color.FromRgb((byte)r, (byte)g, (byte)b));
        }
    }
}

public class LogoTintTests
{
    [Fact]
    public void Only_the_colour_changes_the_shape_stays()
    {
        var source = BitmapSource.Create(
            2, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 10, 20, 30, 255, 40, 50, 60, 0 }, 8);

        var tinted = LogoTint.Tint(source, Color.FromRgb(0x2F, 0x7D, 0xF6));

        var pixels = new byte[8];
        tinted.CopyPixels(pixels, 8, 0);
        pixels[..3].Should().Equal(0xF6, 0x7D, 0x2F);
        pixels[3].Should().Be(255, "an opaque pixel stays opaque");
        pixels[7].Should().Be(0, "a see-through pixel stays see-through");
        tinted.IsFrozen.Should().BeTrue();
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

    [Fact]
    public void A_chosen_dark_theme_beats_what_windows_says()
    {
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources);

        theme.Configure(new AppSettings { Theme = ThemeMode.Light });
        theme.IsLight.Should().BeTrue();
        BackgroundOf(resources).Should().Be(ThemePalettes.Light.Colors["Background"]);

        theme.Configure(new AppSettings { Theme = ThemeMode.Dark });
        theme.IsLight.Should().BeFalse();
        BackgroundOf(resources).Should().Be(ThemePalettes.Dark.Colors["Background"]);
    }

    [Fact]
    public void The_command_line_flag_still_wins_over_the_chosen_theme()
    {
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources, forcedLight: true);

        theme.Configure(new AppSettings { Theme = ThemeMode.Dark });

        theme.IsLight.Should().BeTrue();
    }

    [Fact]
    public void A_chosen_accent_reaches_every_accent_brush_and_fires_changed()
    {
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources, forcedLight: false);
        theme.Configure(new AppSettings());
        var calls = 0;
        theme.Changed += _ => calls++;

        theme.Configure(new AppSettings { AccentColor = "#2F7DF6" });

        ((SolidColorBrush)resources["Accent"]).Color.Should().Be(Color.FromRgb(0x2F, 0x7D, 0xF6));
        resources.Contains("AccentForeground").Should().BeTrue();
        theme.Accent.Should().Be(Color.FromRgb(0x2F, 0x7D, 0xF6));
        calls.Should().Be(1);
    }

    [Fact]
    public void The_logo_is_painted_only_when_asked_and_only_for_a_colour_other_than_the_brand_purple()
    {
        var original = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 158, 32, 230, 255 }, 4);
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources, forcedLight: false, logoLoader: () => original);

        theme.Configure(new AppSettings());
        resources[ThemeManager.LogoKey].Should().BeSameAs(original);

        theme.Configure(new AppSettings { AccentColor = "#2F7DF6" });
        resources[ThemeManager.LogoKey].Should().NotBeSameAs(original);

        theme.Configure(new AppSettings { AccentColor = "#2F7DF6", RecolorLogo = false });
        resources[ThemeManager.LogoKey].Should().BeSameAs(original);
    }

    [Fact]
    public void The_window_icon_is_a_box_in_the_accent_colour_and_the_shipped_icon_comes_back_with_the_default()
    {
        var glyph = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 158, 32, 230, 255 }, 4);
        var shipped = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 1, 2, 3, 255 }, 4);
        var resources = new ResourceDictionary { [ThemeManager.IconKey] = shipped, [ThemeManager.DpiIconKey] = shipped };
        using var theme = new ThemeManager(resources, forcedLight: false, logoLoader: () => glyph, dpiLogoLoader: () => glyph);

        theme.Configure(new AppSettings { AccentColor = "#2F7DF6" });

        foreach (var key in new[] { ThemeManager.IconKey, ThemeManager.DpiIconKey })
        {
            var icon = resources[key].Should().BeOfType<RenderTargetBitmap>().Subject;
            icon.PixelWidth.Should().Be(256);
            icon.PixelHeight.Should().Be(256);
        }

        theme.Configure(new AppSettings());
        resources[ThemeManager.IconKey].Should().BeSameAs(shipped);
        resources[ThemeManager.DpiIconKey].Should().BeSameAs(shipped);
    }

    [Fact]
    public void FoxDPIs_logo_follows_the_colour_like_FoxFixs()
    {
        var glyph = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 158, 32, 230, 255 }, 4);
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources, forcedLight: false, dpiLogoLoader: () => glyph);

        theme.Configure(new AppSettings { AccentColor = "#E5484D" });

        resources[ThemeManager.DpiLogoKey].Should().NotBeSameAs(glyph);
        theme.Configure(new AppSettings());
        resources[ThemeManager.DpiLogoKey].Should().BeSameAs(glyph);
    }

    [Theory]
    [InlineData("#9E20E6")]
    [InlineData("#000080")]
    [InlineData("#FFFF00")]
    public void The_fox_stays_visible_on_the_box_whatever_the_colour(string hex)
    {
        ColorMath.TryParse(hex, out var box);
        var ink = ColorMath.Contrast(Colors.Black, box) >= 3 ? Colors.Black : Colors.White;

        ColorMath.Contrast(ink, box).Should().BeGreaterThanOrEqualTo(3);
    }
    [Fact]
    public void A_logo_that_cannot_be_read_leaves_the_resource_alone()
    {
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(resources, forcedLight: false, logoLoader: () => null);

        theme.Configure(new AppSettings { AccentColor = "#2F7DF6" });

        resources.Contains(ThemeManager.LogoKey).Should().BeFalse();
    }
}