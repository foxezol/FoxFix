using FluentAssertions;
using FoxFix.App;
using FoxFix.Core;
using FoxFix.Core.Settings;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class OnboardingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-onboarding-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "ayarlar.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private (SettingsViewModel Settings, OnboardingViewModel Onboarding, List<AppPage> Pages) Create(AppSettings? initial = null, Action? restart = null)
    {
        var pages = new List<AppPage>();
        var settings = new SettingsViewModel(new AppSettingsStore(FilePath), initial ?? new AppSettings { Language = Strings.Turkish }, _ => { }, restart: restart);
        return (settings, new OnboardingViewModel(settings, pages.Add), pages);
    }

    private AppSettings Saved() => new AppSettingsStore(FilePath).Load();

    // ---------------------------------------------------------------- the question after the language

    [Fact]
    public void The_first_start_asks_whether_to_take_the_tour_and_does_not_start_it_by_itself()
    {
        var (_, onboarding, _) = Create();

        onboarding.OfferTourIfFirstRun();

        onboarding.IsTourPromptOpen.Should().BeTrue();
        onboarding.Tour.IsOpen.Should().BeFalse("the person is asked first");
    }

    [Theory]
    [InlineData(TourState.Skipped)]
    [InlineData(TourState.Completed)]
    public void After_an_answer_the_question_never_comes_back(TourState answered)
    {
        var (_, onboarding, _) = Create(new AppSettings { Language = Strings.Turkish, Tour = answered });

        onboarding.OfferTourIfFirstRun();

        onboarding.IsTourPromptOpen.Should().BeFalse();
    }

    [Fact]
    public void Saying_yes_starts_the_tour_from_the_first_step_on_the_overview()
    {
        var (_, onboarding, pages) = Create();
        onboarding.OfferTourIfFirstRun();

        onboarding.StartTourFromPromptCommand.Execute(null);

        onboarding.IsTourPromptOpen.Should().BeFalse();
        onboarding.Tour.IsOpen.Should().BeTrue();
        onboarding.Tour.Index.Should().Be(0);
        pages.Should().Equal(AppPage.Home);
    }

    [Fact]
    public void Saying_not_now_counts_as_skipped_and_is_remembered_across_restarts()
    {
        var (_, onboarding, _) = Create();
        onboarding.OfferTourIfFirstRun();

        onboarding.DeclineTourCommand.Execute(null);

        onboarding.IsTourPromptOpen.Should().BeFalse();
        Saved().Tour.Should().Be(TourState.Skipped);

        var (_, again, _) = Create(Saved());
        again.OfferTourIfFirstRun();
        again.IsTourPromptOpen.Should().BeFalse("it was answered");
    }

    // ---------------------------------------------------------------- the tour

    [Fact]
    public void The_tour_goes_forward_and_back_and_the_first_step_has_no_back()
    {
        var (_, onboarding, _) = Create();
        var tour = onboarding.Tour;
        tour.Start();

        tour.CanGoBack.Should().BeFalse();
        tour.BackCommand.CanExecute(null).Should().BeFalse();

        tour.NextCommand.Execute(null);
        tour.NextCommand.Execute(null);

        tour.Index.Should().Be(2);
        tour.CanGoBack.Should().BeTrue();

        tour.BackCommand.Execute(null);

        tour.Index.Should().Be(1);
        tour.StepText.Should().Be(Strings.Format(Strings.TourStepOf, 2, tour.Steps.Count));
    }

    [Fact]
    public void The_sound_and_camera_step_is_only_in_the_tour_while_that_page_is_switched_on()
    {
        var (_, off, _) = Create();
        var (_, on, _) = Create(new AppSettings { Language = Strings.Turkish, ExperimentalDevices = true });

        off.Tour.Start();
        on.Tour.Start();

        off.Tour.Steps.Select(x => x.TargetName).Should().NotContain("NavDevices");
        on.Tour.Steps.Select(x => x.TargetName).Should().Contain("NavDevices");
        on.Tour.Steps.Count.Should().Be(off.Tour.Steps.Count + 1);
    }

    [Fact]
    public void Finishing_the_last_step_completes_the_tour_and_remembers_it()
    {
        var (_, onboarding, _) = Create();
        var tour = onboarding.Tour;
        tour.Start();

        for (var i = 0; i < tour.Steps.Count - 1; i++)
        {
            tour.IsLast.Should().BeFalse();
            tour.NextCommand.Execute(null);
        }

        tour.IsLast.Should().BeTrue();
        tour.NextCommand.Execute(null);

        tour.IsOpen.Should().BeFalse();
        Saved().Tour.Should().Be(TourState.Completed);
        Saved().DpiHintSeen.Should().BeTrue("the tour already explained FoxDPI");
    }

    [Fact]
    public void Skipping_in_the_middle_counts_as_skipped_and_leaves_the_dpi_hint_for_later()
    {
        var (_, onboarding, _) = Create();
        onboarding.Tour.Start();
        onboarding.Tour.NextCommand.Execute(null);

        onboarding.Tour.SkipCommand.Execute(null);

        onboarding.Tour.IsOpen.Should().BeFalse();
        Saved().Tour.Should().Be(TourState.Skipped);
        Saved().DpiHintSeen.Should().BeFalse();
    }

    [Fact]
    public void A_closed_tour_ignores_buttons_and_does_not_report_an_end_twice()
    {
        var (_, onboarding, _) = Create();
        var ends = new List<TourState>();
        onboarding.Tour.Ended += ends.Add;

        onboarding.Tour.NextCommand.Execute(null);
        onboarding.Tour.SkipCommand.Execute(null);
        onboarding.Tour.Start();
        onboarding.Tour.SkipCommand.Execute(null);
        onboarding.Tour.SkipCommand.Execute(null);

        ends.Should().Equal(TourState.Skipped);
    }

    [Fact]
    public void The_tour_can_be_started_again_any_time_from_the_settings_page_and_starts_from_the_top()
    {
        var (_, onboarding, pages) = Create(new AppSettings { Language = Strings.Turkish, Tour = TourState.Completed });
        onboarding.Tour.Start();
        onboarding.Tour.NextCommand.Execute(null);
        onboarding.Tour.SkipCommand.Execute(null);
        pages.Clear();

        onboarding.StartTourFromSettingsCommand.Execute(null);

        onboarding.Tour.IsOpen.Should().BeTrue();
        onboarding.Tour.Index.Should().Be(0);
        pages.Should().Equal(AppPage.Home);
    }

    [Fact]
    public void Every_step_has_a_title_and_a_text_in_both_languages()
    {
        foreach (var step in TourViewModel.AllSteps)
        {
            Strings.GetIn(step.TitleKey, Strings.Turkish).Should().NotBeNullOrWhiteSpace();
            Strings.GetIn(step.TitleKey, Strings.English).Should().NotBeNullOrWhiteSpace();
            Strings.GetIn(step.TextKey, Strings.Turkish).Should().NotBeNullOrWhiteSpace();
            Strings.GetIn(step.TextKey, Strings.English).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void The_tour_covers_the_main_places_and_not_things_that_do_not_exist()
    {
        var targets = TourViewModel.AllSteps.Select(s => s.TargetName).Where(n => n is not null).ToList();

        targets.Should().BeEquivalentTo("TourFixButton", "NavConnection", "NavTweaks", "NavDnsDpi", "NavAccess", "NavDevices", "NavReport", "NavSettings");

        // Every pointed-at element really is named in the window (a rename would silently break the tour otherwise).
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "FoxFix.App", "MainWindow.xaml"));
        foreach (var name in targets)
        {
            xaml.Should().Contain($"x:Name=\"{name}\"", $"the tour points at {name}");
        }
    }

    [Fact]
    public void The_first_and_last_steps_are_in_the_middle_of_the_window_and_the_others_point_at_something()
    {
        TourViewModel.AllSteps[0].TargetName.Should().BeNull();
        TourViewModel.AllSteps[^1].TargetName.Should().BeNull();
        TourViewModel.AllSteps.Skip(1).Take(TourViewModel.AllSteps.Count - 2).Should().OnlyContain(s => s.TargetName != null);
    }

    // ---------------------------------------------------------------- the FoxDPI hint that replaced the pop-up

    [Fact]
    public void Somebody_who_skipped_the_tour_sees_the_dpi_explanation_once_on_the_dpi_page()
    {
        var (_, onboarding, _) = Create(new AppSettings { Language = Strings.Turkish, Tour = TourState.Skipped });

        onboarding.OnPageEntered(AppPage.Access);
        onboarding.IsDpiHintOpen.Should().BeFalse("only the DNS ve DPI page shows it");

        onboarding.OnPageEntered(AppPage.DnsDpi);
        onboarding.IsDpiHintOpen.Should().BeTrue();

        onboarding.CloseDpiHintCommand.Execute(null);
        onboarding.IsDpiHintOpen.Should().BeFalse();
        Saved().DpiHintSeen.Should().BeTrue();

        onboarding.OnPageEntered(AppPage.DnsDpi);
        onboarding.IsDpiHintOpen.Should().BeFalse("it shows once");
    }

    [Theory]
    [InlineData(TourState.Completed)]
    [InlineData(TourState.NotOffered)]
    public void The_dpi_explanation_is_only_for_those_who_skipped_the_tour(TourState state)
    {
        var (_, onboarding, _) = Create(new AppSettings { Language = Strings.Turkish, Tour = state });

        onboarding.OnPageEntered(AppPage.DnsDpi);

        onboarding.IsDpiHintOpen.Should().BeFalse();
    }

    [Fact]
    public void The_dpi_explanation_does_not_open_on_top_of_a_running_tour()
    {
        var (_, onboarding, _) = Create(new AppSettings { Language = Strings.Turkish, Tour = TourState.Skipped });
        onboarding.Tour.Start();

        onboarding.OnPageEntered(AppPage.DnsDpi);

        onboarding.IsDpiHintOpen.Should().BeFalse();
    }

    // ---------------------------------------------------------------- language in the settings

    [Fact]
    public void The_language_choice_is_saved_at_once_and_offers_a_reopen()
    {
        var restarted = 0;
        var (settings, _, _) = Create(restart: () => restarted++);

        settings.IsEnglish = true;

        settings.Language.Should().Be(Strings.English);
        settings.IsTurkish.Should().BeFalse();
        Saved().Language.Should().Be(Strings.English);
        settings.IsRestartPromptOpen.Should().BeTrue();
        restarted.Should().Be(0, "nothing reopens without the person's yes");

        settings.RestartNowCommand.Execute(null);

        restarted.Should().Be(1);
        settings.IsRestartPromptOpen.Should().BeFalse();
    }

    [Fact]
    public void Later_keeps_the_choice_and_closes_the_question()
    {
        var restarted = 0;
        var (settings, _, _) = Create(restart: () => restarted++);
        settings.IsEnglish = true;

        settings.RestartLaterCommand.Execute(null);

        settings.IsRestartPromptOpen.Should().BeFalse();
        restarted.Should().Be(0);
        Saved().Language.Should().Be(Strings.English);
    }

    [Fact]
    public void Opening_the_page_with_the_saved_language_does_not_ask_to_reopen()
    {
        var (settings, _, _) = Create(new AppSettings { Language = Strings.English });

        settings.IsEnglish.Should().BeTrue();
        settings.IsRestartPromptOpen.Should().BeFalse();
        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public void Tour_state_and_language_are_kept_when_another_setting_is_saved()
    {
        var (settings, _, _) = Create(new AppSettings { Language = Strings.English, Tour = TourState.Skipped, DpiHintSeen = true });

        settings.AutoCheckUpdates = false;

        var saved = Saved();
        saved.Language.Should().Be(Strings.English);
        saved.Tour.Should().Be(TourState.Skipped);
        saved.DpiHintSeen.Should().BeTrue();
    }

    [Fact]
    public void A_damaged_language_or_tour_value_in_the_file_falls_back_to_the_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"Language\":\"klingon\",\"Tour\":\"Sideways\"}");

        var loaded = new AppSettingsStore(FilePath).Load();

        loaded.Language.Should().BeEmpty("the first start asks again");
        loaded.Tour.Should().Be(TourState.NotOffered);
    }

    [Fact]
    public void An_old_settings_file_without_the_new_fields_loads_and_asks_for_language_and_tour()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"AutoCheckUpdates\":false,\"AccentColor\":\"#2F7DF6\"}");

        var loaded = new AppSettingsStore(FilePath).Load();

        loaded.AutoCheckUpdates.Should().BeFalse();
        loaded.Language.Should().BeEmpty();
        loaded.Tour.Should().Be(TourState.NotOffered);
        loaded.DpiHintSeen.Should().BeFalse();
    }

    // ---------------------------------------------------------------- the old FoxDPI pop-up is gone

    [Fact]
    public void The_old_foxdpi_explanation_texts_are_gone_from_the_resource_files()
    {
        var keys = Strings.AllKeys().ToList();

        keys.Should().NotContain("FoxDpiExplain").And.NotContain("FoxDpiExplainTitle");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FoxFix.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("FoxFix.sln not found above the test folder.");
    }
}
