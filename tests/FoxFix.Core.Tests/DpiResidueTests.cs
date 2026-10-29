using FluentAssertions;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Diagnostics.Checks;
using FoxFix.Core.Dpi;
using Xunit;

namespace FoxFix.Core.Tests;

public class ServicePathParserTests
{
    [Theory]
    [InlineData("\"C:\\Users\\a\\Downloads\\goodbyedpi\\x86_64\\goodbyedpi.exe\" -5 --set-ttl 5", "goodbyedpi.exe")]
    [InlineData("C:\\Program Files\\Some Tool\\winws.exe --wf-tcp=443", "winws.exe")]
    [InlineData("\\??\\C:\\tools\\goodbyedpi\\x86_64\\WinDivert64.sys", "WinDivert64.sys")]
    [InlineData("C:\\Windows\\system32\\drivers\\Monkey64.sys", "Monkey64.sys")]
    [InlineData("C:\\Windows\\System32\\svchost.exe -k netsvcs -p", "svchost.exe")]
    public void Extracts_the_file_name(string pathName, string expected)
    {
        ServicePathParser.GetFileName(pathName).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unterminated")]
    public void Returns_null_for_unusable_input(string? pathName)
    {
        ServicePathParser.GetFileName(pathName).Should().BeNull();
    }
}

public class DpiResidueDetectorTests
{
    private sealed class FakeServices : IServiceSource
    {
        private readonly IReadOnlyList<ServiceEntry> _entries;

        public FakeServices(params ServiceEntry[] entries) => _entries = entries;

        public IReadOnlyList<ServiceEntry> GetServicesAndDrivers() => _entries;
    }

    private sealed class FakeProcesses : IProcessSource
    {
        private readonly IReadOnlyList<ProcessEntry> _entries;

        public FakeProcesses(params ProcessEntry[] entries) => _entries = entries;

        public IReadOnlyList<ProcessEntry> GetProcesses() => _entries;
    }

    private sealed class FakePaths : IPathProbe
    {
        private readonly HashSet<string> _existing;

        public FakePaths(params string[] existing) => _existing = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => _existing.Contains(path);
    }

    private const string SplitWireDir = @"C:\Program Files\SplitWire-Turkey";

    private static ServiceEntry Svc(string name, string? path, bool running = true, bool driver = false) =>
        new(name, path, driver, running);

    private static IReadOnlyList<ResidueFinding> Scan(
        ServiceEntry[]? services = null,
        ProcessEntry[]? processes = null,
        string[]? existingDirs = null,
        params AdapterInfoStub[] adapters)
    {
        var network = new FakeNetworkInfo(adapters.Select(a => a.Build()).ToArray());
        var detector = new DpiResidueDetector(
            new FakeServices(services ?? []),
            new FakeProcesses(processes ?? []),
            new FakePaths(existingDirs ?? []),
            network,
            [SplitWireDir, @"C:\Users\x\AppData\Local\SplitWire-Turkey"]);

        return detector.Scan();
    }

    private sealed record AdapterInfoStub(string[] Dns, bool Physical = true)
    {
        public FoxFix.Core.Network.AdapterInfo Build() => Adapters.Wifi(
            addresses: ["192.168.1.20"],
            gateways: ["192.168.1.1"],
            dns: Dns,
            description: Physical ? "Intel(R) Wi-Fi 6 AX201 160MHz" : "Hyper-V Virtual Ethernet Adapter");
    }

    [Fact]
    public void A_clean_machine_has_no_findings()
    {
        var findings = Scan(
            services:
            [
                Svc("Dnscache", @"C:\Windows\system32\svchost.exe -k NetworkService -p"),
                Svc("wuauserv", @"C:\Windows\system32\svchost.exe -k netsvcs -p"),
                Svc("MonkeyApp", @"C:\Apps\Monkey\monkey.exe"),
                Svc("Npcap", @"C:\Windows\system32\DRIVERS\npcap.sys", driver: true),
            ],
            processes: [new ProcessEntry("explorer", null), new ProcessEntry("winwsx", null), new ProcessEntry("notepad", null)],
            adapters: new AdapterInfoStub(["192.168.1.1"]));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void Service_with_goodbyedpi_binary_is_confirmed()
    {
        var findings = Scan(services: [Svc("GoodbyeDPI", @"""C:\gdpi\x86_64\goodbyedpi.exe"" -5")]);

        var finding = findings.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("goodbyedpi");
        finding.Kind.Should().Be(ResidueKind.Service);
        finding.Confidence.Should().Be(ResidueConfidence.Confirmed);
        finding.IsActive.Should().BeTrue();
        finding.Handling.Should().Be(ResidueHandling.Removable);
    }

    [Fact]
    public void Renamed_service_is_found_through_its_binary_path()
    {
        var findings = Scan(services: [Svc("UpdaterX", @"""C:\Users\a\Downloads\goodbyedpi\x86_64\goodbyedpi.exe"" -5 --set-ttl 5")]);

        findings.Should().ContainSingle(f => f.RuleId == "goodbyedpi" && f.Confidence == ResidueConfidence.Confirmed);
    }

    [Fact]
    public void Name_only_match_is_marked_possible()
    {
        var findings = Scan(services: [Svc("GoodbyeDPI", null, running: false)]);

        var finding = findings.Should().ContainSingle().Subject;
        finding.Confidence.Should().Be(ResidueConfidence.Possible);
        finding.IsActive.Should().BeFalse();
        finding.Message.Should().Contain("kesin değil");
    }

    [Fact]
    public void Monkey_service_is_only_flagged_with_the_zapret_driver_file()
    {
        var unrelated = Scan(services: [Svc("monkey", @"C:\Apps\Monkey\monkey.exe")]);
        var zapret = Scan(services: [Svc("monkey", @"\??\C:\zapret-win-turkey\bin\Monkey64.sys", driver: true)]);

        unrelated.Should().BeEmpty();
        zapret.Should().ContainSingle(f => f.RuleId == "zapret" && f.Kind == ResidueKind.Driver);
    }

    [Fact]
    public void Stopped_windivert_driver_is_a_leftover_not_active()
    {
        var findings = Scan(services: [Svc("WinDivert", @"\??\C:\gdpi\x86_64\WinDivert64.sys", running: false, driver: true)]);

        var finding = findings.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("windivert");
        finding.IsActive.Should().BeFalse();
        finding.Message.Should().Contain("kalıntısı");
    }

    [Theory]
    [InlineData("goodbyedpi", "goodbyedpi")]
    [InlineData("winws", "zapret")]
    [InlineData("WinWS2", "zapret")]
    [InlineData("ciadpi", "byedpi")]
    [InlineData("dnscrypt-proxy", "dnscrypt")]
    public void Known_processes_are_found(string processName, string expectedRule)
    {
        var findings = Scan(processes: [new ProcessEntry(processName, null)]);

        findings.Should().ContainSingle(f => f.RuleId == expectedRule && f.Kind == ResidueKind.Process);
    }

    [Fact]
    public void SplitWire_is_report_only_and_never_removable()
    {
        var findings = Scan(existingDirs: [SplitWireDir]);

        var finding = findings.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("splitwire");
        finding.Handling.Should().Be(ResidueHandling.ReportOnly);
        finding.Message.Should().Contain("FoxFix bunu silmez");
    }

    [Fact]
    public void Loopback_dns_without_a_running_proxy_is_removable_orphan()
    {
        var findings = Scan(adapters: new AdapterInfoStub(["127.0.0.1"]));

        var finding = findings.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("dns-loopback-orphan");
        finding.Handling.Should().Be(ResidueHandling.Removable);
    }

    [Fact]
    public void Loopback_dns_with_a_stopped_proxy_is_still_an_orphan()
    {
        var findings = Scan(
            services: [Svc("dnscrypt-proxy", @"C:\dnscrypt\dnscrypt-proxy.exe -service", running: false)],
            adapters: new AdapterInfoStub(["127.0.0.1"]));

        findings.Should().Contain(f => f.RuleId == "dns-loopback-orphan");
    }

    [Fact]
    public void Loopback_dns_with_a_running_proxy_is_left_alone()
    {
        var findings = Scan(
            services: [Svc("dnscrypt-proxy", @"C:\dnscrypt\dnscrypt-proxy.exe -service")],
            adapters: new AdapterInfoStub(["127.0.0.1"]));

        findings.Should().Contain(f => f.RuleId == "dns-loopback-proxy" && f.Handling == ResidueHandling.ReportOnly);
        findings.Should().NotContain(f => f.RuleId == "dns-loopback-orphan");
    }

    [Fact]
    public void Ipv6_loopback_dns_counts_too()
    {
        var findings = Scan(adapters: new AdapterInfoStub(["::1"]));

        findings.Should().ContainSingle(f => f.RuleId == "dns-loopback-orphan");
    }

    [Fact]
    public void Loopback_dns_on_a_virtual_adapter_is_ignored()
    {
        var findings = Scan(adapters: new AdapterInfoStub(["127.0.0.1"], Physical: false));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void Goodbyedpi_and_zapret_running_together_raise_a_conflict()
    {
        var findings = Scan(processes: [new ProcessEntry("goodbyedpi", null), new ProcessEntry("winws", null)]);

        findings.Should().ContainSingle(f => f.Kind == ResidueKind.Conflict && f.Handling == ResidueHandling.ReportOnly);
    }

    [Fact]
    public void A_stopped_tool_next_to_a_running_one_is_not_a_conflict()
    {
        var findings = Scan(
            services: [Svc("GoodbyeDPI", @"C:\gdpi\goodbyedpi.exe", running: false)],
            processes: [new ProcessEntry("winws", null)]);

        findings.Should().NotContain(f => f.Kind == ResidueKind.Conflict);
    }

    [Fact]
    public void Nothing_found_is_never_removable_unless_it_matches_the_allowlist()
    {
        var findings = Scan(
            services: [Svc("Dnscache", @"C:\Windows\system32\svchost.exe -k NetworkService -p")],
            existingDirs: [SplitWireDir]);

        findings.Where(f => f.Handling == ResidueHandling.Removable).Should().BeEmpty();
    }
}

public class DpiResidueCheckTests
{
    private sealed class OneService : IServiceSource
    {
        private readonly ServiceEntry[] _entries;

        public OneService(params ServiceEntry[] entries) => _entries = entries;

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

    private static DpiResidueCheck Create(FoxFix.Core.Network.AdapterInfo adapter, params ServiceEntry[] services) =>
        new(new DpiResidueDetector(new OneService(services), new NoProcesses(), new NoPaths(), new FakeNetworkInfo(adapter), []));

    private static FoxFix.Core.Network.AdapterInfo Healthy(params string[] dns) =>
        Adapters.Wifi(addresses: ["192.168.1.20"], gateways: ["192.168.1.1"], dns: dns);

    [Fact]
    public async Task Passes_when_nothing_is_found()
    {
        var result = await Create(Healthy("192.168.1.1")).RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Warns_for_a_leftover_and_lists_it_in_the_detail()
    {
        var service = new ServiceEntry("GoodbyeDPI", @"C:\gdpi\goodbyedpi.exe", false, false);

        var result = await Create(Healthy("192.168.1.1"), service).RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Warning);
        result.Detail.Should().Contain("GoodbyeDPI");
    }

    [Fact]
    public async Task Fails_when_dns_points_to_a_dead_loopback()
    {
        var result = await Create(Healthy("127.0.0.1")).RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
    }
}
