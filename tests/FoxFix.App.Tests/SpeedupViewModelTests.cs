using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.Cleanup;
using FoxFix.Core.Startup;
using Xunit;

namespace FoxFix.App.Tests;

internal static class SpeedupFakes
{
    public sealed class StartupStore : IStartupStore
    {
        public List<StartupEntry> Entries { get; } = [];

        public bool Throws { get; set; }

        public int ListCalls { get; private set; }

        public IReadOnlyList<StartupEntry> List()
        {
            ListCalls++;
            return Throws ? throw new IOException("kayıt defteri okunamadı") : Entries.ToList();
        }

        public string? ReadApproval(StartupEntry entry) => null;

        public void WriteApproval(StartupEntry entry, string? rawBase64)
        {
        }
    }

    public sealed class Target : ICleanupTarget
    {
        public Target(string id, long bytes)
        {
            Id = id;
            Bytes = bytes;
        }

        public string Id { get; }

        public string Title => "Başlık " + Id;

        public string Description => "açıklama";

        public bool NeedsAdmin => false;

        public long Bytes { get; }

        public bool Throws { get; set; }

        public int Measured { get; private set; }

        public long MeasureBytes(CancellationToken ct)
        {
            Measured++;
            return Throws ? throw new UnauthorizedAccessException("erişim yok") : Bytes;
        }

        public CleanupOutcome Clean(CancellationToken ct) => new(Bytes, 0);
    }
}

public class StartupViewModelTests
{
    private static StartupEntry Entry(string name, bool enabled = true) =>
        new(name, $@"C:\Apps\{name}.exe", StartupSource.CurrentUserRun, enabled);

    private static (StartupViewModel Vm, SpeedupFakes.StartupStore Store, List<IReadOnlyDictionary<string, bool>> Desired, int[] Flows) Create(
        params StartupEntry[] entries)
    {
        var store = new SpeedupFakes.StartupStore();
        store.Entries.AddRange(entries);
        var desired = new List<IReadOnlyDictionary<string, bool>>();
        var flows = new[] { 0 };
        var vm = new StartupViewModel(store, desired.Add, () =>
        {
            flows[0]++;
            return Task.CompletedTask;
        });
        return (vm, store, desired, flows);
    }

    [Fact]
    public async Task Refresh_lists_the_programs_with_their_current_state()
    {
        var (vm, _, _, _) = Create(Entry("Discord"), Entry("Steam", enabled: false));

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Select(i => (i.Name, i.IsEnabled)).Should().Equal(("Discord", true), ("Steam", false));
        vm.IsEmpty.Should().BeFalse();
        vm.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_list_is_recognised()
    {
        var (vm, _, _, _) = Create();

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_is_disabled_until_a_checkbox_is_changed()
    {
        var (vm, _, _, _) = Create(Entry("Discord"));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.ApplyCommand.CanExecute(null).Should().BeFalse();

        vm.Items[0].IsEnabled = false;
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();
        vm.HasChanges.Should().BeTrue();

        vm.Items[0].IsEnabled = true;
        vm.ApplyCommand.CanExecute(null).Should().BeFalse("changing it back is no change");
    }

    [Fact]
    public async Task Apply_hands_over_only_the_changed_programs_and_starts_the_confirm_flow()
    {
        var (vm, _, desired, flows) = Create(Entry("Discord"), Entry("Steam"), Entry("Spotify", enabled: false));
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Items[0].IsEnabled = false;
        vm.Items[2].IsEnabled = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        desired.Should().ContainSingle();
        desired[0].Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            [Entry("Discord").Id] = false,
            [Entry("Spotify").Id] = true,
        });
        flows[0].Should().Be(1);
    }

    [Fact]
    public async Task Reloading_forgets_unapplied_changes()
    {
        var (vm, _, _, _) = Create(Entry("Discord"));
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Items[0].IsEnabled = false;

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.HasChanges.Should().BeFalse();
        vm.Items[0].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_list_that_cannot_be_read_says_so_instead_of_crashing()
    {
        var (vm, store, _, _) = Create();
        store.Throws = true;

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.HasLoadError.Should().BeTrue();
        vm.LoadError.Should().Contain("Liste okunamadı").And.Contain("kayıt defteri okunamadı");
    }

    [Fact]
    public async Task Machine_wide_programs_are_marked_as_needing_administrator_rights()
    {
        var (vm, _, _, _) = Create(new StartupEntry("Updater", @"C:\x.exe", StartupSource.MachineRun, true), Entry("Mine"));

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Single(i => i.Name == "Updater").NeedsAdmin.Should().BeTrue();
        vm.Items.Single(i => i.Name == "Mine").NeedsAdmin.Should().BeFalse();
    }
}

public class CleanupViewModelTests
{
    private static (CleanupViewModel Vm, List<IEnumerable<string>> Selected, int[] Flows) Create(params ICleanupTarget[] targets)
    {
        var selected = new List<IEnumerable<string>>();
        var flows = new[] { 0 };
        var vm = new CleanupViewModel(targets, selected.Add, () =>
        {
            flows[0]++;
            return Task.CompletedTask;
        });
        return (vm, selected, flows);
    }

    [Fact]
    public void Every_target_is_listed_and_nothing_is_selected_at_first()
    {
        var (vm, _, _) = Create(new SpeedupFakes.Target("a", 1), new SpeedupFakes.Target("b", 2));

        vm.Items.Should().HaveCount(2);
        vm.Items.Should().OnlyContain(i => !i.IsSelected);
        vm.CleanCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Scanning_fills_in_each_size_in_readable_units()
    {
        var (vm, _, _) = Create(new SpeedupFakes.Target("a", 5L * 1024 * 1024), new SpeedupFakes.Target("b", 2048));

        await vm.ScanCommand.ExecuteAsync(null);

        vm.Items.Select(i => i.SizeText).Should().Equal("5 MB", "2 KB");
        vm.IsScanning.Should().BeFalse();
    }

    [Fact]
    public async Task A_target_that_cannot_be_measured_shows_a_question_mark_and_the_rest_continue()
    {
        var (vm, _, _) = Create(new SpeedupFakes.Target("bad", 1) { Throws = true }, new SpeedupFakes.Target("ok", 2048));

        await vm.ScanCommand.ExecuteAsync(null);

        vm.Items.Select(i => i.SizeText).Should().Equal("?", "2 KB");
    }

    [Fact]
    public async Task Selecting_something_enables_cleaning_and_the_total_counts_only_selected_items()
    {
        var (vm, _, _) = Create(new SpeedupFakes.Target("a", 1024 * 1024), new SpeedupFakes.Target("b", 3L * 1024 * 1024));
        await vm.ScanCommand.ExecuteAsync(null);

        vm.Items[1].IsSelected = true;

        vm.CleanCommand.CanExecute(null).Should().BeTrue();
        vm.SelectedTotalText.Should().Be("3 MB");

        vm.Items[0].IsSelected = true;
        vm.SelectedTotalText.Should().Be("4 MB");
    }

    [Fact]
    public async Task Cleaning_sends_only_the_selected_ids_and_starts_the_confirm_flow()
    {
        var (vm, selected, flows) = Create(
            new SpeedupFakes.Target("user-temp", 10), new SpeedupFakes.Target("windows-temp", 20), new SpeedupFakes.Target("recycle-bin", 30));
        await vm.ScanCommand.ExecuteAsync(null);
        vm.Items[0].IsSelected = true;
        vm.Items[2].IsSelected = true;

        await vm.CleanCommand.ExecuteAsync(null);

        selected.Should().ContainSingle().Which.Should().Equal("user-temp", "recycle-bin");
        flows[0].Should().Be(1);
    }

    [Fact]
    public async Task Cleaning_is_not_offered_while_a_scan_is_running()
    {
        var gate = new TaskCompletionSource();
        var slow = new SlowTarget(gate);
        var (vm, _, _) = Create(slow);
        vm.Items[0].IsSelected = true;

        var scan = vm.ScanCommand.ExecuteAsync(null);
        await slow.Started.Task;

        vm.IsScanning.Should().BeTrue();
        vm.CleanCommand.CanExecute(null).Should().BeFalse();

        gate.SetResult();
        await scan;
        vm.CleanCommand.CanExecute(null).Should().BeTrue();
    }

    private sealed class SlowTarget : ICleanupTarget
    {
        private readonly TaskCompletionSource _gate;

        public SlowTarget(TaskCompletionSource gate) => _gate = gate;

        public TaskCompletionSource Started { get; } = new();

        public string Id => "slow";

        public string Title => "Yavaş";

        public string Description => "";

        public bool NeedsAdmin => false;

        public long MeasureBytes(CancellationToken ct)
        {
            Started.TrySetResult();
            _gate.Task.GetAwaiter().GetResult();
            return 1024;
        }

        public CleanupOutcome Clean(CancellationToken ct) => new(0, 0);
    }
}
