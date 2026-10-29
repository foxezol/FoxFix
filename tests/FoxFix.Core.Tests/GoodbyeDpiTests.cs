using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Diagnostics.Checks;
using FoxFix.Core.Dpi;
using FoxFix.Core.GoodbyeDpi;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class GoodbyeDpiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "foxfix-gdpi-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;
    private readonly string _trialFolder;

    private readonly FakePayload _payload = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeServices _services = new();
    private readonly FakeMachine _machine = new();

    public GoodbyeDpiTests()
    {
        Directory.CreateDirectory(_root);
        _folder = Path.Combine(_root, "GoodbyeDPI");
        _trialFolder = Path.Combine(_root, "GoodbyeDPI-trial");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ---------- fakes ----------

    private sealed class FakePayload : IGoodbyeDpiPayload
    {
        public static readonly string[] Names = ["goodbyedpi.exe", "WinDivert.dll", "WinDivert64.sys"];

        public bool Tamper { get; set; }

        public Dictionary<string, string> Hashes =>
            Names.ToDictionary(n => n, n => Convert.ToHexString(SHA256.HashData(Content(n))));

        public void WriteTo(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach (var name in Names)
            {
                File.WriteAllBytes(Path.Combine(folder, name), Tamper && name == "WinDivert.dll" ? [9, 9, 9] : Content(name));
            }
        }

        private static byte[] Content(string name) => System.Text.Encoding.UTF8.GetBytes("inhalt:" + name);
    }

    private sealed class FakeProcess(FakeLauncher owner, bool exited) : IGoodbyeDpiProcess
    {
        public bool HasExited => exited;

        public Task StopAsync()
        {
            owner.Stopped++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeLauncher : IGoodbyeDpiLauncher
    {
        public List<string> Arguments { get; } = [];

        public int Stopped { get; set; }

        public bool FailStart { get; set; }

        public bool ExitImmediately { get; set; }

        public bool Running { get; set; }

        public IGoodbyeDpiProcess Start(string exePath, string arguments)
        {
            if (FailStart)
            {
                throw new InvalidOperationException("engellendi");
            }

            Arguments.Add(arguments);
            return new FakeProcess(this, ExitImmediately);
        }

        public Task<int> StopAllInFolderAsync(string folder, CancellationToken ct) => Task.FromResult(0);

        public bool IsRunningFromFolder(string folder) => Running;
    }

    private sealed class FakeServices : IServiceManager
    {
        public Dictionary<string, ServiceConfig> Configs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Started { get; } = [];

        public bool StartSucceeds { get; set; } = true;

        public Task<ServiceConfig?> ReadConfigAsync(string name, CancellationToken ct) =>
            Task.FromResult(Configs.GetValueOrDefault(name));

        public Task<StopOutcome> StopAsync(string name, CancellationToken ct) => Task.FromResult(StopOutcome.Stopped);

        public Task<DeleteOutcome> DeleteAsync(string name, CancellationToken ct)
        {
            Configs.Remove(name);
            return Task.FromResult(DeleteOutcome.Deleted);
        }

        public Task<bool> CreateAsync(ServiceConfig config, CancellationToken ct)
        {
            Configs[config.Name] = config;
            return Task.FromResult(true);
        }

        public Task<bool> StartAsync(string name, CancellationToken ct)
        {
            Started.Add(name);
            return Task.FromResult(StartSucceeds);
        }
    }

    private sealed class FakeMachine : IServiceSource, IProcessSource, IPathProbe, INetworkInfoProvider
    {
        public List<ServiceEntry> Services { get; } = [];

        public List<ProcessEntry> Processes { get; } = [];

        public IReadOnlyList<ServiceEntry> GetServicesAndDrivers() => Services;

        public IReadOnlyList<ProcessEntry> GetProcesses() => Processes;

        public bool DirectoryExists(string path) => false;

        public IReadOnlyList<AdapterInfo> GetAdapters() => [];
    }

    /// <summary>Answers "ok" for a site only when the running setting is one the test says opens it.</summary>
    private sealed class FakeProbe : IHttpsProbe
    {
        private readonly FakeLauncher _launcher;

        public FakeProbe(FakeLauncher launcher)
        {
            _launcher = launcher;
        }

        public Func<string, Uri, bool> Opens { get; set; } = (_, _) => false;

        public Task<HttpsProbeOutcome> ProbeAsync(Uri url, TimeSpan timeout, CancellationToken ct)
        {
            var current = _launcher.Arguments.LastOrDefault() ?? "";
            return Task.FromResult(Opens(current, url) ? HttpsProbeOutcome.Ok : HttpsProbeOutcome.TlsFailed);
        }
    }

    private GoodbyeDpiTrial NewTrial(FakeProbe probe, Func<string?>? blocker = null) => new(
        _payload, _launcher, probe, _services, _trialFolder, blocker ?? (() => null), _ => Task.CompletedTask,
        (_, _) => Task.CompletedTask, _payload.Hashes);

    private GoodbyeDpiInstallFix NewFix()
    {
        var detector = new DpiResidueDetector(_machine, _machine, _machine, _machine, [], _root);
        var status = new GoodbyeDpiStatusReader(_machine, _launcher, detector, _folder);
        return new GoodbyeDpiInstallFix(_payload, _launcher, _services, status, _folder, _payload.Hashes);
    }

    private UndoJournal NewJournal() => new(Path.Combine(_root, "undo.json"));

    private static readonly Uri Discord = new("https://discord.com/");

    // ---------- targets, layout, files ----------

    [Fact]
    public void Targets_keep_plain_host_names_only_and_drop_duplicates()
    {
        var uris = TrialTargets.Parse("discord.com, https://x.org/path?q=1  foo; Discord.com\nbad host.com");

        uris.Select(u => u.Host).Should().Equal("discord.com", "x.org", "host.com");
    }

    [Fact]
    public void Managed_means_inside_the_folder_and_nothing_next_to_it()
    {
        var folder = Path.Combine(_root, "GoodbyeDPI");

        GoodbyeDpiLayout.IsManaged($"\"{folder}\\goodbyedpi.exe\" -5", folder).Should().BeTrue();
        GoodbyeDpiLayout.IsManaged(Path.Combine(_root, "GoodbyeDPI-trial", "goodbyedpi.exe"), folder).Should().BeFalse();
        GoodbyeDpiLayout.IsManaged(@"C:\GoodbyeDPI\goodbyedpi.exe", folder).Should().BeFalse();
        GoodbyeDpiLayout.IsManaged(null, folder).Should().BeFalse();
    }

    [Fact]
    public void The_pinned_hashes_cover_the_three_files_FoxFix_runs()
    {
        GoodbyeDpiFiles.Sha256.Keys.Should().BeEquivalentTo("goodbyedpi.exe", "WinDivert.dll", "WinDivert64.sys");
        GoodbyeDpiFiles.Sha256.Values.Should().OnlyContain(h => h.Length == 64);
    }

    [Fact]
    public void A_changed_or_missing_file_is_reported_by_name()
    {
        GoodbyeDpiFiles.Prepare(_payload, _folder, _payload.Hashes).Should().BeEmpty();

        File.WriteAllText(Path.Combine(_folder, "WinDivert.dll"), "başka");
        File.Delete(Path.Combine(_folder, "WinDivert64.sys"));

        GoodbyeDpiFiles.FindProblems(_folder, _payload.Hashes).Should().BeEquivalentTo("WinDivert.dll", "WinDivert64.sys");
    }

    // ---------- trial ----------

    [Fact]
    public async Task When_the_sites_already_open_nothing_is_started()
    {
        var probe = new FakeProbe(_launcher) { Opens = (_, _) => true };

        var report = await NewTrial(probe).RunAsync([Discord], null, CancellationToken.None);

        report.Conclusion.Should().Be(TrialConclusion.NotNeeded);
        _launcher.Arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task The_setting_that_opens_the_most_sites_is_recommended_and_everything_is_cleaned_up()
    {
        var second = new Uri("https://roblox.com/");
        var probe = new FakeProbe(_launcher)
        {
            Opens = (args, url) => url == TrialTargets.Control
                || (args.StartsWith("-9 --dns-addr") && true)
                || (args == "-5" && url == Discord),
        };
        var steps = new List<TrialStep>();

        var report = await NewTrial(probe).RunAsync([Discord, second], new Progress<TrialStep>(steps.Add), CancellationToken.None);

        report.Conclusion.Should().Be(TrialConclusion.Found);
        report.Best!.Id.Should().Be("turkey-5");
        report.Best.Arguments.Should().StartWith("-9 --dns-addr 77.88.8.8");
        report.Results.Should().HaveCount(GoodbyeDpiProfiles.All.Count);
        _launcher.Arguments.Should().HaveCount(GoodbyeDpiProfiles.All.Count);
        _launcher.Stopped.Should().Be(GoodbyeDpiProfiles.All.Count, "every setting is stopped again");
        Directory.Exists(_trialFolder).Should().BeFalse("the temporary copy is removed");
    }

    [Fact]
    public async Task A_setting_that_breaks_normal_sites_is_never_recommended()
    {
        var probe = new FakeProbe(_launcher) { Opens = (args, url) => url == Discord && args.StartsWith("-5 --set-ttl 5") };

        var report = await NewTrial(probe).RunAsync([Discord], null, CancellationToken.None);

        report.Conclusion.Should().Be(TrialConclusion.NothingHelped, "the control site stayed closed, so the setting breaks the connection");
        report.Best.Should().BeNull();
    }

    [Fact]
    public async Task When_nothing_helps_it_says_so()
    {
        var probe = new FakeProbe(_launcher) { Opens = (_, url) => url == TrialTargets.Control };

        var report = await NewTrial(probe).RunAsync([Discord], null, CancellationToken.None);

        report.Conclusion.Should().Be(TrialConclusion.NothingHelped);
        report.Message.Should().Be(Strings.Get(Strings.TrialNothingHelped));
    }

    [Fact]
    public async Task If_the_first_start_fails_the_trial_stops_with_a_hint_about_antivirus()
    {
        _launcher.FailStart = true;
        var probe = new FakeProbe(_launcher) { Opens = (_, url) => url == TrialTargets.Control };

        var report = await NewTrial(probe).RunAsync([Discord], null, CancellationToken.None);

        report.Conclusion.Should().Be(TrialConclusion.CouldNotStart);
        report.Results.Should().ContainSingle();
        Directory.Exists(_trialFolder).Should().BeFalse();
    }

    [Fact]
    public async Task A_program_that_exits_by_itself_counts_as_not_started()
    {
        _launcher.ExitImmediately = true;
        var probe = new FakeProbe(_launcher) { Opens = (_, url) => url == TrialTargets.Control };

        var report = await NewTrial(probe).RunAsync([Discord], null, CancellationToken.None);

        report.Conclusion.Should().Be(TrialConclusion.CouldNotStart);
        report.Results[0].Status.Should().Be(TrialStatus.FailedToStart);
    }

    [Fact]
    public async Task A_blocker_stops_the_trial_before_anything_runs()
    {
        var probe = new FakeProbe(_launcher);

        var report = await NewTrial(probe, () => "başka GoodbyeDPI çalışıyor").RunAsync([Discord], null, CancellationToken.None);

        report.Conclusion.Should().Be(TrialConclusion.Blocked);
        report.Message.Should().Contain("başka GoodbyeDPI");
        _launcher.Arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task Files_that_fail_the_hash_check_stop_the_trial()
    {
        _payload.Tamper = true;
        var probe = new FakeProbe(_launcher) { Opens = (_, url) => url == TrialTargets.Control };

        var report = await NewTrial(probe).RunAsync([Discord], null, CancellationToken.None);

        report.Conclusion.Should().Be(TrialConclusion.CouldNotStart);
        report.Message.Should().Contain("WinDivert.dll");
        _launcher.Arguments.Should().BeEmpty();
    }

    // ---------- install ----------

    [Fact]
    public async Task The_preview_names_the_setting_the_folder_and_the_driver_warning()
    {
        var fix = NewFix();
        fix.Configure("turkey-1");

        var preview = await fix.PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeTrue();
        preview.Changes.Should().Contain(l => l.Contains("Standart"));
        preview.Changes.Should().Contain(l => l.Contains(_folder));
        preview.Changes.Should().Contain(l => l.Contains("Yandex"));
        preview.Changes.Should().Contain(Strings.Get(Strings.GdpiPreviewDriver));
        Directory.Exists(_folder).Should().BeFalse("previewing changes nothing");
    }

    [Fact]
    public async Task Without_a_chosen_setting_there_is_nothing_to_do()
    {
        var preview = await NewFix().PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Service_mode_writes_the_journal_first_then_creates_and_starts_the_service()
    {
        var fix = NewFix();
        var journal = NewJournal();
        fix.Configure("turkey-2");

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        var config = _services.Configs[GoodbyeDpiLayout.ServiceName];
        config.BinaryPath.Should().Be($"\"{Path.Combine(_folder, "goodbyedpi.exe")}\" -5");
        config.StartType.Should().Be(2, "automatic");
        _services.Started.Should().Contain(GoodbyeDpiLayout.ServiceName);
        (await journal.LoadAsync(CancellationToken.None)).Should().ContainSingle(r => r.FixId == GoodbyeDpiInstallFix.FixIdValue);
        GoodbyeDpiState.Read(_folder).Should().Be(new GoodbyeDpiState("turkey-2"));
        GoodbyeDpiFiles.FindProblems(_folder, _payload.Hashes).Should().BeEmpty();
    }

    [Fact]
    public async Task A_service_that_does_not_start_is_rolled_back_completely()
    {
        _services.StartSucceeds = false;
        var fix = NewFix();
        var journal = NewJournal();
        fix.Configure("turkey-1");

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Be(Strings.Get(Strings.GdpiServiceFailed));
        _services.Configs.Should().BeEmpty();
        Directory.Exists(_folder).Should().BeFalse();
        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty("there is nothing left to undo");
    }

    [Fact]
    public async Task Tampered_files_stop_the_install_before_anything_runs()
    {
        _payload.Tamper = true;
        var fix = NewFix();
        var journal = NewJournal();
        fix.Configure("turkey-1");

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("WinDivert.dll");
        _services.Configs.Should().BeEmpty();
        _launcher.Arguments.Should().BeEmpty();
        Directory.Exists(_folder).Should().BeFalse();
        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Someone_elses_running_GoodbyeDPI_blocks_the_install()
    {
        _machine.Services.Add(new ServiceEntry("GoodbyeDPI", @"""C:\GoodbyeDPI\x86_64\goodbyedpi.exe"" -5", IsDriver: false, IsRunning: true));
        var fix = NewFix();
        fix.Configure("turkey-1");

        var preview = await fix.PreviewAsync(CancellationToken.None);
        var result = await fix.ApplyAsync(NewJournal(), CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        preview.Changes.Should().Equal(Strings.Get(Strings.GdpiBlockedForeign));
        result.Success.Should().BeFalse();
        _services.Configs.Should().BeEmpty();
    }

    [Fact]
    public async Task FoxFixs_own_service_is_not_reported_as_a_leftover_or_as_someone_elses()
    {
        var own = $"\"{Path.Combine(_folder, "goodbyedpi.exe")}\" -5";
        _machine.Services.Add(new ServiceEntry(GoodbyeDpiLayout.ServiceName, own, IsDriver: false, IsRunning: true));
        _machine.Services.Add(new ServiceEntry("WinDivert", Path.Combine(_folder, "WinDivert64.sys"), IsDriver: true, IsRunning: true));
        var detector = new DpiResidueDetector(_machine, _machine, _machine, _machine, [], _root);

        detector.Scan().Should().BeEmpty();

        var status = new GoodbyeDpiStatusReader(_machine, _launcher, detector, _folder).Read();
        status.ServiceInstalled.Should().BeTrue();
        status.ServiceRunning.Should().BeTrue();
        status.OtherRunning.Should().BeFalse();
    }

    [Fact]
    public async Task The_same_service_outside_FoxFixs_folder_is_still_found()
    {
        _machine.Services.Add(new ServiceEntry("GoodbyeDPI", @"""C:\GoodbyeDPI\x86_64\goodbyedpi.exe"" -5", IsDriver: false, IsRunning: true));
        var detector = new DpiResidueDetector(_machine, _machine, _machine, _machine, [], _root);

        detector.Scan().Should().Contain(f => f.RuleId == "goodbyedpi" && f.IsActive);
    }

    [Fact]
    public async Task Undo_removes_the_service_the_driver_registration_and_the_folder()
    {
        var fix = NewFix();
        var journal = NewJournal();
        fix.Configure("turkey-1");
        await fix.ApplyAsync(journal, CancellationToken.None);
        _services.Configs["WinDivert"] = new ServiceConfig("WinDivert", "WinDivert", Path.Combine(_folder, "WinDivert64.sys"), 3, 1, true);
        _services.Configs["WinDivert14"] = new ServiceConfig("WinDivert14", "x", @"C:\Other\WinDivert64.sys", 3, 1, true);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();

        await fix.UndoAsync(record, CancellationToken.None);

        _services.Configs.Should().ContainKey("WinDivert14", "a driver registered from another folder is not ours to remove");
        _services.Configs.Should().NotContainKey(GoodbyeDpiLayout.ServiceName).And.NotContainKey("WinDivert");
        Directory.Exists(_folder).Should().BeFalse();
    }

    [Fact]
    public async Task Installing_again_replaces_the_old_install_instead_of_stacking()
    {
        var fix = NewFix();
        fix.Configure("turkey-1");
        await fix.ApplyAsync(NewJournal(), CancellationToken.None);
        _machine.Services.Add(new ServiceEntry(GoodbyeDpiLayout.ServiceName, "x", false, true));

        fix.Configure("turkey-2");
        var preview = await fix.PreviewAsync(CancellationToken.None);
        var result = await fix.ApplyAsync(NewJournal(), CancellationToken.None);

        preview.Changes.Should().Contain(Strings.Get(Strings.GdpiPreviewReplace));
        result.Success.Should().BeTrue();
        _services.Configs[GoodbyeDpiLayout.ServiceName].BinaryPath.Should().EndWith("\" -5");
    }

    [Fact]
    public void Every_profile_has_a_plain_title_and_the_DNS_ones_say_so()
    {
        GoodbyeDpiProfiles.All.Select(p => p.Id).Should().OnlyHaveUniqueItems();
        GoodbyeDpiProfiles.All.Should().OnlyContain(p => p.Title.Length > 3);
        GoodbyeDpiProfiles.All.Where(p => p.RedirectsDns).Should().OnlyContain(p => p.Arguments.Contains("--dns-addr 77.88.8.8 --dns-port 1253"));
        GoodbyeDpiProfiles.All.Where(p => !p.RedirectsDns).Should().OnlyContain(p => !p.Arguments.Contains("--dns"));
    }

    // ---------- DNS check ----------

    private sealed class RedirectedDns : IDnsProbe
    {
        public Task<bool> ResolveWithSystemAsync(string host, TimeSpan timeout, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> QueryUdpAsync(IPAddress server, string host, TimeSpan timeout, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> QueryTcpAsync(IPAddress server, string host, TimeSpan timeout, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<IPAddress>> ResolveAddressesAsync(string host, TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([]);
    }

    [Fact]
    public async Task A_DNS_redirect_is_only_a_warning_when_FoxFix_did_not_set_it_up()
    {
        (await new DnsCheck(new RedirectedDns()).RunAsync(CancellationToken.None)).Status.Should().Be(CheckStatus.Warning);
        (await new DnsCheck(new RedirectedDns(), () => true).RunAsync(CancellationToken.None)).Status.Should().Be(CheckStatus.Passed);
    }

    // ---------- drivers are registered as \??\C:\... ----------

    [Fact]
    public void A_driver_path_in_its_NT_form_is_still_recognised_as_inside_the_folder()
    {
        GoodbyeDpiLayout.IsManaged(@"\??\" + Path.Combine(_trialFolder, "WinDivert64.sys"), GoodbyeDpiLayout.OwnRoot(_folder)).Should().BeTrue();
        GoodbyeDpiLayout.IsManaged(@"\??\C:\Other\WinDivert64.sys", GoodbyeDpiLayout.OwnRoot(_folder)).Should().BeFalse();
    }

    [Fact]
    public async Task A_driver_left_loaded_from_the_trial_copy_is_stopped_and_removed_with_the_next_teardown()
    {
        _services.Configs["WinDivert"] = new ServiceConfig("WinDivert", "WinDivert", @"\??\" + Path.Combine(_trialFolder, "WinDivert64.sys"), 4, 1, true);
        _services.Configs["WinDivert14"] = new ServiceConfig("WinDivert14", "x", @"\??\C:\Other\WinDivert64.sys", 3, 1, true);

        await new GoodbyeDpiTeardown(_services, _launcher, _folder).RemoveAsync(deleteFolder: true, CancellationToken.None);

        _services.Configs.Should().NotContainKey("WinDivert");
        _services.Configs.Should().ContainKey("WinDivert14", "someone else's driver stays");
    }

    [Fact]
    public async Task A_leftover_driver_of_FoxFix_alone_counts_as_something_to_remove_but_not_as_someone_elses_tool()
    {
        _machine.Services.Add(new ServiceEntry("WinDivert", @"\??\" + Path.Combine(_trialFolder, "WinDivert64.sys"), IsDriver: true, IsRunning: true));
        var detector = new DpiResidueDetector(_machine, _machine, _machine, _machine, [], GoodbyeDpiLayout.OwnRoot(_folder));
        var status = new GoodbyeDpiStatusReader(_machine, _launcher, detector, _folder);

        var read = status.Read();

        read.OwnDriverRegistered.Should().BeTrue();
        read.OtherRunning.Should().BeFalse();
        detector.Scan().Should().BeEmpty("it is FoxFix's own, not a leftover of an old tool");

        var fix = new GoodbyeDpiRemoveFix(status, new GoodbyeDpiTeardown(_services, _launcher, _folder), _folder);
        (await fix.PreviewAsync(CancellationToken.None)).HasChanges.Should().BeTrue("the driver can still be taken out");
    }
}