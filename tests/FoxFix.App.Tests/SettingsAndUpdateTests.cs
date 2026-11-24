using FluentAssertions;
using FoxFix.Core;
using FoxFix.App;
using FoxFix.Core.Settings;
using FoxFix.Core.Updates;
using Xunit;
using ThemeMode = FoxFix.Core.Settings.ThemeMode;

namespace FoxFix.App.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-settings-vm-" + Guid.NewGuid().ToString("N"));

    private readonly List<AppSettings> _applied = [];

    private string FilePath => Path.Combine(_dir, "ayarlar.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private SettingsViewModel Create(AppSettings? initial = null) =>
        new(new AppSettingsStore(FilePath), initial ?? new AppSettings(), _applied.Add);

    [Fact]
    public void It_starts_from_the_saved_values_without_applying_or_saving_anything()
    {
        var vm = Create(new AppSettings { AutoCheckUpdates = false, Theme = ThemeMode.Dark, AccentColor = "#2F7DF6", RecolorLogo = false });

        vm.AutoCheckUpdates.Should().BeFalse();
        vm.IsDarkTheme.Should().BeTrue();
        vm.Accent.Should().Be("#2F7DF6");
        vm.RecolorLogo.Should().BeFalse();
        _applied.Should().BeEmpty();
        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public void The_matching_colour_in_the_list_is_marked()
    {
        var vm = Create(new AppSettings { AccentColor = "#1FA971" });

        vm.Swatches.Where(s => s.IsSelected).Should().ContainSingle().Which.Name.Should().Be("Yeşil");
    }

    [Fact]
    public void A_colour_outside_the_list_marks_none()
    {
        Create(new AppSettings { AccentColor = "#123456" }).Swatches.Should().OnlyContain(s => !s.IsSelected);
    }

    [Fact]
    public void Switching_off_the_automatic_check_is_applied_and_saved_at_once()
    {
        var vm = Create();

        vm.AutoCheckUpdates = false;

        _applied.Should().ContainSingle().Which.AutoCheckUpdates.Should().BeFalse();
        new AppSettingsStore(FilePath).Load().AutoCheckUpdates.Should().BeFalse();
    }

    [Fact]
    public void The_three_theme_choices_are_one_choice()
    {
        var vm = Create();
        vm.IsSystemTheme.Should().BeTrue();

        vm.IsDarkTheme = true;

        vm.Theme.Should().Be(ThemeMode.Dark);
        (vm.IsSystemTheme, vm.IsLightTheme, vm.IsDarkTheme).Should().Be((false, false, true));
        _applied.Last().Theme.Should().Be(ThemeMode.Dark);

        vm.IsLightTheme = true;
        (vm.IsSystemTheme, vm.IsLightTheme, vm.IsDarkTheme).Should().Be((false, true, false));
    }

    [Fact]
    public void A_radio_button_being_unticked_changes_nothing()
    {
        var vm = Create(new AppSettings { Theme = ThemeMode.Dark });

        vm.IsDarkTheme = false;

        vm.Theme.Should().Be(ThemeMode.Dark);
        _applied.Should().BeEmpty();
    }

    [Fact]
    public void Picking_a_colour_applies_it_and_selects_it()
    {
        var vm = Create();

        vm.PickAccentCommand.Execute("#e5484d");

        vm.Accent.Should().Be("#E5484D");
        vm.Swatches.Single(s => s.IsSelected).Name.Should().Be("Kırmızı");
        _applied.Last().AccentColor.Should().Be("#E5484D");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mavi")]
    [InlineData("#12")]
    public void Picking_something_that_is_not_a_colour_is_ignored(string? hex)
    {
        var vm = Create();

        vm.PickAccentCommand.Execute(hex);

        vm.Accent.Should().Be(AppSettings.DefaultAccent);
        _applied.Should().BeEmpty();
    }

    [Fact]
    public void A_colour_outside_the_list_is_shown_by_the_wheels_circle()
    {
        var custom = Create(new AppSettings { AccentColor = "#123456" });
        var ready = Create(new AppSettings { AccentColor = "#1FA971" });

        custom.IsCustomAccent.Should().BeTrue();
        custom.CustomSwatchSelected.Should().BeTrue();
        ready.IsCustomAccent.Should().BeFalse();
        ready.CustomSwatchSelected.Should().BeFalse();
    }

    [Fact]
    public void Opening_the_wheel_starts_from_the_colour_in_use_and_changes_nothing()
    {
        var vm = Create(new AppSettings { AccentColor = "#2F7DF6" });

        vm.Picker.ToggleCommand.Execute(null);

        vm.Picker.IsOpen.Should().BeTrue();
        vm.Picker.Hex.Should().Be("#2F7DF6");
        vm.CustomSwatchSelected.Should().BeTrue("the circle is ringed while its panel is open");
        _applied.Should().BeEmpty();
    }

    [Fact]
    public void Moving_the_wheel_only_previews_and_does_not_change_the_theme()
    {
        var vm = Create();
        vm.Picker.ToggleCommand.Execute(null);

        vm.Picker.Hue = 120;
        vm.Picker.Saturation = 1;
        vm.Picker.Brightness = 100;

        vm.Picker.Hex.Should().Be("#00FF00");
        vm.Accent.Should().Be(AppSettings.DefaultAccent);
        _applied.Should().BeEmpty();
    }

    [Fact]
    public void The_slider_darkens_the_preview_without_applying_it()
    {
        var vm = Create();
        vm.Picker.ToggleCommand.Execute(null);
        vm.Picker.Hue = 0;
        vm.Picker.Saturation = 1;

        vm.Picker.Brightness = 50;

        vm.Picker.Hex.Should().Be("#800000");
        _applied.Should().BeEmpty();
    }

    [Fact]
    public void A_typed_hex_moves_the_wheel_without_being_rewritten()
    {
        var vm = Create();
        vm.Picker.ToggleCommand.Execute(null);

        vm.Picker.Hex = "#2f7df6";

        vm.Picker.Hex.Should().Be("#2f7df6", "typing must not jump");
        vm.Picker.Hue.Should().BeApproximately(215, 2);
        vm.Picker.Saturation.Should().BeApproximately(0.81, 0.02);
        vm.Picker.Brightness.Should().Be(96);
        _applied.Should().BeEmpty();
    }

    [Theory]
    [InlineData("#2F7D")]
    [InlineData("#")]
    [InlineData("")]
    public void A_hex_still_being_typed_says_nothing_and_leaves_the_wheel_alone(string typed)
    {
        var vm = Create();
        vm.Picker.ToggleCommand.Execute(null);
        var hue = vm.Picker.Hue;

        vm.Picker.Hex = typed;

        vm.Picker.HasError.Should().BeFalse();
        vm.Picker.Hue.Should().Be(hue);
    }

    [Fact]
    public void Apply_sets_the_accent_saves_it_and_closes_the_panel()
    {
        var vm = Create();
        vm.Picker.ToggleCommand.Execute(null);
        vm.Picker.Hex = "2f7df6";

        vm.Picker.ApplyCommand.Execute(null);

        vm.Accent.Should().Be("#2F7DF6");
        vm.Picker.IsOpen.Should().BeFalse();
        _applied.Last().AccentColor.Should().Be("#2F7DF6");
        new AppSettingsStore(FilePath).Load().AccentColor.Should().Be("#2F7DF6");
    }

    [Theory]
    [InlineData("#GGGGGG")]
    [InlineData("mavi renk")]
    [InlineData("#12")]
    public void Apply_with_a_wrong_hex_explains_and_changes_nothing(string typed)
    {
        var vm = Create();
        vm.Picker.ToggleCommand.Execute(null);
        vm.Picker.Hex = typed;

        vm.Picker.ApplyCommand.Execute(null);

        vm.Picker.Error.Should().Contain("#RRGGBB");
        vm.Picker.IsOpen.Should().BeTrue();
        vm.Accent.Should().Be(AppSettings.DefaultAccent);
        _applied.Should().BeEmpty();
    }

    [Fact]
    public void Cancel_closes_the_panel_and_changes_nothing()
    {
        var vm = Create();
        vm.Picker.ToggleCommand.Execute(null);
        vm.Picker.Hex = "#2F7DF6";

        vm.Picker.CloseCommand.Execute(null);

        vm.Picker.IsOpen.Should().BeFalse();
        vm.Accent.Should().Be(AppSettings.DefaultAccent);
        _applied.Should().BeEmpty();
    }

    [Fact]
    public void Picking_a_ready_made_colour_closes_the_open_panel()
    {
        var vm = Create();
        vm.Picker.ToggleCommand.Execute(null);

        vm.PickAccentCommand.Execute("#E5484D");

        vm.Picker.IsOpen.Should().BeFalse();
        vm.Accent.Should().Be("#E5484D");
    }

    [Fact]
    public void The_wheel_keeps_its_hue_when_a_grey_is_typed()
    {
        var vm = Create();
        vm.Picker.ToggleCommand.Execute(null);
        vm.Picker.Hue = 200;
        vm.Picker.Saturation = 0.5;

        vm.Picker.Hex = "#808080";

        vm.Picker.Hue.Should().Be(200);
        vm.Picker.Saturation.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 1, 1, "#FF0000")]
    [InlineData(120, 1, 1, "#00FF00")]
    [InlineData(240, 1, 1, "#0000FF")]
    [InlineData(60, 1, 0.5, "#808000")]
    [InlineData(0, 0, 1, "#FFFFFF")]
    public void Hue_saturation_and_value_make_the_expected_colour_and_back(double h, double s, double v, string hex)
    {
        var color = ColorMath.FromHsv(h, s, v);

        ColorMath.ToHex(color).Should().Be(hex);
        var (h2, s2, v2) = ColorMath.ToHsv(color);
        ColorMath.ToHex(ColorMath.FromHsv(h2, s2, v2)).Should().Be(hex);
    }

    [Fact]
    public void The_default_colour_button_goes_back_to_the_brand_purple()
    {
        var vm = Create(new AppSettings { AccentColor = "#2F7DF6" });

        vm.ResetAccentCommand.Execute(null);

        vm.Accent.Should().Be("#9E20E6");
        vm.Swatches.Single(s => s.IsSelected).Name.Should().Be("Mor");
    }

    [Fact]
    public void The_logo_colour_switch_is_applied_and_saved()
    {
        var vm = Create();

        vm.RecolorLogo = false;

        _applied.Single().RecolorLogo.Should().BeFalse();
        new AppSettingsStore(FilePath).Load().RecolorLogo.Should().BeFalse();
    }

    [Fact]
    public void Everything_chosen_is_in_the_applied_settings()
    {
        var vm = Create();
        vm.IsLightTheme = true;
        vm.PickAccentCommand.Execute("#14A3C7");
        vm.AutoCheckUpdates = false;

        _applied.Last().Should().Be(new AppSettings { Theme = ThemeMode.Light, AccentColor = "#14A3C7", AutoCheckUpdates = false, RecolorLogo = true, Language = Strings.Language });
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_said_aloud_but_the_choice_still_applies()
    {
        Directory.CreateDirectory(FilePath);
        var vm = Create();

        vm.AutoCheckUpdates = false;

        vm.HasSaveError.Should().BeTrue();
        _applied.Should().ContainSingle();
    }

    [Fact]
    public void A_later_good_save_clears_the_warning()
    {
        var vm = Create();
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(FilePath);
        vm.AutoCheckUpdates = false;
        vm.HasSaveError.Should().BeTrue();

        Directory.Delete(FilePath);
        vm.AutoCheckUpdates = true;

        vm.HasSaveError.Should().BeFalse();
    }
}

public sealed class UpdateViewModelTests
{
    private sealed class Source : IReleaseSource
    {
        public Func<Task<ReleaseInfo?>> Get { get; set; } = () => Task.FromResult<ReleaseInfo?>(null);

        public int Calls { get; private set; }

        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
        {
            Calls++;
            return Get();
        }
    }

    private readonly Source _source = new();
    private readonly List<string> _opened = [];
    private readonly List<string> _logged = [];

    private UpdateViewModel Create(string current = "0.9.14") =>
        new(new UpdateChecker(_source), Version.Parse(current), _opened.Add, _logged.Add);

    private static ReleaseInfo Release(string tag = "v0.9.15", string notes = "Yenilikler", string url = "https://github.com/foxezol/FoxFix/releases/tag/v0.9.15") =>
        new(Version.Parse(tag.TrimStart('v')), tag, url, notes);

    [Fact]
    public async Task The_start_up_check_shows_a_pop_up_only_when_there_is_a_newer_version()
    {
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release());
        var vm = Create();

        await vm.RunStartupCheckAsync();

        vm.IsPromptOpen.Should().BeTrue();
        vm.HasUpdate.Should().BeTrue();
        vm.PromptTitle.Should().Contain("0.9.15");
        vm.PromptNotes.Should().Be("Yenilikler");
    }

    [Fact]
    public async Task The_start_up_check_says_nothing_when_up_to_date()
    {
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release("v0.9.14"));
        var vm = Create();

        await vm.RunStartupCheckAsync();

        vm.IsPromptOpen.Should().BeFalse();
        vm.HasStatus.Should().BeFalse();
        vm.HasUpdate.Should().BeFalse();
        _logged.Should().BeEmpty();
    }

    [Fact]
    public async Task The_start_up_check_says_nothing_when_it_fails_but_leaves_a_line_in_the_log()
    {
        _source.Get = () => Task.FromException<ReleaseInfo?>(new HttpRequestException("çevrimdışı"));
        var vm = Create();

        await vm.RunStartupCheckAsync();

        vm.IsPromptOpen.Should().BeFalse();
        vm.HasStatus.Should().BeFalse();
        _logged.Should().ContainSingle().Which.Should().StartWith("[FX-6003]").And.Contain("çevrimdışı");
    }

    [Fact]
    public async Task The_button_always_answers_up_to_date()
    {
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release("v0.9.14"));
        var vm = Create();

        await vm.CheckNowCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be("FoxFix güncel.");
        vm.IsPromptOpen.Should().BeFalse();
    }

    [Fact]
    public async Task The_button_answers_a_failure_with_its_code_and_logs_it()
    {
        _source.Get = () => Task.FromException<ReleaseInfo?>(new HttpRequestException("çevrimdışı"));
        var vm = Create();

        await vm.CheckNowCommand.ExecuteAsync(null);

        vm.StatusText.Should().StartWith("[FX-6003]");
        _logged.Should().ContainSingle();
    }

    [Fact]
    public async Task The_button_finding_an_update_says_so_and_opens_the_pop_up()
    {
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release());
        var vm = Create();

        await vm.CheckNowCommand.ExecuteAsync(null);

        vm.StatusText.Should().Contain("0.9.15");
        vm.IsPromptOpen.Should().BeTrue();
    }

    [Fact]
    public async Task While_a_check_runs_the_button_is_off_and_a_second_check_does_nothing()
    {
        var gate = new TaskCompletionSource<ReleaseInfo?>();
        _source.Get = () => gate.Task;
        var vm = Create();

        var first = vm.CheckNowCommand.ExecuteAsync(null);
        vm.IsChecking.Should().BeTrue();
        vm.CheckNowCommand.CanExecute(null).Should().BeFalse();
        await vm.RunStartupCheckAsync();
        _source.Calls.Should().Be(1);

        gate.SetResult(null);
        await first;

        vm.IsChecking.Should().BeFalse();
        vm.CheckNowCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_later_up_to_date_answer_takes_the_old_update_away()
    {
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release());
        var vm = Create();
        await vm.RunStartupCheckAsync();
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release("v0.9.14"));

        await vm.CheckNowCommand.ExecuteAsync(null);

        vm.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task Later_closes_the_pop_up_and_opens_nothing()
    {
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release());
        var vm = Create();
        await vm.RunStartupCheckAsync();

        vm.ClosePromptCommand.Execute(null);

        vm.IsPromptOpen.Should().BeFalse();
        _opened.Should().BeEmpty();
    }

    [Fact]
    public async Task Opening_the_release_goes_to_its_page_and_closes_the_pop_up()
    {
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release());
        var vm = Create();
        await vm.RunStartupCheckAsync();

        vm.OpenReleaseCommand.Execute(null);

        _opened.Should().Equal("https://github.com/foxezol/FoxFix/releases/tag/v0.9.15");
        vm.IsPromptOpen.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://github.com/foxezol/FoxFix/releases/tag/v1.0.0", "https://github.com/foxezol/FoxFix/releases/tag/v1.0.0")]
    [InlineData("https://GitHub.com/foxezol/foxfix/releases/latest", "https://github.com/foxezol/foxfix/releases/latest")]
    [InlineData("http://github.com/foxezol/FoxFix/releases/tag/v1", UpdateViewModel.ReleasesUrl)]
    [InlineData("https://github.com.evil.test/foxezol/FoxFix/releases", UpdateViewModel.ReleasesUrl)]
    [InlineData("https://github.com/someone-else/FoxFix/releases", UpdateViewModel.ReleasesUrl)]
    [InlineData("https://github.com/foxezol/FoxFixer/releases", UpdateViewModel.ReleasesUrl)]
    [InlineData("file:///C:/Windows/System32/calc.exe", UpdateViewModel.ReleasesUrl)]
    [InlineData("javascript:alert(1)", UpdateViewModel.ReleasesUrl)]
    [InlineData("", UpdateViewModel.ReleasesUrl)]
    [InlineData(null, UpdateViewModel.ReleasesUrl)]
    public void Only_a_page_of_this_projects_own_repository_is_ever_opened(string? url, string expected)
    {
        UpdateViewModel.SafeReleaseUrl(url).Should().Be(expected);
    }

    [Fact]
    public async Task A_hostile_address_in_the_answer_opens_the_projects_release_page_instead()
    {
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release(url: "https://example.test/indir.exe"));
        var vm = Create();
        await vm.RunStartupCheckAsync();

        vm.OpenReleaseCommand.Execute(null);

        _opened.Should().Equal(UpdateViewModel.ReleasesUrl);
    }

    [Fact]
    public async Task Long_notes_are_cut_and_empty_ones_hide_the_text_area()
    {
        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release(notes: new string('a', 2000)));
        var vm = Create();
        await vm.RunStartupCheckAsync();

        vm.PromptNotes.Length.Should().BeLessThan(800);
        vm.PromptNotes.Should().EndWith("…");
        vm.HasNotes.Should().BeTrue();

        _source.Get = () => Task.FromResult<ReleaseInfo?>(Release(notes: "  \r\n "));
        await vm.RunStartupCheckAsync();
        vm.HasNotes.Should().BeFalse();
    }

    [Fact]
    public void The_installed_version_is_shown_as_three_numbers()
    {
        Create("0.9.14.0").CurrentVersionText.Should().Be("Kurulu sürüm: 0.9.14");
    }
}
