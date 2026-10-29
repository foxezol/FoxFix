using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Fixes;
using FoxFix.Core.Reporting;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-main-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private MainViewModel Create(Action<string>? openLink = null)
    {
        var flow = new FixFlowViewModel([], new UndoJournal(Path.Combine(_dir, "undo.json")), new FakeElevation(), new FakeReboot());
        return new MainViewModel(new DiagnosticsEngine([]), flow, _reports, _clipboard, _saver, NewSpeed(), NewStartup(), NewCleanup(), dpi: null, openLink);
    }

    private readonly FakeReports _reports = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeSaver _saver = new();

    [Fact]
    public void The_app_opens_on_the_home_screen_without_a_back_button()
    {
        var vm = CreateWithChecks();

        vm.Page.Should().Be(AppPage.Home);
        vm.IsHome.Should().BeTrue();
        vm.ShowBack.Should().BeFalse();
        vm.PageTitle.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AppPage.Connection, "Bağlantı kontrolü")]
    [InlineData(AppPage.Speedup, "Bilgisayarı hızlandır")]
    [InlineData(AppPage.DnsDpi, "DNS ve DPI")]
    [InlineData(AppPage.Access, "Erişim testi")]
    [InlineData(AppPage.Report, "Rapor ve geçmiş")]
    public void Each_card_opens_its_own_page_with_its_title_and_a_back_button(AppPage page, string title)
    {
        var vm = CreateWithChecks();

        vm.OpenPageCommand.Execute(page);

        vm.Page.Should().Be(page);
        vm.PageTitle.Should().Be(title);
        vm.ShowBack.Should().BeTrue();
        vm.IsHome.Should().BeFalse();
    }

    [Fact]
    public void Exactly_one_page_is_shown_at_a_time()
    {
        var vm = CreateWithChecks();

        foreach (var page in Enum.GetValues<AppPage>())
        {
            vm.OpenPageCommand.Execute(page);

            new[] { vm.IsHome, vm.IsConnection, vm.IsSpeedup, vm.IsDnsDpi, vm.IsAccess, vm.IsReport }
                .Count(shown => shown).Should().Be(1, $"only {page} is shown");
        }
    }

    [Fact]
    public void Going_home_returns_from_any_page()
    {
        var vm = CreateWithChecks();
        vm.OpenPageCommand.Execute(AppPage.Speedup);

        vm.GoHomeCommand.Execute(null);

        vm.IsHome.Should().BeTrue();
        vm.ShowBack.Should().BeFalse();
    }

    [Fact]
    public void Changing_the_page_tells_the_view_so_the_title_and_back_button_update()
    {
        var vm = CreateWithChecks();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.OpenPageCommand.Execute(AppPage.DnsDpi);

        changed.Should().Contain(["Page", "IsDnsDpi", "IsHome", "ShowBack", "PageTitle"]);
    }

    [Fact]
    public async Task Navigating_does_not_throw_away_the_diagnosis_or_the_report_settings()
    {
        var vm = CreateWithChecks(Check("DNS", CheckStatus.Passed));
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);
        vm.ShowPersonalData = true;

        vm.OpenPageCommand.Execute(AppPage.Report);
        vm.GoHomeCommand.Execute(null);

        vm.Rows.Should().HaveCount(1);
        vm.ShowPersonalData.Should().BeTrue();
        vm.Verdict.Should().Be(VerdictState.Ok);
    }

    [Fact]
    public void The_feedback_button_opens_the_makers_discord_profile()
    {
        var opened = new List<string>();
        var vm = Create(opened.Add);

        vm.OpenFeedbackCommand.Execute(null);

        opened.Should().Equal("https://discord.com/users/381533632852131841");
    }

    [Fact]
    public void A_link_that_cannot_be_opened_is_reported_instead_of_crashing()
    {
        var vm = Create(_ => throw new System.ComponentModel.Win32Exception("tarayıcı yok"));

        vm.OpenFeedbackCommand.Execute(null);

        vm.Notice.IsOpen.Should().BeTrue("the failure is told in a pop-up in the middle of the window");
        vm.Notice.Message.Should().Contain("tarayıcı yok");
    }

    [Fact]
    public void The_info_pop_up_shows_the_version_and_opens_the_GoodbyeDPI_pages()
    {
        var opened = new List<string>();
        var vm = Create(opened.Add);

        vm.Info.OpenCommand.Execute(null);
        vm.Info.IsOpen.Should().BeTrue();
        vm.Info.VersionText.Should().StartWith("Sürüm ").And.MatchRegex(@"\d+\.\d+\.\d+");

        vm.Info.OpenGoodbyeDpiCommand.Execute(null);
        vm.Info.OpenTurkeyForkCommand.Execute(null);
        opened.Should().Equal("https://github.com/ValdikSS/GoodbyeDPI", "https://github.com/cagritaskn/GoodbyeDPI-Turkey");

        vm.Info.CloseCommand.Execute(null);
        vm.Info.IsOpen.Should().BeFalse();
    }
    private MainViewModel CreateWithChecks(params ICheck[] checks)
    {
        var flow = new FixFlowViewModel([], new UndoJournal(Path.Combine(_dir, "undo.json")), new FakeElevation(), new FakeReboot());
        return new MainViewModel(new DiagnosticsEngine(checks), flow, _reports, _clipboard, _saver, NewSpeed(), NewStartup(), NewCleanup());
    }

    private static StartupViewModel NewStartup() =>
        new(new SpeedupFakes.StartupStore(), _ => { }, () => Task.CompletedTask);

    private static CleanupViewModel NewCleanup() =>
        new([], _ => { }, () => Task.CompletedTask);

    private static SpeedTestViewModel NewSpeed() => new(new NeverRunSpeedTest());

    private sealed class NeverRunSpeedTest : FoxFix.Core.SpeedTest.ISpeedTest
    {
        public Task<FoxFix.Core.SpeedTest.SpeedTestResult> RunAsync(IProgress<FoxFix.Core.SpeedTest.SpeedTestProgress>? progress, CancellationToken ct) =>
            throw new InvalidOperationException("not used here");
    }

    [Fact]
    public async Task Copying_puts_the_masked_report_on_the_clipboard_by_default()
    {
        var vm = CreateWithChecks();

        await vm.CopyReportCommand.ExecuteAsync(null);

        _clipboard.Copied.Should().Equal("RAPOR(gizli)");
        _reports.Calls.Should().ContainSingle().Which.Masked.Should().BeTrue();
        vm.StatusText.Should().Be("Rapor kopyalandı.");
    }

    [Fact]
    public async Task Ticking_show_personal_data_asks_for_the_unmasked_report()
    {
        var vm = CreateWithChecks();
        vm.ShowPersonalData = true;

        await vm.CopyReportCommand.ExecuteAsync(null);

        _reports.Calls.Single().Masked.Should().BeFalse();
        _clipboard.Copied.Should().Equal("RAPOR(açık)");
    }

    [Fact]
    public async Task The_report_uses_the_latest_diagnosis_once_one_was_run()
    {
        var vm = CreateWithChecks(Check("DNS", CheckStatus.Passed));

        await vm.CopyReportCommand.ExecuteAsync(null);
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);
        await vm.CopyReportCommand.ExecuteAsync(null);

        _reports.Calls[0].Diagnosis.Should().BeNull("no diagnosis yet");
        _reports.Calls[1].Diagnosis!.Reports.Select(r => r.Title).Should().Equal("DNS");
    }

    [Fact]
    public async Task A_report_that_cannot_be_built_is_reported_and_nothing_is_copied()
    {
        _reports.Throws = true;
        var vm = CreateWithChecks();

        await vm.CopyReportCommand.ExecuteAsync(null);

        vm.StatusText.Should().Contain("Rapor hazırlanamadı").And.Contain("rapor bozuk");
        _clipboard.Copied.Should().BeEmpty();
    }

    [Fact]
    public async Task A_locked_clipboard_is_reported_not_thrown()
    {
        _clipboard.Throws = true;
        var vm = CreateWithChecks();

        await vm.CopyReportCommand.ExecuteAsync(null);

        vm.StatusText.Should().Contain("Rapor kopyalanamadı").And.Contain("pano dolu");
    }

    [Fact]
    public async Task Saving_offers_a_dated_txt_name_and_announces_where_it_went()
    {
        _saver.ChosenPath = @"C:\Belgelerim\rapor.txt";
        var vm = CreateWithChecks();

        await vm.SaveReportCommand.ExecuteAsync(null);

        _saver.Offered.Should().ContainSingle().Which.Name.Should().MatchRegex(@"^FoxFix-rapor-\d{8}-\d{4}\.txt$");
        _saver.Offered[0].Content.Should().Be("RAPOR(gizli)");
        vm.StatusText.Should().Be(@"Rapor kaydedildi: C:\Belgelerim\rapor.txt");
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_changes_nothing()
    {
        _saver.ChosenPath = null;
        var vm = CreateWithChecks();
        vm.StatusText = "eski mesaj";

        await vm.SaveReportCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be("eski mesaj");
    }

    [Fact]
    public async Task A_save_that_fails_is_reported()
    {
        _saver.Throws = true;
        var vm = CreateWithChecks();

        await vm.SaveReportCommand.ExecuteAsync(null);

        vm.StatusText.Should().Contain("Rapor kaydedilemedi").And.Contain("disk dolu");
    }

    private sealed class FakeReports : IDiagnosticReportService
    {
        public List<(DiagnosticsReport? Diagnosis, bool Masked)> Calls { get; } = [];

        public bool Throws { get; set; }

        public Task<string> CreateAsync(DiagnosticsReport? lastDiagnosis, bool maskPersonalData, CancellationToken ct)
        {
            Calls.Add((lastDiagnosis, maskPersonalData));
            return Throws
                ? throw new InvalidOperationException("rapor bozuk")
                : Task.FromResult(maskPersonalData ? "RAPOR(gizli)" : "RAPOR(açık)");
        }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public List<string> Copied { get; } = [];

        public bool Throws { get; set; }

        public void SetText(string text)
        {
            if (Throws)
            {
                throw new InvalidOperationException("pano dolu");
            }

            Copied.Add(text);
        }
    }

    private sealed class FakeSaver : IReportFileSaver
    {
        public List<(string Name, string Content)> Offered { get; } = [];

        public string? ChosenPath { get; set; } = @"C:\x.txt";

        public bool Throws { get; set; }

        public Task<string?> SaveAsync(string suggestedFileName, string content, CancellationToken ct)
        {
            Offered.Add((suggestedFileName, content));
            return Throws ? throw new IOException("disk dolu") : Task.FromResult(ChosenPath);
        }
    }

    private static StubCheck Check(string title, CheckStatus status) => new(title, status);

    [Fact]
    public void Before_the_first_check_the_verdict_says_it_is_looking()
    {
        var vm = CreateWithChecks();

        vm.Verdict.Should().Be(VerdictState.Checking);
        vm.VerdictTitle.Should().Contain("kontrol ediliyor");
        vm.HasRows.Should().BeFalse();
    }

    [Fact]
    public async Task All_clear_gives_a_calm_verdict_and_one_row_per_layer()
    {
        var vm = CreateWithChecks(Check("Ağ bağlantısı", CheckStatus.Passed), Check("DNS", CheckStatus.Passed));

        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        vm.Verdict.Should().Be(VerdictState.Ok);
        vm.VerdictTitle.Should().Be("Sistem Durumu Normal");
        vm.Rows.Select(r => r.Title).Should().Equal("Ağ bağlantısı", "DNS");
        vm.HasRows.Should().BeTrue();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Warnings_are_counted_in_plain_words()
    {
        var vm = CreateWithChecks(Check("DNS", CheckStatus.Warning), Check("Kalıntı", CheckStatus.Warning), Check("Ağ", CheckStatus.Passed));

        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        vm.Verdict.Should().Be(VerdictState.Attention);
        vm.VerdictTitle.Should().Be("Bağlantın çalışıyor, ama 2 şeye bakmak gerek");
        vm.VerdictDetail.Should().Contain("Beni Düzelt");
    }

    [Fact]
    public async Task A_check_that_itself_broke_is_also_worth_a_note()
    {
        var vm = CreateWithChecks(Check("Kalıntı", CheckStatus.Error));

        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        vm.Verdict.Should().Be(VerdictState.Attention);
    }

    [Fact]
    public async Task A_failed_layer_is_named_and_the_user_is_told_nothing_changes_without_consent()
    {
        var vm = CreateWithChecks(Check("Ağ bağlantısı", CheckStatus.Passed), Check("İnternet adreslerini bulma (DNS)", CheckStatus.Failed));

        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        vm.Verdict.Should().Be(VerdictState.Problem);
        vm.VerdictTitle.Should().Be("Bir sorun var");
        vm.VerdictDetail.Should().Contain("İnternet adreslerini bulma (DNS)").And.Contain("onaylamadan hiçbir şey değişmez");
    }

    [Fact]
    public async Task A_second_run_replaces_the_old_results()
    {
        var vm = CreateWithChecks(Check("DNS", CheckStatus.Failed));
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(1);
    }

    [Fact]
    public async Task Each_result_kind_is_marked_with_a_symbol_and_a_theme_color_kind()
    {
        var vm = CreateWithChecks(
            Check("a", CheckStatus.Passed),
            Check("b", CheckStatus.Warning),
            Check("c", CheckStatus.Error),
            Check("d", CheckStatus.Skipped),
            Check("e", CheckStatus.Failed));

        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        vm.Rows.Select(r => r.Kind).Should().Equal(
            StatusKind.Ok, StatusKind.Warn, StatusKind.Warn, StatusKind.Neutral, StatusKind.Bad);

        // Four different drawings, and screen readers get a spoken label instead of the picture.
        vm.Rows.Select(r => r.Icon).Distinct().Should().HaveCount(4);
        vm.Rows.Select(r => r.StatusLabel).Should().Equal(
            "Sorun yok", "Dikkat", "Dikkat", "Atlandı", "Sorun var");
    }

    [Fact]
    public void The_status_drawings_stay_inside_their_16_by_16_grid_with_room_for_the_stroke()
    {
        foreach (var icon in new[] { StatusIcons.Ok, StatusIcons.Bad, StatusIcons.Warn, StatusIcons.Neutral })
        {
            var bounds = icon.Bounds;

            bounds.Left.Should().BeGreaterThanOrEqualTo(1);
            bounds.Top.Should().BeGreaterThanOrEqualTo(1);
            bounds.Right.Should().BeLessThanOrEqualTo(15);
            bounds.Bottom.Should().BeLessThanOrEqualTo(15);
            icon.IsFrozen.Should().BeTrue();
        }
    }

    private sealed class StubCheck : ICheck
    {
        private readonly CheckStatus _status;

        public StubCheck(string title, CheckStatus status)
        {
            Title = title;
            _status = status;
        }

        public string Id => Title;

        public string Title { get; }

        public Task<CheckResult> RunAsync(CancellationToken ct) => Task.FromResult(new CheckResult(_status, Title + " özeti"));
    }

    private sealed class FakeElevation : IElevation
    {
        public bool IsElevated => true;

        public bool RestartElevated(string? arguments = null) => true;
    }

    private sealed class FakeReboot : IRebootService
    {
        public Task<bool> ScheduleRestartAsync(int delaySeconds, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> CancelAsync(CancellationToken ct) => Task.FromResult(true);
    }
}
