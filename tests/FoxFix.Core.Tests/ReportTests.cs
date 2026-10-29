using FluentAssertions;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Dpi;
using FoxFix.Core.Reporting;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public class SystemInfoTests
{
    [Theory]
    [InlineData("10.0.26200.0", "Windows 11 (sürüm 10.0.26200.0)")]
    [InlineData("10.0.22000.0", "Windows 11 (sürüm 10.0.22000.0)")]
    [InlineData("10.0.19045.0", "Windows 10 (sürüm 10.0.19045.0)")]
    public void Windows_10_and_11_are_told_apart_by_build_number(string version, string expected)
    {
        SystemInfo.DescribeWindows(Version.Parse(version)).Should().Be(expected);
    }

    [Fact]
    public void Other_versions_fall_back_to_the_runtime_description()
    {
        SystemInfo.DescribeWindows(new Version(6, 1, 7601)).Should().BeNull();
    }
}

public class PersonalDataMaskerTests
{
    [Theory]
    [InlineData("MAC: AA-BB-CC-DD-EE-FF")]
    [InlineData("MAC: aa:bb:cc:dd:ee:ff")]
    [InlineData("a 00-1A-2b-3C-4d-5E b")]
    public void Mac_addresses_are_hidden_in_either_notation(string text)
    {
        var masked = PersonalDataMasker.Mask(text, "", "");

        masked.Should().Contain(PersonalDataMasker.MaskedMac);
        masked.Should().NotMatchRegex("[0-9A-Fa-f]{2}[-:][0-9A-Fa-f]{2}[-:][0-9A-Fa-f]{2}");
    }

    [Fact]
    public void The_user_name_is_hidden_in_paths_and_text_regardless_of_case()
    {
        var masked = PersonalDataMasker.Mask(@"C:\Users\foxezol\Downloads\gdpi ve FOXEZOL kullanıcısı", "foxezol", "PC-1");

        masked.Should().Be(@"C:\Users\<kullanıcı>\Downloads\gdpi ve <kullanıcı> kullanıcısı");
    }

    [Fact]
    public void The_computer_name_is_hidden()
    {
        PersonalDataMasker.Mask("Bilgisayar DESKTOP-ABC123 açık", "x", "DESKTOP-ABC123")
            .Should().Be("Bilgisayar <bilgisayar> açık");
    }

    [Fact]
    public void Only_whole_words_are_hidden()
    {
        PersonalDataMasker.Mask("kalite ve Ali ve ALİ", "ali", "").Should().Be("kalite ve <kullanıcı> ve ALİ");
    }

    [Theory]
    [InlineData("al")]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("   ")]
    public void Very_short_or_empty_names_are_left_alone_so_ordinary_words_survive(string name)
    {
        PersonalDataMasker.Mask("al ve bal ve a", name, name).Should().Be("al ve bal ve a");
    }

    [Fact]
    public void Special_characters_in_a_name_cannot_break_the_pattern()
    {
        var act = () => PersonalDataMasker.Mask("a.b(c) test", "a.b(c)", "[x");

        act.Should().NotThrow();
        PersonalDataMasker.Mask("a.b(c) test", "a.b(c)", "[x").Should().Be("<kullanıcı> test");
    }

    [Fact]
    public void Own_and_router_addresses_are_hidden_but_well_known_public_DNS_stays()
    {
        PersonalDataMasker.Mask("IP 192.168.1.23, ağ geçidi 10.0.0.1, DNS 192.168.1.1, 1.1.1.1, 8.8.8.8", "foxezol", "pc")
            .Should().Be("IP <ip>, ağ geçidi <ip>, DNS <ip>, 1.1.1.1, 8.8.8.8");
    }

    [Theory]
    [InlineData("DNS 127.0.0.1 üzerinde", "DNS 127.0.0.1 üzerinde")]
    [InlineData("Maske 255.255.255.0 ve 0.0.0.0", "Maske 255.255.255.0 ve 0.0.0.0")]
    [InlineData("Genel IP 85.105.44.201 görünüyor", "Genel IP <ip> görünüyor")]
    [InlineData("Sürüm 10.0.26200.1 çalışıyor", "Sürüm 10.0.26200.1 çalışıyor")]
    public void Loopback_and_subnet_masks_say_something_about_the_diagnosis_and_stay(string text, string expected)
    {
        PersonalDataMasker.Mask(text, "", "").Should().Be(expected);
    }

    [Theory]
    [InlineData("adres fe80::1a2b:3c4d:5e6f:7a8b%12 sonu", "adres <ip> sonu")]
    [InlineData("adres 2a02:ff0:1234:5678::1 sonu", "adres <ip> sonu")]
    [InlineData("loopback ::1 kalır", "loopback ::1 kalır")]
    [InlineData("DNS 2606:4700:4700::1111 kalır", "DNS 2606:4700:4700::1111 kalır")]
    [InlineData("saat 12:30:45 kalır", "saat 12:30:45 kalır")]
    public void Ipv6_addresses_are_hidden_and_clock_times_are_not_mistaken_for_them(string text, string expected)
    {
        PersonalDataMasker.Mask(text, "", "").Should().Be(expected);
    }
}

public class DiagnosticReportBuilderTests
{
    private static readonly DateTimeOffset When = new(2026, 10, 27, 20, 30, 0, TimeSpan.FromHours(3));

    private static ReportInput Input(
        IReadOnlyList<CheckReport>? checks = null,
        IReadOnlyList<ResidueFinding>? findings = null,
        IReadOnlyList<UndoRecord>? changes = null,
        IReadOnlyList<ReportAdapter>? adapters = null) => new(
        When,
        "0.1.0",
        "Microsoft Windows 11 Pro 10.0.26200",
        "foxezol",
        "DESKTOP-ABC123",
        adapters ?? [new ReportAdapter("Ethernet 3", "Realtek PCIe GbE", true, "AA-BB-CC-DD-EE-FF", ["192.168.1.109"], ["192.168.1.1"], ["1.1.1.1", "1.0.0.1"])],
        checks,
        findings ?? [],
        changes ?? []);

    private static CheckReport Check(string title, CheckStatus status, string summary, string? detail = null) =>
        new(title, title, new CheckResult(status, summary, detail));

    [Fact]
    public void The_header_names_the_tool_version_windows_and_time()
    {
        var report = DiagnosticReportBuilder.Build(Input(), maskPersonalData: true);

        report.Should().StartWith("FoxFix Teşhis Raporu");
        report.Should().Contain("27.10.2026 20:30").And.Contain("0.1.0").And.Contain("Windows 11 Pro");
    }

    [Fact]
    public void Masked_reports_hide_the_user_the_computer_and_the_mac_by_default()
    {
        var report = DiagnosticReportBuilder.Build(
            Input(findings: [Finding(@"C:\Users\foxezol\Downloads\goodbyedpi.exe")]), maskPersonalData: true);

        report.Should().NotContain("foxezol").And.NotContain("DESKTOP-ABC123").And.NotContain("AA-BB-CC-DD-EE-FF");
        report.Should().Contain("gizlendi").And.Contain(PersonalDataMasker.MaskedMac).And.Contain(@"C:\Users\<kullanıcı>\Downloads");
    }

    [Fact]
    public void Unmasked_reports_show_everything_and_warn_the_user()
    {
        var report = DiagnosticReportBuilder.Build(Input(), maskPersonalData: false);

        report.Should().Contain("Kullanıcı: foxezol").And.Contain("Bilgisayar: DESKTOP-ABC123").And.Contain("AA-BB-CC-DD-EE-FF");
        report.Should().Contain("kişisel bilgiler gizlenmedi");
    }

    [Fact]
    public void Without_a_diagnosis_the_report_says_so_instead_of_inventing_results()
    {
        var report = DiagnosticReportBuilder.Build(Input(checks: null), maskPersonalData: true);

        report.Should().Contain("Henüz kontrol yapılmadı");
        report.Should().NotContain("Kontroller\n").And.NotContain("[Sorun yok]");
    }

    [Fact]
    public void A_healthy_diagnosis_is_summarised_as_such()
    {
        var report = DiagnosticReportBuilder.Build(Input(checks: [Check("DNS", CheckStatus.Passed, "çözülüyor")]), true);

        report.Should().Contain("Sorun yok.").And.Contain("[Sorun yok] DNS: çözülüyor");
    }

    [Fact]
    public void Warnings_are_counted_and_errors_count_as_notes_too()
    {
        var checks = new[] { Check("A", CheckStatus.Warning, "x"), Check("B", CheckStatus.Error, "y"), Check("C", CheckStatus.Passed, "z") };

        var report = DiagnosticReportBuilder.Build(Input(checks: checks), true);

        report.Should().Contain("Bağlantı çalışıyor, 2 şeye bakmak gerek.");
    }

    [Fact]
    public void A_failed_layer_is_named_in_the_summary()
    {
        var report = DiagnosticReportBuilder.Build(Input(checks: [Check("İnternet adreslerini bulma (DNS)", CheckStatus.Failed, "çözülemiyor")]), true);

        report.Should().Contain("Sorun var: İnternet adreslerini bulma (DNS).");
    }

    [Fact]
    public void Check_details_are_indented_under_their_check()
    {
        var report = DiagnosticReportBuilder.Build(
            Input(checks: [Check("Kalıntı", CheckStatus.Warning, "2 bulgu", "GoodbyeDPI çalışıyor\nWinDivert çalışıyor")]), true);

        report.Should().Contain("    GoodbyeDPI çalışıyor").And.Contain("    WinDivert çalışıyor");
    }

    [Fact]
    public void Adapters_list_their_addresses_gateway_and_dns()
    {
        var report = DiagnosticReportBuilder.Build(Input(), true);

        report.Should().Contain("Ethernet 3 (Realtek PCIe GbE): açık")
            .And.Contain("IPv4: <ip>").And.Contain("Ağ geçidi: <ip>").And.Contain("DNS: 1.1.1.1, 1.0.0.1")
            .And.NotContain("192.168.1");

        DiagnosticReportBuilder.Build(Input(), false).Should().Contain("IPv4: 192.168.1.109").And.Contain("Ağ geçidi: 192.168.1.1");
    }

    [Fact]
    public void Missing_addresses_are_written_as_none()
    {
        var adapter = new ReportAdapter("Wi-Fi", "Intel", false, null, [], [], []);

        var report = DiagnosticReportBuilder.Build(Input(adapters: [adapter]), true);

        report.Should().Contain("Wi-Fi (Intel): bağlı değil").And.Contain("IPv4: yok").And.Contain("DNS: yok");
        report.Should().NotContain("MAC:");
    }

    [Fact]
    public void No_adapters_is_said_plainly()
    {
        DiagnosticReportBuilder.Build(Input(adapters: []), true).Should().Contain("Fiziksel ağ kartı bulunamadı.");
    }

    [Fact]
    public void Findings_state_how_sure_we_are_and_whether_foxfix_may_clean_them()
    {
        var report = DiagnosticReportBuilder.Build(Input(findings: [Finding(@"C:\gdpi\goodbyedpi.exe")]), true);

        report.Should().Contain("GoodbyeDPI çalışıyor").And.Contain("kesin, temizlenebilir, çalışıyor").And.Contain(@"C:\gdpi\goodbyedpi.exe");
    }

    [Fact]
    public void Possible_and_report_only_findings_are_labelled_so()
    {
        var finding = new ResidueFinding("x", ResidueKind.Folder, "SplitWire-Turkey", null, false,
            ResidueConfidence.Possible, ResidueHandling.ReportOnly, "SplitWire kurulu");

        DiagnosticReportBuilder.Build(Input(findings: [finding]), true).Should().Contain("olası, yalnızca bildirim, durmuş");
    }

    [Fact]
    public void No_findings_and_no_changes_say_so()
    {
        var report = DiagnosticReportBuilder.Build(Input(), true);

        report.Should().Contain("Bulgu yok.").And.Contain("FoxFix henüz bir değişiklik yapmadı.");
    }

    [Fact]
    public void Foxfix_own_changes_are_listed_oldest_first()
    {
        var older = new UndoRecord(Guid.NewGuid(), "dns-repair", When.AddHours(-2), "Ethernet 3 DNS eski haline getirilebilir", new Dictionary<string, string>());
        var newer = new UndoRecord(Guid.NewGuid(), "dpi-cleanup", When.AddHours(-1), "GoodbyeDPI hizmeti eski haline getirilebilir", new Dictionary<string, string>());

        var report = DiagnosticReportBuilder.Build(Input(changes: [newer, older]), true);

        report.IndexOf("Ethernet 3 DNS", StringComparison.Ordinal).Should().BeLessThan(report.IndexOf("GoodbyeDPI hizmeti", StringComparison.Ordinal));
    }

    [Fact]
    public void The_report_is_plain_text_ending_in_a_single_newline()
    {
        var report = DiagnosticReportBuilder.Build(Input(), true);

        report.Should().EndWith(Environment.NewLine).And.NotEndWith(Environment.NewLine + Environment.NewLine);
        report.Should().NotContain("\t");
    }

    private static ResidueFinding Finding(string path) => new(
        "goodbyedpi", ResidueKind.Service, "GoodbyeDPI", path, true,
        ResidueConfidence.Confirmed, ResidueHandling.Removable, "GoodbyeDPI çalışıyor: GoodbyeDPI");
}

public sealed class DiagnosticReportServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-report-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class FakeSystem : ISystemInfo
    {
        public string OsDescription => "Windows Test";

        public string UserName => "foxezol";

        public string MachineName => "DESKTOP-ABC123";

        public string FoxFixVersion => "9.9.9";

        public DateTimeOffset Now => new(2026, 1, 2, 3, 4, 0, TimeSpan.Zero);
    }

    private sealed class NoServices : IServiceSource
    {
        public IReadOnlyList<ServiceEntry> GetServicesAndDrivers() =>
            [new ServiceEntry("GoodbyeDPI", @"C:\Users\foxezol\gdpi\goodbyedpi.exe -5", false, true)];
    }

    private sealed class NoProcesses : IProcessSource
    {
        public IReadOnlyList<ProcessEntry> GetProcesses() => [];
    }

    private sealed class NoPaths : IPathProbe
    {
        public bool DirectoryExists(string path) => false;
    }

    private DiagnosticReportService CreateService(UndoJournal journal)
    {
        var network = new FakeNetworkInfo(
            Adapters.Wifi(addresses: ["192.168.1.20"], gateways: ["192.168.1.1"], dns: ["1.1.1.1"]),
            Adapters.Wifi(description: "Hyper-V Virtual Ethernet Adapter", addresses: ["172.20.0.1"]));
        var detector = new DpiResidueDetector(new NoServices(), new NoProcesses(), new NoPaths(), network, []);
        return new DiagnosticReportService(network, detector, journal, new FakeSystem());
    }

    [Fact]
    public async Task It_gathers_adapters_findings_changes_and_the_last_diagnosis()
    {
        var journal = new UndoJournal(Path.Combine(_dir, "undo.json"));
        await journal.AppendAsync(
            new UndoRecord(Guid.NewGuid(), "dns-repair", DateTimeOffset.UtcNow, "Ethernet DNS eski haline getirilebilir", new Dictionary<string, string>()),
            CancellationToken.None);
        var diagnosis = new DiagnosticsReport([new CheckReport("dns", "DNS", new CheckResult(CheckStatus.Passed, "çözülüyor"))]);

        var report = await CreateService(journal).CreateAsync(diagnosis, maskPersonalData: true, CancellationToken.None);

        report.Should().Contain("9.9.9").And.Contain("Windows Test");
        report.Should().Contain("[Sorun yok] DNS: çözülüyor");
        report.Should().Contain("IPv4: <ip>").And.NotContain("192.168.1.20");
        report.Should().NotContain("172.20.0.1", "virtual adapters are not listed");
        report.Should().Contain("GoodbyeDPI çalışıyor");
        report.Should().Contain("Ethernet DNS eski haline getirilebilir");
    }

    [Fact]
    public async Task Masking_covers_data_that_came_from_the_live_scan()
    {
        var journal = new UndoJournal(Path.Combine(_dir, "undo.json"));

        var masked = await CreateService(journal).CreateAsync(null, maskPersonalData: true, CancellationToken.None);
        var open = await CreateService(journal).CreateAsync(null, maskPersonalData: false, CancellationToken.None);

        masked.Should().NotContain("foxezol").And.Contain(@"C:\Users\<kullanıcı>\gdpi\goodbyedpi.exe");
        open.Should().Contain(@"C:\Users\foxezol\gdpi\goodbyedpi.exe");
    }

    [Fact]
    public async Task Without_a_diagnosis_it_still_produces_a_useful_report()
    {
        var journal = new UndoJournal(Path.Combine(_dir, "undo.json"));

        var report = await CreateService(journal).CreateAsync(null, true, CancellationToken.None);

        report.Should().Contain("Henüz kontrol yapılmadı").And.Contain("Ağ kartları");
    }

    [Fact]
    public async Task Building_a_report_never_writes_to_the_journal()
    {
        var path = Path.Combine(_dir, "undo.json");

        await CreateService(new UndoJournal(path)).CreateAsync(null, true, CancellationToken.None);

        File.Exists(path).Should().BeFalse();
    }
}
