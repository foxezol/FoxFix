using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.Cleanup;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;
using FoxFix.Core.Startup;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class RepairPickerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-picker-" + Guid.NewGuid().ToString("N"));

    public RepairPickerTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Elevated : IElevation
    {
        public bool IsElevated => true;

        public bool RestartElevated(string? arguments = null) => true;
    }

    private sealed class NoReboot : IRebootService
    {
        public Task<bool> ScheduleRestartAsync(int delaySeconds, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> CancelAsync(CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class RecordingFix(string id, bool includedInRepair = true) : IFix
    {
        public string Id => id;

        public string Title => "Başlık " + id;

        public bool IncludedInRepair => includedInRepair;

        public Task<FixPreview> PreviewAsync(CancellationToken ct) => Task.FromResult(new FixPreview(["değişiklik " + id]));

        public Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct) => Task.FromResult(new FixResult(true, "ok"));

        public Task UndoAsync(UndoRecord record, CancellationToken ct) => Task.CompletedTask;
    }

    private (RepairPickerViewModel Picker, FixFlowViewModel Flow, StartupViewModel Startup, CleanupViewModel Cleanup, List<IReadOnlyDictionary<string, bool>> Desired, List<IEnumerable<string>> Selected) Create()
    {
        var flow = new FixFlowViewModel(
            [
                new RecordingFix(DpiCleanupFix.FixId),
                new RecordingFix(DnsRepairFix.FixId),
                new RecordingFix(StartupFix.FixIdValue, includedInRepair: false),
                new RecordingFix(DiskCleanupFix.FixIdValue, includedInRepair: false),
            ],
            new UndoJournal(Path.Combine(_dir, "undo.json")), new Elevated(), new NoReboot());

        var store = new SpeedupFakes.StartupStore();
        store.Entries.Add(new StartupEntry("Discord", @"C:\Apps\Discord.exe", StartupSource.CurrentUserRun, true));
        var desired = new List<IReadOnlyDictionary<string, bool>>();
        var startup = new StartupViewModel(store, desired.Add, () => Task.CompletedTask);

        var selected = new List<IEnumerable<string>>();
        var cleanup = new CleanupViewModel([new SpeedupFakes.Target("user-temp", 100)], selected.Add, () => Task.CompletedTask);

        return (new RepairPickerViewModel(startup, cleanup, flow), flow, startup, cleanup, desired, selected);
    }

    [Fact]
    public async Task By_default_only_the_two_internet_jobs_are_ticked_and_it_starts_closed()
    {
        var (picker, flow, _, _, _, _) = Create();

        picker.IsOpen.Should().BeFalse();
        picker.IncludeDpi.Should().BeTrue();
        picker.IncludeDns.Should().BeTrue();

        picker.OpenCommand.Execute(null);
        await picker.ContinueCommand.ExecuteAsync(null);

        picker.IsOpen.Should().BeFalse("continuing closes the window");
        flow.PreviewLines.Select(l => l.Text).Should().Contain(["Başlık dpi-cleanup", "değişiklik dpi-cleanup", "Başlık dns-repair", "değişiklik dns-repair"]);
        flow.PreviewLines.Select(l => l.Text).Should().NotContain(t => t.Contains("startup") || t.Contains("disk-cleanup"));
    }

    [Fact]
    public async Task Startup_changes_and_cleanup_choices_are_added_to_the_same_preview()
    {
        var (picker, flow, startup, cleanup, desired, selected) = Create();
        await startup.RefreshCommand.ExecuteAsync(null);
        startup.Items[0].IsEnabled = false;
        cleanup.Items[0].IsSelected = true;

        picker.OpenCommand.Execute(null);
        picker.IncludeStartup.Should().BeTrue("a change was already chosen on the speed-up page");
        await picker.ContinueCommand.ExecuteAsync(null);

        desired.Should().ContainSingle().Which.Should().Contain(new KeyValuePair<string, bool>("CurrentUserRun|Discord", false));
        selected.Should().ContainSingle().Which.Should().Equal("user-temp");
        flow.PreviewLines.Select(l => l.Text).Should().Contain(["Başlık startup", "Başlık disk-cleanup"]);
    }

    [Fact]
    public async Task Unticked_jobs_are_left_out_and_startup_is_not_staged()
    {
        var (picker, flow, startup, _, desired, _) = Create();
        await startup.RefreshCommand.ExecuteAsync(null);
        startup.Items[0].IsEnabled = false;

        picker.OpenCommand.Execute(null);
        picker.IncludeDpi = false;
        picker.IncludeStartup = false;
        await picker.ContinueCommand.ExecuteAsync(null);

        desired.Should().BeEmpty();
        flow.PreviewLines.Select(l => l.Text).Should().Contain("değişiklik dns-repair").And.NotContain("değişiklik dpi-cleanup");
    }

    [Fact]
    public void Continue_is_disabled_when_nothing_is_ticked()
    {
        var (picker, _, _, _, _, _) = Create();

        picker.IncludeDpi = false;
        picker.IncludeDns = false;

        picker.ContinueCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Startup_is_only_offered_when_the_speed_up_page_has_changes()
    {
        var (picker, _, startup, _, _, _) = Create();
        await startup.RefreshCommand.ExecuteAsync(null);

        picker.StartupAvailable.Should().BeFalse();

        startup.Items[0].IsEnabled = false;

        picker.StartupAvailable.Should().BeTrue();
        picker.StartupText.Should().Contain("1 değişiklik");
    }

    [Fact]
    public void Cancel_closes_without_starting_anything()
    {
        var (picker, flow, _, _, _, _) = Create();

        picker.OpenCommand.Execute(null);
        picker.CloseCommand.Execute(null);

        picker.IsOpen.Should().BeFalse();
        flow.Stage.Should().Be(FlowStage.Idle);
    }
}

public class StartupGroupingTests
{
    [Fact]
    public async Task Programs_are_split_by_their_state_on_disk_and_do_not_jump_when_ticked()
    {
        var store = new SpeedupFakes.StartupStore();
        store.Entries.Add(new StartupEntry("Discord", @"C:\a.exe", StartupSource.CurrentUserRun, true));
        store.Entries.Add(new StartupEntry("Steam", @"C:\b.exe", StartupSource.CurrentUserRun, false));
        var vm = new StartupViewModel(store, _ => { }, () => Task.CompletedTask);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.EnabledItems.Select(i => i.Name).Should().Equal("Discord");
        vm.DisabledItems.Select(i => i.Name).Should().Equal("Steam");
        vm.EnabledHeader.Should().Be("Açık olanlar (1)");
        vm.DisabledHeader.Should().Be("Kapalı olanlar (1)");

        vm.EnabledItems[0].IsEnabled = false;

        vm.EnabledItems.Should().HaveCount(1, "a tick only marks a change; the row moves after it is applied");
    }

    [Fact]
    public async Task Icons_come_from_the_loader_and_a_failing_loader_only_costs_the_icon()
    {
        var store = new SpeedupFakes.StartupStore();
        store.Entries.Add(new StartupEntry("Discord", @"C:\a.exe", StartupSource.CurrentUserRun, true));
        store.Entries.Add(new StartupEntry("Broken", @"C:\b.exe", StartupSource.CurrentUserRun, true));
        var vm = new StartupViewModel(
            store, _ => { }, () => Task.CompletedTask,
            e => e.Name == "Broken" ? throw new IOException("dosya yok") : new System.Windows.Media.Imaging.WriteableBitmap(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null));

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Single(i => i.Name == "Discord").HasIcon.Should().BeTrue();
        var broken = vm.Items.Single(i => i.Name == "Broken");
        broken.HasIcon.Should().BeFalse();
        broken.Initial.Should().Be("B");
        vm.LoadError.Should().BeEmpty();
    }
}
