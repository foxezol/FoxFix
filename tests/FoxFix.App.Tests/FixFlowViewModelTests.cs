using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class FixFlowViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-app-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeFix _fix = new();

    private readonly FakeElevation _elevation = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private UndoJournal NewJournal() => new(Path.Combine(_dir, "undo.json"));

    private readonly FakeReboot _reboot = new();

    private FixFlowViewModel Create(UndoJournal? journal = null) =>
        new([_fix], journal ?? NewJournal(), _elevation, _reboot);

    private async Task<FixFlowViewModel> ApplyWithRebootAsync(bool autoRestart, bool success = true, bool rebootNeeded = true)
    {
        _elevation.IsElevated = true;
        _fix.Preview = new FixPreview(["WinDivert silinecek"], RequiresReboot: true);
        _fix.Result = new FixResult(success, "Sonuç.", RequiresReboot: rebootNeeded);
        var vm = Create();
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;
        vm.AutoRestart = autoRestart;

        await vm.ApplyCommand.ExecuteAsync(null);
        return vm;
    }

    private FixFlowViewModel CreateWith(FakeFix second, UndoJournal? journal = null) =>
        new([_fix, second], journal ?? NewJournal(), _elevation, _reboot);

    [Fact]
    public async Task A_single_button_can_start_a_repair_fix_by_its_id_without_touching_the_others()
    {
        _elevation.IsElevated = true;
        var dns = new FakeFix { Id = "dns-repair", Title = "DNS", Preview = new FixPreview(["DNS otomatiğe alınacak"]) };
        var vm = CreateWith(dns);

        await vm.StartSingleCommand.ExecuteAsync("dns-repair");

        vm.PreviewLines.Select(l => l.Text).Should().Equal("DNS otomatiğe alınacak");
        _fix.PreviewCalls.Should().Be(0);

        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        dns.ApplyCalls.Should().Be(1);
        _fix.ApplyCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData(null)]
    public async Task An_unknown_id_on_the_single_button_does_nothing(string? fixId)
    {
        var vm = Create();

        await vm.StartSingleCommand.ExecuteAsync(fixId);

        vm.Stage.Should().Be(FlowStage.Idle);
        vm.IsPanelVisible.Should().BeFalse();
    }

    [Fact]
    public async Task A_fix_that_needs_no_admin_rights_runs_without_a_UAC_prompt()
    {
        _elevation.IsElevated = false;
        var light = new FakeFix { Id = "startup", IncludedInRepair = false, RequiresAdmin = false, Result = new FixResult(true, "Tamam.") };
        var vm = new FixFlowViewModel([light], NewJournal(), _elevation, _reboot);

        await vm.StartOptionalCommand.ExecuteAsync("startup");

        vm.ApplyLabel.Should().Be("Uygula");
        vm.AdminNote.Should().BeEmpty();

        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        _elevation.RestartCalls.Should().Be(0);
        light.ApplyCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_fix_that_needs_admin_rights_still_asks_for_them()
    {
        _elevation.IsElevated = false;
        var heavy = new FakeFix { Id = "disk-cleanup", IncludedInRepair = false, RequiresAdmin = true };
        var vm = new FixFlowViewModel([heavy], NewJournal(), _elevation, _reboot);

        await vm.StartOptionalCommand.ExecuteAsync("disk-cleanup");

        vm.ApplyLabel.Should().Be("Yönetici olarak uygula");
        vm.AdminNote.Should().Contain("yönetici izni");
    }

    private static FakeFix OptionalFix() => new()
    {
        Id = "optional-fix",
        Title = "Ek ayar",
        IncludedInRepair = false,
        Preview = new FixPreview(["Ek ayar uygulanacak"]),
        Result = new FixResult(true, "Ek ayar tamam."),
    };

    [Fact]
    public async Task Beni_Duzelt_never_previews_or_applies_an_optional_fix()
    {
        _elevation.IsElevated = true;
        var optional = OptionalFix();
        var vm = CreateWith(optional);

        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        optional.PreviewCalls.Should().Be(0);
        optional.ApplyCalls.Should().Be(0);
        _fix.ApplyCalls.Should().Be(1);
        vm.Message.Should().NotContain("Ek ayar");
    }

    [Fact]
    public async Task The_optional_button_previews_and_applies_only_that_fix()
    {
        _elevation.IsElevated = true;
        var optional = OptionalFix();
        var vm = CreateWith(optional);

        await vm.StartOptionalCommand.ExecuteAsync("optional-fix");

        vm.PreviewLines.Select(l => l.Text).Should().Equal("Ek ayar uygulanacak");
        vm.ShowConfirmation.Should().BeTrue();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse("it still needs the confirmation tick");

        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        optional.ApplyCalls.Should().Be(1);
        _fix.PreviewCalls.Should().Be(0);
        _fix.ApplyCalls.Should().Be(0);
        vm.Message.Should().Be("Ek ayar tamam.");
    }

    [Theory]
    [InlineData("nope")]
    [InlineData(null)]
    [InlineData("fake-fix")] // a repair fix cannot be started through the optional door
    public async Task Unknown_or_repair_ids_do_nothing_on_the_optional_button(string? fixId)
    {
        var vm = CreateWith(OptionalFix());

        await vm.StartOptionalCommand.ExecuteAsync(fixId);

        vm.Stage.Should().Be(FlowStage.Idle);
        vm.IsPanelVisible.Should().BeFalse();
        _fix.PreviewCalls.Should().Be(0);
    }

    [Fact]
    public async Task Undo_of_an_optional_fix_is_routed_to_it()
    {
        _elevation.IsElevated = true;
        var journal = NewJournal();
        var optional = OptionalFix();
        optional.JournalOnApply = journal;
        var vm = CreateWith(optional, journal);
        await vm.StartOptionalCommand.ExecuteAsync("optional-fix");
        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.UndoCommand.ExecuteAsync(vm.UndoItems.Single());

        optional.UndoneIds.Should().ContainSingle();
        _fix.UndoneIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Several_fixes_with_changes_are_listed_under_their_own_headers()
    {
        var dns = new FakeFix { Id = "dns", Title = "DNS ayarını onar", Preview = new FixPreview(["Ethernet 3 otomatiğe alınacak"]) };
        var vm = CreateWith(dns);

        await vm.StartCommand.ExecuteAsync(null);

        vm.PreviewLines.Select(l => (l.Text, l.IsHeader)).Should().Equal(
            ("Sahte", true), ("GoodbyeDPI silinecek", false),
            ("DNS ayarını onar", true), ("Ethernet 3 otomatiğe alınacak", false));
    }

    [Fact]
    public async Task A_fix_with_nothing_to_do_is_left_out_when_another_has_changes()
    {
        var dns = new FakeFix { Id = "dns", Title = "DNS", Preview = new FixPreview(["DNS temiz."], HasChanges: false) };
        var vm = CreateWith(dns);

        await vm.StartCommand.ExecuteAsync(null);

        vm.PreviewLines.Select(l => l.Text).Should().Equal("GoodbyeDPI silinecek");
        vm.HasChanges.Should().BeTrue();
    }

    [Fact]
    public async Task When_no_fix_has_changes_every_nothing_to_do_note_is_shown()
    {
        _fix.Preview = new FixPreview(["Temizlenecek bir şey yok."], HasChanges: false);
        var dns = new FakeFix { Id = "dns", Preview = new FixPreview(["DNS temiz."], HasChanges: false) };
        var vm = CreateWith(dns);

        await vm.StartCommand.ExecuteAsync(null);

        vm.PreviewLines.Select(l => l.Text).Should().Equal("Temizlenecek bir şey yok.", "DNS temiz.");
        vm.HasChanges.Should().BeFalse();
        vm.ShowConfirmation.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_runs_every_fix_in_order_and_joins_their_summaries()
    {
        _elevation.IsElevated = true;
        var order = new List<string>();
        _fix.OnApply = () => order.Add("cleanup");
        var dns = new FakeFix { Id = "dns", Result = new FixResult(true, "DNS tamam."), OnApply = () => order.Add("dns") };
        var vm = CreateWith(dns);
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        order.Should().Equal("cleanup", "dns");
        vm.Message.Should().Contain("1 öğe temizlendi.").And.Contain("DNS tamam.");
    }

    [Fact]
    public async Task Undo_is_routed_to_the_fix_that_wrote_the_record()
    {
        _elevation.IsElevated = true;
        var journal = NewJournal();
        var dns = new FakeFix { Id = "dns", JournalOnApply = journal };
        var vm = CreateWith(dns, journal);
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.UndoCommand.ExecuteAsync(vm.UndoItems.Single());

        dns.UndoneIds.Should().ContainSingle();
        _fix.UndoneIds.Should().BeEmpty();
    }

    [Fact]
    public async Task The_auto_restart_tick_is_only_offered_when_a_restart_is_expected()
    {
        var plain = Create();
        await plain.StartCommand.ExecuteAsync(null);
        plain.ShowAutoRestartOption.Should().BeFalse();

        _fix.Preview = new FixPreview(["WinDivert silinecek"], RequiresReboot: true);
        var withReboot = Create();
        await withReboot.StartCommand.ExecuteAsync(null);
        withReboot.ShowAutoRestartOption.Should().BeTrue();
    }

    [Fact]
    public async Task With_the_tick_a_clean_cleanup_schedules_a_cancellable_restart()
    {
        var vm = await ApplyWithRebootAsync(autoRestart: true);

        _reboot.Scheduled.Should().Equal(FixFlowViewModel.AutoRestartDelaySeconds);
        vm.RebootScheduled.Should().BeTrue();
        vm.ShowRebootBanner.Should().BeFalse();
        vm.Message.Should().Contain("5 saniye sonra yeniden başlayacak");
        vm.AutoRestart.Should().BeFalse("the tick must be chosen again next time");
    }

    [Fact]
    public async Task Without_the_tick_the_user_gets_a_banner_and_nothing_restarts()
    {
        var vm = await ApplyWithRebootAsync(autoRestart: false);

        _reboot.Scheduled.Should().BeEmpty();
        vm.RebootNeeded.Should().BeTrue();
        vm.ShowRebootBanner.Should().BeTrue();
    }

    [Fact]
    public async Task A_cleanup_with_errors_never_restarts_by_itself()
    {
        var vm = await ApplyWithRebootAsync(autoRestart: true, success: false);

        _reboot.Scheduled.Should().BeEmpty();
        vm.ShowRebootBanner.Should().BeTrue();
    }

    [Fact]
    public async Task A_ticked_restart_happens_even_when_the_run_itself_found_nothing_left_over()
    {
        // The tick was only offered because the preview said a restart is needed: ticking it means "restart afterwards".
        var vm = await ApplyWithRebootAsync(autoRestart: true, rebootNeeded: false);

        _reboot.Scheduled.Should().Equal(FixFlowViewModel.AutoRestartDelaySeconds);
        FixFlowViewModel.AutoRestartDelaySeconds.Should().Be(5);
        vm.RebootScheduled.Should().BeTrue();
    }

    [Fact]
    public async Task Without_the_tick_no_restart_is_scheduled_even_if_one_is_needed()
    {
        var vm = await ApplyWithRebootAsync(autoRestart: false);

        _reboot.Scheduled.Should().BeEmpty();
        vm.ShowRebootBanner.Should().BeTrue();
    }

    [Fact]
    public async Task A_finished_change_says_plainly_whether_it_worked()
    {
        var vm = await ApplyWithRebootAsync(autoRestart: false);

        vm.HasResultTitle.Should().BeTrue();
        vm.ResultSucceeded.Should().BeTrue();
        vm.ResultTitle.Should().Be("İşlem başarılı");
        vm.ShowBusyNote.Should().BeFalse();
    }

    [Fact]
    public async Task Restart_now_schedules_a_short_countdown()
    {
        var vm = await ApplyWithRebootAsync(autoRestart: false);

        await vm.RestartNowCommand.ExecuteAsync(null);

        _reboot.Scheduled.Should().Equal(FixFlowViewModel.RestartNowDelaySeconds);
        vm.RebootScheduled.Should().BeTrue();
    }

    [Fact]
    public async Task A_restart_that_cannot_be_scheduled_tells_the_user_to_do_it_manually()
    {
        _reboot.ScheduleSucceeds = false;
        var vm = await ApplyWithRebootAsync(autoRestart: true);

        vm.RebootScheduled.Should().BeFalse();
        vm.Message.Should().Contain("kendin yeniden başlat");
        vm.ShowRebootBanner.Should().BeTrue();
    }

    [Fact]
    public async Task A_throwing_reboot_service_is_reported_not_thrown()
    {
        _reboot.ScheduleThrows = true;
        var vm = await ApplyWithRebootAsync(autoRestart: true);

        vm.Message.Should().Contain("kendin yeniden başlat").And.Contain("shutdown yok");
    }

    [Fact]
    public async Task Cancelling_the_restart_aborts_it_and_brings_the_banner_back()
    {
        var vm = await ApplyWithRebootAsync(autoRestart: true);

        await vm.CancelRestartCommand.ExecuteAsync(null);

        _reboot.CancelCalls.Should().Be(1);
        vm.RebootScheduled.Should().BeFalse();
        vm.ShowRebootBanner.Should().BeTrue();
        vm.Message.Should().Contain("iptal edildi");
    }

    [Fact]
    public async Task A_failed_cancel_keeps_the_scheduled_state_honest()
    {
        var vm = await ApplyWithRebootAsync(autoRestart: true);
        _reboot.CancelSucceeds = false;

        await vm.CancelRestartCommand.ExecuteAsync(null);

        vm.Message.Should().Contain("iptal edilemedi");
    }

    private sealed class FakeReboot : IRebootService
    {
        public List<int> Scheduled { get; } = [];

        public int CancelCalls { get; private set; }

        public bool ScheduleSucceeds { get; set; } = true;

        public bool ScheduleThrows { get; set; }

        public bool CancelSucceeds { get; set; } = true;

        public Task<bool> ScheduleRestartAsync(int delaySeconds, CancellationToken ct)
        {
            if (ScheduleThrows)
            {
                throw new InvalidOperationException("shutdown yok");
            }

            Scheduled.Add(delaySeconds);
            return Task.FromResult(ScheduleSucceeds);
        }

        public Task<bool> CancelAsync(CancellationToken ct)
        {
            CancelCalls++;
            return Task.FromResult(CancelSucceeds);
        }
    }

    [Fact]
    public async Task Nothing_is_visible_or_applied_before_Beni_Duzelt_is_pressed()
    {
        var vm = Create();

        vm.IsPanelVisible.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
        _fix.PreviewCalls.Should().Be(0);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Start_shows_the_preview_and_waits_for_confirmation_without_changing_anything()
    {
        var vm = Create();

        await vm.StartCommand.ExecuteAsync(null);

        vm.Stage.Should().Be(FlowStage.AwaitingConfirmation);
        vm.PreviewLines.Select(l => l.Text).Should().Equal("GoodbyeDPI silinecek");
        vm.PreviewLines.Should().OnlyContain(l => !l.IsHeader);
        vm.HasChanges.Should().BeTrue();
        vm.ShowConfirmation.Should().BeTrue();
        _fix.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Apply_stays_disabled_until_the_box_is_ticked()
    {
        var vm = Create();
        await vm.StartCommand.ExecuteAsync(null);

        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
        vm.IsConfirmed = true;
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();
        vm.IsConfirmed = false;
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_runs_the_fix_shows_the_summary_and_clears_the_confirmation()
    {
        _elevation.IsElevated = true;
        var vm = Create();
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        _fix.ApplyCalls.Should().Be(1);
        vm.Stage.Should().Be(FlowStage.Finished);
        vm.Message.Should().Be("1 öğe temizlendi.");
        vm.IsConfirmed.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_second_apply_needs_a_new_preview_and_a_new_confirmation()
    {
        _elevation.IsElevated = true;
        var vm = Create();
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
        await vm.StartCommand.ExecuteAsync(null);
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
        vm.IsConfirmed = true;
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task With_nothing_to_do_apply_is_never_offered()
    {
        _fix.Preview = new FixPreview(["Temizlenecek bir şey yok."], false, HasChanges: false);
        var vm = Create();

        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;

        vm.ShowConfirmation.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
        vm.PreviewLines.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancel_closes_the_panel_and_applies_nothing()
    {
        var vm = Create();
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;

        vm.CancelCommand.Execute(null);

        vm.Stage.Should().Be(FlowStage.Idle);
        vm.IsPanelVisible.Should().BeFalse();
        vm.IsConfirmed.Should().BeFalse();
        vm.PreviewLines.Should().BeEmpty();
        _fix.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Without_admin_rights_apply_asks_for_elevation_and_changes_nothing()
    {
        _elevation.IsElevated = false;
        var vm = Create();
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        vm.ApplyLabel.Should().Be("Yönetici olarak uygula");
        _elevation.RestartCalls.Should().Be(1);
        _fix.ApplyCalls.Should().Be(0);
        vm.Message.Should().Contain("yeniden açılıyor");
    }

    [Fact]
    public async Task Declining_the_admin_prompt_leaves_everything_untouched()
    {
        _elevation.IsElevated = false;
        _elevation.RestartSucceeds = false;
        var vm = Create();
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        vm.Message.Should().Contain("Hiçbir şey değişmedi");
        vm.Stage.Should().Be(FlowStage.AwaitingConfirmation);
        _fix.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Admin_users_see_the_plain_apply_label()
    {
        _elevation.IsElevated = true;

        Create().ApplyLabel.Should().Be("Uygula");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_failing_preview_is_reported_not_thrown()
    {
        _fix.PreviewThrows = true;
        var vm = Create();

        await vm.StartCommand.ExecuteAsync(null);

        vm.Message.Should().Contain("Önizleme hazırlanamadı").And.Contain("önizleme bozuk");
        vm.Stage.Should().Be(FlowStage.Finished);
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_failing_apply_is_reported_not_thrown()
    {
        _elevation.IsElevated = true;
        _fix.ApplyThrows = true;
        var vm = Create();
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        vm.Message.Should().Contain("Uygulanırken hata çıktı").And.Contain("uygulama bozuk");
        vm.Stage.Should().Be(FlowStage.Finished);
    }

    [Fact]
    public async Task Reboot_requirement_reaches_the_ui()
    {
        _elevation.IsElevated = true;
        _fix.Preview = new FixPreview(["WinDivert silinecek"], RequiresReboot: true);
        _fix.Result = new FixResult(true, "Tamam.", RequiresReboot: true);
        var vm = Create();

        await vm.StartCommand.ExecuteAsync(null);
        vm.RequiresReboot.Should().BeTrue();
        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        vm.RequiresReboot.Should().BeTrue();
    }

    [Fact]
    public async Task Applied_changes_appear_in_the_undo_list()
    {
        _elevation.IsElevated = true;
        var journal = NewJournal();
        _fix.JournalOnApply = journal;
        var vm = Create(journal);
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        vm.UndoItems.Should().ContainSingle().Which.Text.Should().Contain("GoodbyeDPI hizmeti");
        vm.HasUndoItems.Should().BeTrue();
    }

    [Fact]
    public async Task Undo_calls_the_fix_removes_the_record_and_updates_the_list()
    {
        _elevation.IsElevated = true;
        var journal = NewJournal();
        _fix.JournalOnApply = journal;
        var vm = Create(journal);
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);
        var item = vm.UndoItems.Single();

        await vm.UndoCommand.ExecuteAsync(item);

        _fix.UndoneIds.Should().ContainSingle().Which.Should().Be(item.Record.Id);
        vm.UndoItems.Should().BeEmpty();
        vm.HasUndoItems.Should().BeFalse();
        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty();
        vm.Message.Should().Contain("Geri alındı");
    }

    [Fact]
    public async Task A_failed_undo_keeps_the_record_so_it_can_be_retried()
    {
        _elevation.IsElevated = true;
        var journal = NewJournal();
        _fix.JournalOnApply = journal;
        var vm = Create(journal);
        await vm.StartCommand.ExecuteAsync(null);
        vm.IsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);
        _fix.UndoThrows = true;

        await vm.UndoCommand.ExecuteAsync(vm.UndoItems.Single());

        vm.Message.Should().Contain("Geri alınamadı").And.Contain("geri alma bozuk");
        vm.UndoItems.Should().ContainSingle();
        (await journal.LoadAsync(CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task Undo_for_an_unknown_fix_is_reported()
    {
        var journal = NewJournal();
        var record = new UndoRecord(Guid.NewGuid(), "someone-else", DateTimeOffset.UtcNow, "x", new Dictionary<string, string>());
        await journal.AppendAsync(record, CancellationToken.None);
        var vm = Create(journal);
        await vm.RefreshUndoAsync();

        await vm.UndoCommand.ExecuteAsync(vm.UndoItems.Single());

        vm.Message.Should().Contain("geri alma işlemi bulunamadı");
        vm.UndoItems.Should().ContainSingle();
    }

    [Fact]
    public async Task Changes_from_an_earlier_session_are_loaded_at_startup()
    {
        var journal = NewJournal();
        var record = new UndoRecord(Guid.NewGuid(), FakeFix.FixIdValue, DateTimeOffset.UtcNow.AddDays(-1), "Eski değişiklik", new Dictionary<string, string>());
        await journal.AppendAsync(record, CancellationToken.None);
        var vm = Create(journal);

        await vm.RefreshUndoAsync();

        vm.UndoItems.Should().ContainSingle().Which.Text.Should().Contain("Eski değişiklik");
    }

    private sealed class FakeElevation : IElevation
    {
        public bool IsElevated { get; set; }

        public bool RestartSucceeds { get; set; } = true;

        public int RestartCalls { get; private set; }

        public bool RestartElevated(string? arguments = null)
        {
            RestartCalls++;
            return RestartSucceeds;
        }
    }

    private sealed class FakeFix : IFix
    {
        public const string FixIdValue = "fake-fix";

        public string Id { get; init; } = FixIdValue;

        public string Title { get; init; } = "Sahte";

        public bool IncludedInRepair { get; init; } = true;

        public bool RequiresAdmin { get; init; } = true;

        public FixPreview Preview { get; set; } = new(["GoodbyeDPI silinecek"]);

        public FixResult Result { get; set; } = new(true, "1 öğe temizlendi.");

        public Action? OnApply { get; set; }

        public bool PreviewThrows { get; set; }

        public bool ApplyThrows { get; set; }

        public bool UndoThrows { get; set; }

        public UndoJournal? JournalOnApply { get; set; }

        public int PreviewCalls { get; private set; }

        public int ApplyCalls { get; private set; }

        public List<Guid> UndoneIds { get; } = [];

        public Task<FixPreview> PreviewAsync(CancellationToken ct)
        {
            PreviewCalls++;
            return PreviewThrows ? throw new InvalidOperationException("önizleme bozuk") : Task.FromResult(Preview);
        }

        public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
        {
            ApplyCalls++;
            OnApply?.Invoke();
            if (ApplyThrows)
            {
                throw new InvalidOperationException("uygulama bozuk");
            }

            if (JournalOnApply is not null)
            {
                await journal.AppendAsync(
                    new UndoRecord(Guid.NewGuid(), Id, DateTimeOffset.UtcNow, "GoodbyeDPI hizmeti eski haline getirilebilir", new Dictionary<string, string>()),
                    ct);
            }

            return Result;
        }

        public Task UndoAsync(UndoRecord record, CancellationToken ct)
        {
            if (UndoThrows)
            {
                throw new InvalidOperationException("geri alma bozuk");
            }

            UndoneIds.Add(record.Id);
            return Task.CompletedTask;
        }
    }
}
