using FluentAssertions;
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
        vm.AccentInput.Should().Be("#2F7DF6");
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
    public void Picking_a_colour_applies_it_selects_it_and_fills_the_box()
    {
        var vm = Create();

        vm.PickAccentCommand.Execute("#e5484d");

        vm.Accent.Should().Be("#E5484D");
        vm.AccentInput.Should().Be("#E5484D");
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
    public void A_complete_typed_colour_becomes_the_accent_without_rewriting_what_was_typed()
    {
        var vm = Create();

        vm.AccentInput = "#2f7df6";

        vm.Accent.Should().Be("#2F7DF6");
        vm.AccentInput.Should().Be("#2f7df6");
        vm.AccentError.Should().BeEmpty();
        _applied.Last().AccentColor.Should().Be("#2F7DF6");
    }

    [Fact]
    public void The_hash_sign_may_be_left_out()
    {
        var vm = Create();

        vm.AccentInput = "2F7DF6";

        vm.Accent.Should().Be("#2F7DF6");
        vm.AccentInput.Should().Be("#2F7DF6");
    }

    [Theory]
    [InlineData("#2F7D")]
    [InlineData("#")]
    [InlineData("")]
    public void A_colour_still_being_typed_says_nothing_and_changes_nothing(string typed)
    {
        var vm = Create();

        vm.AccentInput = typed;

        vm.AccentError.Should().BeEmpty();
        vm.Accent.Should().Be(AppSettings.DefaultAccent);
        _applied.Should().BeEmpty();
    }

    [Theory]
    [InlineData("#GGGGGG")]
    [InlineData("#12345Z")]
    [InlineData("mavi renk")]
    public void A_full_length_wrong_colour_is_explained_and_not_applied(string typed)
    {
        var vm = Create();

        vm.AccentInput = typed;

        vm.AccentError.Should().Contain("#RRGGBB");
        vm.HasAccentError.Should().BeTrue();
        vm.Accent.Should().Be(AppSettings.DefaultAccent);
        _applied.Should().BeEmpty();
    }

    [Fact]
    public void A_correction_clears_the_explanation()
    {
        var vm = Create();
        vm.AccentInput = "#GGGGGG";

        vm.AccentInput = "#00FF00";

        vm.HasAccentError.Should().BeFalse();
        vm.Accent.Should().Be("#00FF00");
    }

    [Fact]
    public void The_default_colour_button_goes_back_to_the_brand_purple()
    {
        var vm = Create(new AppSettings { AccentColor = "#2F7DF6" });

        vm.ResetAccentCommand.Execute(null);

        vm.Accent.Should().Be("#9E20E6");
        vm.AccentInput.Should().Be("#9E20E6");
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

        _applied.Last().Should().Be(new AppSettings { Theme = ThemeMode.Light, AccentColor = "#14A3C7", AutoCheckUpdates = false, RecolorLogo = true });
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
