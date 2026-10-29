using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using FoxFix.Core.Dpi;
using FoxFix.Core.Fixes;
using FoxFix.Core.GoodbyeDpi;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class DpiResetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "foxfix-reset-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;
    private readonly Payload _payload = new();
    private readonly Launcher _launcher = new();
    private readonly Services _services = new();
    private readonly Machine _machine = new();

    public DpiResetTests()
    {
        Directory.CreateDirectory(_root);
        _folder = Path.Combine(_root, "GoodbyeDPI");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ---------- fakes ----------

    private sealed class Payload : IGoodbyeDpiPayload
    {
        private static readonly string[] Names = ["goodbyedpi.exe", "WinDivert.dll", "WinDivert64.sys"];

        public Dictionary<string, string> Hashes => Names.ToDictionary(n => n, n => Convert.ToHexString(SHA256.HashData(Content(n))));

        public void WriteTo(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach (var name in Names)
            {
                File.WriteAllBytes(Path.Combine(folder, name), Content(name));
            }
        }

        private static byte[] Content(string name) => Encoding.UTF8.GetBytes("x:" + name);
    }

    private sealed class FakeProcess(Launcher owner, bool exited) : IGoodbyeDpiProcess
    {
        public bool Stopped { get; private set; }

        public bool HasExited => exited || Stopped;

        public Task StopAsync()
        {
            Stopped = true;
            owner.Running = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class Launcher : IGoodbyeDpiLauncher
    {
        public bool Running { get; set; }

        public int StopCalls { get; private set; }

        public bool ExitImmediately { get; set; }

        public bool FailStart { get; set; }

        public List<string> Started { get; } = [];

        public FakeProcess? Last { get; private set; }

        public IGoodbyeDpiProcess Start(string exePath, string arguments)
        {
            if (FailStart)
            {
                throw new System.ComponentModel.Win32Exception("erişim reddedildi");
            }

            Started.Add(arguments);
            Running = !ExitImmediately;
            Last = new FakeProcess(this, ExitImmediately);
            return Last;
        }

        public Task<int> StopAllInFolderAsync(string folder, CancellationToken ct)
        {
            StopCalls++;
            Running = false;
            return Task.FromResult(0);
        }

        public bool IsRunningFromFolder(string folder) => Running;
    }

    private sealed class Services : IServiceManager
    {
        public Dictionary<string, ServiceConfig> Configs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ServiceConfig?> ReadConfigAsync(string name, CancellationToken ct) => Task.FromResult(Configs.GetValueOrDefault(name));

        public Task<StopOutcome> StopAsync(string name, CancellationToken ct) => Task.FromResult(StopOutcome.Stopped);

        public Task<DeleteOutcome> DeleteAsync(string name, CancellationToken ct)
        {
            Configs.Remove(name);
            return Task.FromResult(DeleteOutcome.Deleted);
        }

        public Task<bool> CreateAsync(ServiceConfig config, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> StartAsync(string name, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Machine : IServiceSource, IProcessSource, IPathProbe, INetworkInfoProvider, IDnsConfigurator
    {
        private readonly Dictionary<string, (List<string> V4, List<string> V6, string Dns)> _adapters = [];

        public List<ServiceEntry> ServiceList { get; } = [];

        public List<string> Calls { get; } = [];

        public void AddAdapter(string name, string[]? v4 = null, string[]? v6 = null) =>
            _adapters[name] = ((v4 ?? []).ToList(), (v6 ?? []).ToList(), (v4 ?? v6)?.FirstOrDefault() ?? "192.168.1.1");

        public IReadOnlyList<ServiceEntry> GetServicesAndDrivers() => ServiceList;

        public List<ProcessEntry> ProcessList { get; } = [];

        public IReadOnlyList<ProcessEntry> GetProcesses() => ProcessList;

        public bool DirectoryExists(string path) => false;

        public IReadOnlyList<AdapterInfo> GetAdapters() => _adapters.Select(a => new AdapterInfo(
            a.Key, "Intel(R) Ethernet Connection", NetworkInterfaceType.Ethernet, OperationalStatus.Up,
            [IPAddress.Parse("192.168.1.20")], [IPAddress.Parse("192.168.1.1")], [IPAddress.Parse(a.Value.Dns)])).ToList();

        public Task<DnsSnapshot?> ReadAsync(AdapterInfo adapter, CancellationToken ct) =>
            Task.FromResult<DnsSnapshot?>(new DnsSnapshot(adapter.Name, _adapters[adapter.Name].V4.ToList(), _adapters[adapter.Name].V6.ToList()));

        public Task<bool> SetAutomaticAsync(string adapterName, CancellationToken ct)
        {
            Calls.Add("auto:" + adapterName);
            _adapters[adapterName] = ([], [], "192.168.1.1");
            return Task.FromResult(true);
        }

        public Task<bool> RestoreAsync(DnsSnapshot snapshot, CancellationToken ct)
        {
            Calls.Add("restore:" + snapshot.AdapterName);
            _adapters[snapshot.AdapterName] = (snapshot.V4Servers.ToList(), snapshot.V6Servers.ToList(), snapshot.V4Servers.FirstOrDefault() ?? "192.168.1.1");
            return Task.FromResult(true);
        }

        public Task<bool> FlushCacheAsync(CancellationToken ct)
        {
            Calls.Add("flush");
            return Task.FromResult(true);
        }

        public Task<bool> SetStaticAsync(string adapterName, IReadOnlyList<string> v4Servers, IReadOnlyList<string> v6Servers, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private UndoJournal NewJournal() => new(Path.Combine(_root, "undo.json"));

    private GoodbyeDpiStatusReader NewStatus() =>
        new(_machine, _launcher, new DpiResidueDetector(_machine, _machine, _machine, _machine, [], _root), _folder);

    // ---------- DNS reset ----------

    [Fact]
    public async Task Hand_typed_DNS_on_any_connection_goes_back_to_automatic_and_is_saved_for_undo()
    {
        _machine.AddAdapter("Ethernet", v4: ["77.88.8.8", "1.1.1.1"]);
        _machine.AddAdapter("Wi-Fi");
        var fix = new DnsResetFix(_machine, _machine, _machine);
        var journal = NewJournal();

        var preview = await fix.PreviewAsync(CancellationToken.None);
        preview.Changes.Should().Contain(l => l.Contains("Ethernet") && l.Contains("77.88.8.8"));
        preview.Changes.Should().NotContain(l => l.Contains("Wi-Fi"), "automatic already is the Windows default");

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _machine.Calls.Should().Equal("auto:Ethernet", "flush");
        var record = (await journal.LoadAsync(CancellationToken.None)).Should().ContainSingle(r => r.FixId == DnsResetFix.FixIdValue).Subject;

        await fix.UndoAsync(record, CancellationToken.None);
        _machine.Calls.Should().Contain("restore:Ethernet");
    }

    [Fact]
    public async Task When_everything_is_already_automatic_there_is_nothing_to_do()
    {
        _machine.AddAdapter("Ethernet");

        var preview = await new DnsResetFix(_machine, _machine, _machine).PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        preview.Changes.Should().Equal(Strings.Get(Strings.DnsResetNothing));
    }

    [Fact]
    public async Task A_running_DnsCrypt_proxy_keeps_its_own_loopback_DNS()
    {
        _machine.AddAdapter("Ethernet", v4: ["127.0.0.1"]);
        _machine.AddAdapter("Wi-Fi", v4: ["8.8.8.8"]);
        _machine.ServiceList.Add(new ServiceEntry("dnscrypt-proxy", @"""C:\dnscrypt\dnscrypt-proxy.exe""", IsDriver: false, IsRunning: true));

        var preview = await new DnsResetFix(_machine, _machine, _machine).PreviewAsync(CancellationToken.None);

        preview.Changes.Should().Contain(l => l.Contains("Wi-Fi"));
        preview.Changes.Should().NotContain(l => l.Contains("Ethernet"));
    }

    // ---------- remove FoxFix's own GoodbyeDPI ----------

    [Fact]
    public async Task Removing_takes_the_service_and_files_away_and_clears_the_install_entries_in_the_journal()
    {
        GoodbyeDpiFiles.Prepare(_payload, _folder, _payload.Hashes);
        new GoodbyeDpiState("turkey-1").Write(_folder);
        _services.Configs[GoodbyeDpiLayout.ServiceName] =
            new ServiceConfig(GoodbyeDpiLayout.ServiceName, "x", Path.Combine(_folder, "goodbyedpi.exe"), 2, 0x10, false);
        var journal = NewJournal();
        await journal.AppendAsync(new UndoRecord(Guid.NewGuid(), GoodbyeDpiInstallFix.FixIdValue, DateTimeOffset.UtcNow, "kurulum", new Dictionary<string, string>()), CancellationToken.None);
        await journal.AppendAsync(new UndoRecord(Guid.NewGuid(), "dns-reset", DateTimeOffset.UtcNow, "dns", new Dictionary<string, string>()), CancellationToken.None);
        var fix = new GoodbyeDpiRemoveFix(NewStatus(), new GoodbyeDpiTeardown(_services, _launcher, _folder), _folder);

        (await fix.PreviewAsync(CancellationToken.None)).HasChanges.Should().BeTrue();
        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _services.Configs.Should().BeEmpty();
        Directory.Exists(_folder).Should().BeFalse();
        (await journal.LoadAsync(CancellationToken.None)).Select(r => r.FixId).Should().Equal("dns-reset");
    }

    [Fact]
    public async Task Removing_when_nothing_is_installed_is_a_no_op()
    {
        var fix = new GoodbyeDpiRemoveFix(NewStatus(), new GoodbyeDpiTeardown(_services, _launcher, _folder), _folder);

        var preview = await fix.PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        preview.Changes.Should().Equal(Strings.Get(Strings.GdpiRemoveNothing));
    }

    // ---------- FoxDPI session ----------

    private FoxDpiSession NewSession() =>
        new(_payload, _launcher, NewStatus(), _folder, (_, _) => Task.CompletedTask, _payload.Hashes);

    [Fact]
    public async Task Starting_writes_the_files_runs_the_chosen_setting_hidden_and_remembers_it()
    {
        var session = NewSession();

        var result = await session.StartAsync(GoodbyeDpiProfiles.Find("turkey-2")!, CancellationToken.None);

        result.Started.Should().BeTrue();
        session.IsRunning.Should().BeTrue();
        session.Profile!.Id.Should().Be("turkey-2");
        _launcher.Started.Should().Equal("-5");
        GoodbyeDpiFiles.FindProblems(_folder, _payload.Hashes).Should().BeEmpty();
        GoodbyeDpiState.Read(_folder).Should().Be(new GoodbyeDpiState("turkey-2"));
    }

    [Fact]
    public async Task Stopping_ends_GoodbyeDPI_and_can_be_repeated()
    {
        var session = NewSession();
        await session.StartAsync(GoodbyeDpiProfiles.All[0], CancellationToken.None);
        var process = _launcher.Last!;

        await session.StopAsync();
        await session.StopAsync();

        process.Stopped.Should().BeTrue();
        session.IsRunning.Should().BeFalse();
        session.Profile.Should().BeNull();
    }

    [Fact]
    public async Task It_does_not_start_next_to_someone_elses_GoodbyeDPI_or_a_second_time()
    {
        _machine.ServiceList.Add(new ServiceEntry("GoodbyeDPI", @"""C:\GoodbyeDPI\goodbyedpi.exe"" -5", IsDriver: false, IsRunning: true));
        var session = NewSession();

        var blocked = await session.StartAsync(GoodbyeDpiProfiles.All[0], CancellationToken.None);

        blocked.Started.Should().BeFalse();
        blocked.Message.Should().Be(Strings.Get(Strings.GdpiBlockedForeign));
        _launcher.Started.Should().BeEmpty();

        _machine.ServiceList.Clear();
        (await session.StartAsync(GoodbyeDpiProfiles.All[0], CancellationToken.None)).Started.Should().BeTrue();
        var again = await session.StartAsync(GoodbyeDpiProfiles.All[0], CancellationToken.None);

        again.Message.Should().Be(Strings.Get(Strings.FoxDpiAlreadyRunning));
        _launcher.Started.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_program_that_exits_at_once_is_reported_as_not_started()
    {
        _launcher.ExitImmediately = true;
        var session = NewSession();

        var result = await session.StartAsync(GoodbyeDpiProfiles.All[0], CancellationToken.None);

        result.Started.Should().BeFalse();
        result.Message.Should().Be(Strings.Get(Strings.FoxDpiNotRunning));
        session.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task A_program_that_cannot_be_started_is_reported()
    {
        _launcher.FailStart = true;

        var result = await NewSession().StartAsync(GoodbyeDpiProfiles.All[0], CancellationToken.None);

        result.Started.Should().BeFalse();
        result.Message.Should().Contain("erişim reddedildi");
    }

    // ---------- a loaded WinDivert driver is not "someone else's GoodbyeDPI" ----------

    [Fact]
    public void A_WinDivert_driver_registered_from_an_older_install_is_not_a_leftover_while_FoxDPI_runs()
    {
        _machine.ServiceList.Add(new ServiceEntry("WinDivert", @"C:\Old\GoodbyeDPI\x86_64\WinDivert64.sys", IsDriver: true, IsRunning: true));
        var detector = new DpiResidueDetector(_machine, _machine, _machine, _machine, [], _root);

        detector.Scan().Should().Contain(f => f.Kind == ResidueKind.Driver, "with nothing of ours running it is a leftover");

        _machine.ProcessList.Add(new ProcessEntry("goodbyedpi", Path.Combine(_folder, "goodbyedpi.exe")));

        detector.Scan().Should().BeEmpty("FoxDPI is using that driver");
    }

    [Fact]
    public void A_loaded_driver_alone_does_not_count_as_another_running_tool()
    {
        _machine.ServiceList.Add(new ServiceEntry("WinDivert", @"C:\Old\GoodbyeDPI\x86_64\WinDivert64.sys", IsDriver: true, IsRunning: true));

        NewStatus().Read().OtherRunning.Should().BeFalse();

        _machine.ProcessList.Add(new ProcessEntry("goodbyedpi", @"C:\Old\GoodbyeDPI\x86_64\goodbyedpi.exe"));

        NewStatus().Read().OtherRunning.Should().BeTrue("a program of someone else is running");
    }
}