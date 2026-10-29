using System.Net;
using System.Net.NetworkInformation;
using FluentAssertions;
using FoxFix.Core.Dpi;
using FoxFix.Core.Fixes;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public class NetshArgumentsTests
{
    [Fact]
    public void Automatic_dns_passes_the_adapter_name_as_one_token()
    {
        NetshArguments.SetAutomaticV4("Ethernet 3").Should().Equal("interface", "ip", "set", "dns", "Ethernet 3", "dhcp");
        NetshArguments.SetAutomaticV6("Ethernet 3").Should().Equal("interface", "ipv6", "set", "dnsservers", "Ethernet 3", "dhcp");
    }

    [Fact]
    public void A_hostile_adapter_name_stays_a_single_argument()
    {
        var arguments = NetshArguments.SetAutomaticV4("Wi-Fi\" & calc & \"");

        arguments.Should().HaveCount(6);
        arguments[4].Should().Be("Wi-Fi\" & calc & \"");
    }

    [Fact]
    public void Static_servers_use_set_for_the_first_and_add_with_an_index_for_the_rest()
    {
        NetshArguments.SetStatic(false, "Wi-Fi", "1.1.1.1").Should().Equal("interface", "ip", "set", "dns", "Wi-Fi", "static", "1.1.1.1");
        NetshArguments.AddStatic(false, "Wi-Fi", "1.0.0.1", 2).Should().Equal("interface", "ip", "add", "dns", "Wi-Fi", "1.0.0.1", "index=2");
        NetshArguments.SetStatic(true, "Wi-Fi", "::1").Should().Contain("ipv6").And.Contain("dnsservers");
    }

    [Fact]
    public void Flush_uses_ipconfig_flushdns()
    {
        NetshArguments.FlushCache().Should().Equal("/flushdns");
    }
}

public class GoodbyeDpiRedirectTests
{
    [Theory]
    [InlineData(@"""C:\g\goodbyedpi.exe"" -5 --set-ttl 5 --dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253", "77.88.8.8", 1253)]
    [InlineData(@"C:\g\goodbyedpi.exe --dns-addr 8.8.8.8", "8.8.8.8", 53)]
    [InlineData(@"C:\g\goodbyedpi.exe --dns-addr=1.1.1.1 --dns-port=5353", "1.1.1.1", 5353)]
    [InlineData(@"C:\g\goodbyedpi.exe --DNS-ADDR 9.9.9.9", "9.9.9.9", 53)]
    [InlineData(@"C:\g\goodbyedpi.exe --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253", "2a02:6b8::feed:0ff", 1253)]
    public void Finds_the_redirect_target(string commandLine, string address, int port)
    {
        GoodbyeDpiArguments.TryGetDnsRedirect(commandLine, out var redirect).Should().BeTrue();
        redirect.Address.Should().Be(address);
        redirect.Port.Should().Be(port);
    }

    [Theory]
    [InlineData(@"""C:\g\goodbyedpi.exe"" -5")]
    [InlineData(@"C:\g\goodbyedpi.exe -5 --set-ttl 3")]
    [InlineData("")]
    [InlineData(null)]
    public void No_redirect_without_the_switch(string? commandLine)
    {
        GoodbyeDpiArguments.TryGetDnsRedirect(commandLine, out _).Should().BeFalse();
    }

    [Fact]
    public void An_out_of_range_port_falls_back_to_53()
    {
        GoodbyeDpiArguments.TryGetDnsRedirect(@"g.exe --dns-addr 1.1.1.1 --dns-port 99999", out var redirect).Should().BeTrue();
        redirect.Port.Should().Be(53);
    }

    private sealed class Services : IServiceSource
    {
        private readonly ServiceEntry[] _entries;

        public Services(params ServiceEntry[] entries) => _entries = entries;

        public IReadOnlyList<ServiceEntry> GetServicesAndDrivers() => _entries;
    }

    private sealed class NoProcesses : IProcessSource
    {
        public IReadOnlyList<ProcessEntry> GetProcesses() => [];
    }

    private sealed class NoPaths : IPathProbe
    {
        public bool DirectoryExists(string path) => false;
    }

    private static IReadOnlyList<ResidueFinding> Scan(string pathName, bool running = true)
    {
        var detector = new DpiResidueDetector(
            new Services(new ServiceEntry("GoodbyeDPI", pathName, false, running)),
            new NoProcesses(), new NoPaths(), new FakeNetworkInfo(Adapters.Healthy()), []);
        return detector.Scan();
    }

    [Fact]
    public void The_detector_reports_the_redirect_target_to_the_user()
    {
        var findings = Scan(@"""C:\g\goodbyedpi.exe"" -5 --dns-addr 77.88.8.8 --dns-port 1253");

        var redirect = findings.Should().ContainSingle(f => f.Kind == ResidueKind.DnsRedirect).Subject;
        redirect.Handling.Should().Be(ResidueHandling.ReportOnly);
        redirect.IsActive.Should().BeTrue();
        redirect.Message.Should().Contain("77.88.8.8").And.Contain("1253");
        findings.Should().Contain(f => f.Kind == ResidueKind.Service);
    }

    [Fact]
    public void No_redirect_finding_for_a_plain_profile()
    {
        Scan(@"""C:\g\goodbyedpi.exe"" -5").Should().NotContain(f => f.Kind == ResidueKind.DnsRedirect);
    }

    [Fact]
    public void A_stopped_service_reports_an_inactive_redirect()
    {
        Scan(@"C:\g\goodbyedpi.exe --dns-addr 8.8.8.8", running: false)
            .Should().ContainSingle(f => f.Kind == ResidueKind.DnsRedirect).Which.IsActive.Should().BeFalse();
    }

    [Fact]
    public void The_cleanup_plan_never_acts_on_the_redirect_finding_itself()
    {
        var plan = DpiCleanupPolicy.BuildPlan(Scan(@"""C:\g\goodbyedpi.exe"" --dns-addr 77.88.8.8"));

        plan.Services.Should().ContainSingle();
        plan.Skipped.Should().BeEmpty();
        plan.Count.Should().Be(1);
    }
}

public sealed class DnsRepairFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-dns-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeDnsMachine _machine = new();

    private UndoJournal Journal => new(Path.Combine(_dir, "undo.json"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private DnsRepairFix CreateFix() => new(_machine, _machine, _machine);

    [Fact]
    public async Task Preview_lists_the_adapter_with_its_current_dns_and_changes_nothing()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);

        var preview = await CreateFix().PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeTrue();
        preview.Changes.Should().Contain(l => l.Contains("Ethernet 3") && l.Contains("127.0.0.1"));
        preview.Changes.Should().Contain(l => l.Contains("önbelleği"));
        _machine.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_on_a_healthy_machine_has_nothing_to_do()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["1.1.1.1", "1.0.0.1"]);

        var preview = await CreateFix().PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task A_running_dnscrypt_proxy_means_a_deliberate_setup_and_nothing_is_touched()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);
        _machine.Services.Add(new ServiceEntry("dnscrypt-proxy", @"C:\dnscrypt\dnscrypt-proxy.exe", false, true));
        var fix = CreateFix();

        var preview = await fix.PreviewAsync(CancellationToken.None);
        var result = await fix.ApplyAsync(Journal, CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        preview.Changes.Should().ContainSingle().Which.Should().Contain("DnsCrypt-Proxy");
        result.Success.Should().BeTrue();
        _machine.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_stopped_dnscrypt_proxy_does_not_protect_a_dead_loopback()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);
        _machine.Services.Add(new ServiceEntry("dnscrypt-proxy", @"C:\dnscrypt\dnscrypt-proxy.exe", false, false));

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _machine.Calls.Should().Contain("auto:Ethernet 3");
    }

    [Fact]
    public async Task Virtual_and_disconnected_adapters_are_never_touched()
    {
        _machine.AddAdapter("vEthernet (Default Switch)", v4: ["127.0.0.1"], description: "Hyper-V Virtual Ethernet Adapter");
        _machine.AddAdapter("Wi-Fi", v4: ["127.0.0.1"], status: OperationalStatus.Down);

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _machine.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_backs_up_first_then_sets_automatic_then_flushes()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"], v6: ["::1"]);
        var journal = Journal;
        IReadOnlyList<UndoRecord>? seenAtFirstChange = null;
        _machine.OnSetAutomatic = async _ => seenAtFirstChange ??= await journal.LoadAsync(CancellationToken.None);

        var result = await CreateFix().ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("1 bağlantının DNS'i otomatiğe alındı");
        _machine.Calls.Should().Equal("read:Ethernet 3", "auto:Ethernet 3", "flush");
        seenAtFirstChange.Should().ContainSingle();
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();
        record.FixId.Should().Be(DnsRepairFix.FixId);
        record.Data["adapter"].Should().Be("Ethernet 3");
        record.Data["v4"].Should().Be("127.0.0.1");
        record.Data["v6"].Should().Be("::1");
    }

    [Fact]
    public async Task Every_affected_adapter_is_fixed()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);
        _machine.AddAdapter("Wi-Fi", v6: ["::1"]);

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Summary.Should().Contain("2 bağlantının");
        _machine.Calls.Should().Contain("auto:Ethernet 3").And.Contain("auto:Wi-Fi");
        _machine.Calls.Count(c => c == "flush").Should().Be(1);
    }

    [Fact]
    public async Task Running_twice_is_harmless()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);
        var fix = CreateFix();
        await fix.ApplyAsync(Journal, CancellationToken.None);
        var callsAfterFirst = _machine.Calls.Count;

        var second = await fix.ApplyAsync(Journal, CancellationToken.None);

        second.Success.Should().BeTrue();
        _machine.Calls.Should().HaveCount(callsAfterFirst);
    }

    [Fact]
    public async Task Without_a_readable_backup_nothing_is_changed()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);
        _machine.NoSnapshot.Add("Ethernet 3");
        var journal = Journal;

        var result = await CreateFix().ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("okunamadı");
        _machine.Calls.Should().NotContain("auto:Ethernet 3");
        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_change_leaves_no_undo_record()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);
        _machine.FailSet.Add("Ethernet 3");
        var journal = Journal;

        var result = await CreateFix().ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("değiştirilemedi");
        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty();
        _machine.Calls.Should().NotContain("flush");
    }

    [Fact]
    public async Task A_failed_cache_flush_is_reported_but_the_change_stays()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);
        _machine.FailFlush = true;
        var journal = Journal;

        var result = await CreateFix().ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("önbelleği temizlenemedi");
        (await journal.LoadAsync(CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task Undo_puts_the_old_servers_back_and_flushes_the_cache()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1", "8.8.8.8"], v6: ["::1"]);
        var journal = Journal;
        var fix = CreateFix();
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();
        _machine.Calls.Clear();

        await fix.UndoAsync(record, CancellationToken.None);

        _machine.Calls.Should().Equal("restore:Ethernet 3", "flush");
        _machine.Restored.Should().ContainSingle();
        _machine.Restored[0].V4Servers.Should().Equal("127.0.0.1", "8.8.8.8");
        _machine.Restored[0].V6Servers.Should().Equal("::1");
    }

    [Fact]
    public async Task A_failed_restore_is_reported()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);
        var journal = Journal;
        var fix = CreateFix();
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();
        _machine.FailRestore = true;

        var act = () => fix.UndoAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*geri yüklenemedi*");
    }

    [Fact]
    public async Task A_damaged_undo_record_is_rejected()
    {
        var record = new UndoRecord(Guid.NewGuid(), DnsRepairFix.FixId, DateTimeOffset.UtcNow, "x", new Dictionary<string, string>());

        var act = () => CreateFix().UndoAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bozuk*");
    }

    /// <summary>One coherent fake: adapters whose DNS reacts to the configurator, plus a call log.</summary>
    private sealed class FakeDnsMachine : INetworkInfoProvider, IDnsConfigurator, IServiceSource
    {
        private readonly List<(string Name, string Description, OperationalStatus Status)> _meta = [];
        private readonly Dictionary<string, (List<string> V4, List<string> V6)> _static = [];

        public List<ServiceEntry> Services { get; } = [];

        public List<string> Calls { get; } = [];

        public List<DnsSnapshot> Restored { get; } = [];

        public HashSet<string> NoSnapshot { get; } = [];

        public HashSet<string> FailSet { get; } = [];

        public bool FailFlush { get; set; }

        public bool FailRestore { get; set; }

        public Func<string, Task>? OnSetAutomatic { get; set; }

        public void AddAdapter(
            string name,
            string[]? v4 = null,
            string[]? v6 = null,
            string description = "Intel(R) Ethernet Connection",
            OperationalStatus status = OperationalStatus.Up)
        {
            _meta.Add((name, description, status));
            _static[name] = ((v4 ?? []).ToList(), (v6 ?? []).ToList());
        }

        public IReadOnlyList<AdapterInfo> GetAdapters()
        {
            return _meta.Select(m =>
            {
                var (v4, v6) = _static[m.Name];
                var dns = v4.Concat(v6).ToList();
                if (dns.Count == 0)
                {
                    dns.Add("192.168.1.1"); // automatic: the modem answers
                }

                return new AdapterInfo(
                    m.Name, m.Description, NetworkInterfaceType.Ethernet, m.Status,
                    [IPAddress.Parse("192.168.1.20")], [IPAddress.Parse("192.168.1.1")],
                    dns.Select(IPAddress.Parse).ToList(), "{" + m.Name + "}");
            }).ToList();
        }

        public IReadOnlyList<ServiceEntry> GetServicesAndDrivers() => Services.ToList();

        public Task<DnsSnapshot?> ReadAsync(AdapterInfo adapter, CancellationToken ct)
        {
            Calls.Add($"read:{adapter.Name}");
            if (NoSnapshot.Contains(adapter.Name))
            {
                return Task.FromResult<DnsSnapshot?>(null);
            }

            var (v4, v6) = _static[adapter.Name];
            return Task.FromResult<DnsSnapshot?>(new DnsSnapshot(adapter.Name, v4.ToList(), v6.ToList()));
        }

        public async Task<bool> SetAutomaticAsync(string adapterName, CancellationToken ct)
        {
            Calls.Add($"auto:{adapterName}");
            if (OnSetAutomatic is not null)
            {
                await OnSetAutomatic(adapterName);
            }

            if (FailSet.Contains(adapterName))
            {
                return false;
            }

            _static[adapterName] = ([], []);
            return true;
        }

        public Task<bool> RestoreAsync(DnsSnapshot snapshot, CancellationToken ct)
        {
            Calls.Add($"restore:{snapshot.AdapterName}");
            if (FailRestore)
            {
                return Task.FromResult(false);
            }

            Restored.Add(snapshot);
            _static[snapshot.AdapterName] = (snapshot.V4Servers.ToList(), snapshot.V6Servers.ToList());
            return Task.FromResult(true);
        }

        public Task<bool> FlushCacheAsync(CancellationToken ct)
        {
            Calls.Add("flush");
            return Task.FromResult(!FailFlush);
        }

        public Task<bool> SetStaticAsync(string adapterName, IReadOnlyList<string> v4Servers, IReadOnlyList<string> v6Servers, CancellationToken ct) =>
            Task.FromResult(true);

    }
}
