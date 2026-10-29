using FluentAssertions;
using FoxFix.Core.Dpi;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public class ScArgumentsTests
{
    [Fact]
    public void Create_passes_binPath_and_its_value_as_separate_tokens()
    {
        var config = new ServiceConfig("GoodbyeDPI", "GoodbyeDPI", @"""C:\g dpi\goodbyedpi.exe"" -5", 2, 0x10, false);

        ScArguments.Create(config).Should().Equal(
            "create", "GoodbyeDPI",
            "binPath=", @"""C:\g dpi\goodbyedpi.exe"" -5",
            "start=", "auto",
            "type=", "own",
            "DisplayName=", "GoodbyeDPI");
    }

    [Fact]
    public void Create_strips_the_nt_prefix_of_driver_paths_and_uses_kernel_type()
    {
        var config = new ServiceConfig("WinDivert", "WinDivert", @"\??\C:\g\WinDivert64.sys", 3, 1, true);

        var arguments = ScArguments.Create(config);

        arguments.Should().Contain(@"C:\g\WinDivert64.sys");
        arguments.Should().Contain("kernel");
    }

    [Fact]
    public void Create_refuses_unknown_types()
    {
        var config = new ServiceConfig("x", "x", "x.exe", 3, 0x4000, false);

        var act = () => ScArguments.Create(config);

        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(0, StopOutcome.Stopped)]
    [InlineData(1062, StopOutcome.AlreadyStopped)]
    [InlineData(1060, StopOutcome.NotFound)]
    [InlineData(5, StopOutcome.Failed)]
    public void Stop_exit_codes_are_mapped(int code, StopOutcome expected)
    {
        ScArguments.MapStop(code).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, DeleteOutcome.Deleted)]
    [InlineData(1072, DeleteOutcome.MarkedForDeletion)]
    [InlineData(1060, DeleteOutcome.NotFound)]
    [InlineData(5, DeleteOutcome.Failed)]
    public void Delete_exit_codes_are_mapped(int code, DeleteOutcome expected)
    {
        ScArguments.MapDelete(code).Should().Be(expected);
    }
}

public class DpiCleanupPolicyTests
{
    private static ResidueFinding Finding(
        string rule,
        ResidueKind kind = ResidueKind.Service,
        string name = "GoodbyeDPI",
        string? path = @"C:\g\goodbyedpi.exe",
        ResidueConfidence confidence = ResidueConfidence.Confirmed,
        ResidueHandling handling = ResidueHandling.Removable) =>
        new(rule, kind, name, path, true, confidence, handling, "m");

    [Theory]
    [InlineData("goodbyedpi")]
    [InlineData("windivert")]
    [InlineData("zapret")]
    public void Allowlisted_confirmed_findings_are_planned(string rule)
    {
        var plan = DpiCleanupPolicy.BuildPlan([Finding(rule)]);

        plan.Services.Should().ContainSingle();
        plan.Skipped.Should().BeEmpty();
    }

    [Theory]
    [InlineData("dnscrypt")]
    [InlineData("byedpi")]
    [InlineData("proxifyre")]
    [InlineData("something-else")]
    public void Everything_else_is_out_of_scope(string rule)
    {
        var plan = DpiCleanupPolicy.BuildPlan([Finding(rule)]);

        plan.IsEmpty.Should().BeTrue();
        plan.Skipped.Should().ContainSingle(s => s.Reason == SkipReason.OutOfScope);
    }

    [Fact]
    public void Report_only_and_non_service_findings_are_ignored_completely()
    {
        var plan = DpiCleanupPolicy.BuildPlan(
        [
            Finding("goodbyedpi", handling: ResidueHandling.ReportOnly),
            Finding("splitwire", ResidueKind.Folder, "SplitWire-Turkey", @"C:\Program Files\SplitWire-Turkey"),
            Finding("dns-loopback-orphan", ResidueKind.DnsLoopback, "Wi-Fi", null),
            Finding("dpi-conflict", ResidueKind.Conflict, "x", null),
        ]);

        plan.IsEmpty.Should().BeTrue();
        plan.Skipped.Should().BeEmpty();
    }

    [Fact]
    public void Possible_matches_are_skipped()
    {
        var plan = DpiCleanupPolicy.BuildPlan([Finding("windivert", confidence: ResidueConfidence.Possible)]);

        plan.IsEmpty.Should().BeTrue();
        plan.Skipped.Should().ContainSingle(s => s.Reason == SkipReason.NotSure);
    }

    [Theory]
    [InlineData(@"C:\Program Files\SplitWire-Turkey\res\x\goodbyedpi.exe")]
    [InlineData(@"C:\Users\a\AppData\Local\splitwire-turkey\winws.exe")]
    public void Anything_under_splitwire_is_left_to_splitwire(string path)
    {
        var plan = DpiCleanupPolicy.BuildPlan([Finding("goodbyedpi", path: path)]);

        plan.IsEmpty.Should().BeTrue();
        plan.Skipped.Should().ContainSingle(s => s.Reason == SkipReason.OwnedBySplitWire);
    }

    [Theory]
    [InlineData(@"evil\name")]
    [InlineData("evil/name")]
    [InlineData("ev\"il")]
    [InlineData("")]
    [InlineData("a\nb")]
    public void Unsafe_service_names_are_never_planned(string name)
    {
        var plan = DpiCleanupPolicy.BuildPlan([Finding("goodbyedpi", name: name)]);

        plan.IsEmpty.Should().BeTrue();
        plan.Skipped.Should().ContainSingle(s => s.Reason == SkipReason.UnsafeName);
    }

    [Theory]
    [InlineData("GoodbyeDPI", true)]
    [InlineData("My DPI Service", true)]
    [InlineData("WinDivert1.4", true)]
    public void Normal_names_including_spaces_are_safe(string name, bool expected)
    {
        DpiCleanupPolicy.IsSafeServiceName(name).Should().Be(expected);
    }
}

public sealed class DpiCleanupFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeMachine _machine = new();

    private UndoJournal Journal => new(Path.Combine(_dir, "undo.json"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private DpiCleanupFix CreateFix(params string[] existingDirs)
    {
        var network = new FakeNetworkInfo(Adapters.Wifi(addresses: ["192.168.1.20"], gateways: ["192.168.1.1"], dns: ["192.168.1.1"]));
        var detector = new DpiResidueDetector(_machine, _machine, new FakeDirs(existingDirs), network, [@"C:\Program Files\SplitWire-Turkey"]);
        return new DpiCleanupFix(detector, _machine, _machine, _machine, TimeSpan.FromSeconds(2), (_, _) => Task.CompletedTask);
    }

    private void SeedTypicalGoodbyeDpiInstall()
    {
        _machine.Services.Add(new ServiceEntry("GoodbyeDPI", @"""E:\gdpi\x86_64\goodbyedpi.exe"" -5", false, true));
        _machine.Services.Add(new ServiceEntry("WinDivert", @"\??\E:\gdpi\x86_64\WinDivert64.sys", true, true));
        _machine.Processes.Add(new ProcessEntry("goodbyedpi", @"E:\gdpi\x86_64\goodbyedpi.exe"));
    }

    [Fact]
    public async Task Preview_lists_the_plan_and_changes_nothing()
    {
        SeedTypicalGoodbyeDpiInstall();

        var preview = await CreateFix().PreviewAsync(CancellationToken.None);

        preview.Changes.Should().Contain(l => l.Contains("GoodbyeDPI") && l.Contains("silinecek"));
        preview.Changes.Should().Contain(l => l.Contains("WinDivert"));
        preview.Changes.Should().Contain(l => l.Contains("goodbyedpi") && l.Contains("Kapatılacak"));
        preview.RequiresReboot.Should().BeTrue();
        _machine.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_on_a_clean_machine_says_nothing_to_do()
    {
        var preview = await CreateFix().PreviewAsync(CancellationToken.None);

        preview.Changes.Should().ContainSingle().Which.Should().Contain("Temizlenecek bir şey yok");
        preview.RequiresReboot.Should().BeFalse();
        preview.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_removes_services_then_processes_then_drivers()
    {
        SeedTypicalGoodbyeDpiInstall();

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _machine.Calls.Should().Equal(
            "stop:GoodbyeDPI", "delete:GoodbyeDPI",
            "kill:goodbyedpi",
            "stop:WinDivert", "delete:WinDivert");
        _machine.Services.Should().BeEmpty();
        _machine.Processes.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_writes_the_undo_record_before_stopping_anything()
    {
        SeedTypicalGoodbyeDpiInstall();
        var journal = Journal;
        IReadOnlyList<UndoRecord>? seenAtFirstStop = null;
        _machine.OnStop = async name =>
        {
            seenAtFirstStop ??= await journal.LoadAsync(CancellationToken.None);
        };

        await CreateFix().ApplyAsync(journal, CancellationToken.None);

        seenAtFirstStop.Should().ContainSingle(r => r.Data["name"] == "GoodbyeDPI");
        var records = await journal.LoadAsync(CancellationToken.None);
        records.Should().HaveCount(2);
        records.Single(r => r.Data["name"] == "GoodbyeDPI").Data["wasRunning"].Should().Be("True");
    }

    [Fact]
    public async Task Apply_never_touches_anything_outside_the_allowlist()
    {
        SeedTypicalGoodbyeDpiInstall();
        _machine.Services.Add(new ServiceEntry("Dnscache", @"C:\Windows\system32\svchost.exe -k NetworkService -p", false, true));
        _machine.Services.Add(new ServiceEntry("monkey", @"C:\Apps\Monkey\monkey.exe", false, true));
        _machine.Services.Add(new ServiceEntry("dnscrypt-proxy", @"C:\dnscrypt\dnscrypt-proxy.exe", false, true));
        _machine.Services.Add(new ServiceEntry("ByeDPI", @"C:\byedpi\ciadpi.exe", false, true));
        _machine.Services.Add(new ServiceEntry("WinDivert14", null, true, true)); // name-only match
        _machine.Processes.Add(new ProcessEntry("notepad", null));
        _machine.Processes.Add(new ProcessEntry("ciadpi", null));

        await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        _machine.Calls.Should().NotContain(c => c.Contains("Dnscache") || c.Contains("monkey")
            || c.Contains("dnscrypt") || c.Contains("ByeDPI") || c.Contains("WinDivert14")
            || c.Contains("notepad") || c.Contains("ciadpi"));
        _machine.Services.Select(s => s.Name).Should().Contain(["Dnscache", "monkey", "dnscrypt-proxy", "ByeDPI", "WinDivert14"]);
    }

    [Fact]
    public async Task Apply_leaves_splitwire_owned_tools_alone()
    {
        _machine.Services.Add(new ServiceEntry("SWGoodbye", @"""C:\Program Files\SplitWire-Turkey\res\goodbyedpi.exe"" -5", false, true));

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _machine.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_renamed_service_is_removed_through_its_binary_path()
    {
        _machine.Services.Add(new ServiceEntry("UpdaterX", @"""C:\Users\a\Downloads\goodbyedpi\x86_64\goodbyedpi.exe"" -5", false, true));

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _machine.Calls.Should().Equal("stop:UpdaterX", "delete:UpdaterX");
    }

    [Fact]
    public async Task A_service_that_cannot_be_stopped_is_not_deleted_and_leaves_no_undo_record()
    {
        SeedTypicalGoodbyeDpiInstall();
        _machine.FailStop.Add("GoodbyeDPI");
        var journal = Journal;

        var result = await CreateFix().ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("GoodbyeDPI durdurulamadı");
        _machine.Calls.Should().NotContain("delete:GoodbyeDPI");
        (await journal.LoadAsync(CancellationToken.None)).Should().NotContain(r => r.Data["name"] == "GoodbyeDPI");
    }

    [Fact]
    public async Task A_failed_delete_removes_its_undo_record_and_is_reported()
    {
        SeedTypicalGoodbyeDpiInstall();
        _machine.FailDelete.Add("WinDivert");
        var journal = Journal;

        var result = await CreateFix().ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("WinDivert silinemedi");
        (await journal.LoadAsync(CancellationToken.None)).Should().NotContain(r => r.Data["name"] == "WinDivert");
    }

    [Fact]
    public async Task A_driver_stuck_in_stop_pending_requires_a_reboot_and_keeps_its_record()
    {
        SeedTypicalGoodbyeDpiInstall();
        _machine.Stuck.Add("WinDivert");
        var journal = Journal;

        var result = await CreateFix().ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RequiresReboot.Should().BeTrue();
        result.Summary.Should().Contain("yeniden başlat");
        (await journal.LoadAsync(CancellationToken.None)).Should().Contain(r => r.Data["name"] == "WinDivert");
    }

    [Fact]
    public async Task A_process_that_cannot_be_killed_is_reported_without_stopping_the_rest()
    {
        SeedTypicalGoodbyeDpiInstall();
        _machine.FailKill.Add("goodbyedpi");

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("goodbyedpi işlemi kapatılamadı");
        _machine.Calls.Should().Contain("delete:WinDivert");
    }

    [Fact]
    public async Task Running_twice_is_harmless()
    {
        SeedTypicalGoodbyeDpiInstall();
        var fix = CreateFix();
        await fix.ApplyAsync(Journal, CancellationToken.None);
        var callsAfterFirst = _machine.Calls.Count;

        var second = await fix.ApplyAsync(Journal, CancellationToken.None);

        second.Success.Should().BeTrue();
        _machine.Calls.Should().HaveCount(callsAfterFirst);
    }

    [Fact]
    public async Task Undo_recreates_the_service_with_its_old_settings_and_restarts_it()
    {
        SeedTypicalGoodbyeDpiInstall();
        var journal = Journal;
        var fix = CreateFix();
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single(r => r.Data["name"] == "GoodbyeDPI");

        await fix.UndoAsync(record, CancellationToken.None);

        _machine.Created.Should().ContainSingle();
        var created = _machine.Created[0];
        created.Name.Should().Be("GoodbyeDPI");
        created.BinaryPath.Should().Be(@"""E:\gdpi\x86_64\goodbyedpi.exe"" -5");
        created.IsDriver.Should().BeFalse();
        _machine.Calls.Should().Contain("start:GoodbyeDPI");
    }

    [Fact]
    public async Task Undo_does_not_start_a_service_that_was_not_running()
    {
        _machine.Services.Add(new ServiceEntry("GoodbyeDPI", @"C:\gdpi\goodbyedpi.exe", false, false));
        var journal = Journal;
        var fix = CreateFix();
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();

        await fix.UndoAsync(record, CancellationToken.None);

        _machine.Calls.Should().NotContain("start:GoodbyeDPI");
    }

    [Fact]
    public async Task Undo_reports_a_failed_recreation()
    {
        SeedTypicalGoodbyeDpiInstall();
        var journal = Journal;
        var fix = CreateFix();
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).First();
        _machine.CreateSucceeds = false;

        var act = () => fix.UndoAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*geri kurulamadı*");
    }

    [Fact]
    public async Task Undo_rejects_a_damaged_record()
    {
        var record = new UndoRecord(Guid.NewGuid(), DpiCleanupFix.FixId, DateTimeOffset.UtcNow, "x", new Dictionary<string, string>());

        var act = () => CreateFix().UndoAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bozuk*");
    }

    private sealed class FakeDirs : IPathProbe
    {
        private readonly HashSet<string> _dirs;

        public FakeDirs(string[] dirs) => _dirs = dirs.ToHashSet(StringComparer.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => _dirs.Contains(path);
    }

    /// <summary>One coherent fake Windows: services, processes and their mutation, with a call log.</summary>
    private sealed class FakeMachine : IServiceSource, IServiceManager, IProcessSource, IProcessKiller
    {
        public List<ServiceEntry> Services { get; } = [];

        public List<ProcessEntry> Processes { get; } = [];

        public List<string> Calls { get; } = [];

        public List<ServiceConfig> Created { get; } = [];

        public HashSet<string> FailStop { get; } = [];

        public HashSet<string> FailDelete { get; } = [];

        public HashSet<string> FailKill { get; } = [];

        /// <summary>Stop succeeds but the driver never leaves memory (STOP_PENDING).</summary>
        public HashSet<string> Stuck { get; } = [];

        public bool CreateSucceeds { get; set; } = true;

        public Func<string, Task>? OnStop { get; set; }

        public IReadOnlyList<ServiceEntry> GetServicesAndDrivers() => Services.ToList();

        public IReadOnlyList<ProcessEntry> GetProcesses() => Processes.ToList();

        public Task<ServiceConfig?> ReadConfigAsync(string name, CancellationToken ct)
        {
            var entry = Find(name);
            ServiceConfig? config = entry is null
                ? null
                : new ServiceConfig(entry.Name, entry.Name, entry.PathName ?? "", 3, entry.IsDriver ? 1 : 0x10, entry.IsDriver);
            return Task.FromResult(config);
        }

        public async Task<StopOutcome> StopAsync(string name, CancellationToken ct)
        {
            Calls.Add($"stop:{name}");
            if (OnStop is not null)
            {
                await OnStop(name);
            }

            var entry = Find(name);
            if (entry is null)
            {
                return StopOutcome.NotFound;
            }

            if (FailStop.Contains(name))
            {
                return StopOutcome.Failed;
            }

            if (!entry.IsRunning)
            {
                return StopOutcome.AlreadyStopped;
            }

            if (!Stuck.Contains(name))
            {
                Services[Services.IndexOf(entry)] = entry with { IsRunning = false };
            }

            return StopOutcome.Stopped;
        }

        public Task<DeleteOutcome> DeleteAsync(string name, CancellationToken ct)
        {
            Calls.Add($"delete:{name}");
            var entry = Find(name);
            if (entry is null)
            {
                return Task.FromResult(DeleteOutcome.NotFound);
            }

            if (FailDelete.Contains(name))
            {
                return Task.FromResult(DeleteOutcome.Failed);
            }

            if (Stuck.Contains(name))
            {
                return Task.FromResult(DeleteOutcome.MarkedForDeletion);
            }

            Services.Remove(entry);
            return Task.FromResult(DeleteOutcome.Deleted);
        }

        public Task<bool> CreateAsync(ServiceConfig config, CancellationToken ct)
        {
            Calls.Add($"create:{config.Name}");
            if (CreateSucceeds)
            {
                Created.Add(config);
            }

            return Task.FromResult(CreateSucceeds);
        }

        public Task<bool> StartAsync(string name, CancellationToken ct)
        {
            Calls.Add($"start:{name}");
            return Task.FromResult(true);
        }

        public Task<int> KillAsync(string processName, CancellationToken ct)
        {
            Calls.Add($"kill:{processName}");
            if (FailKill.Contains(processName))
            {
                throw new InvalidOperationException("Erişim engellendi");
            }

            return Task.FromResult(Processes.RemoveAll(p => p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase)));
        }

        private ServiceEntry? Find(string name) =>
            Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
