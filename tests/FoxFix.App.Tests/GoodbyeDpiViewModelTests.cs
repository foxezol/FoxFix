using System.Security.Cryptography;
using FluentAssertions;
using FoxFix.App;
using FoxFix.Core;
using FoxFix.Core.Dpi;
using FoxFix.Core.Fixes;
using FoxFix.Core.GoodbyeDpi;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class GoodbyeDpiViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "foxfix-gdpivm-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;

    private readonly Payload _payload = new();
    private readonly Launcher _launcher = new();
    private readonly Services _services = new();
    private readonly Machine _machine = new();
    private readonly Probe _probe;
    private readonly Elevation _elevation = new();
    private readonly NoticeViewModel _notice = new();
    private DpiChoiceStore? _choice;
    private FoxDpiAutoStart? _autoStart;
    private FoxDpiAutoStartFix? _autoStartFix;
    private readonly Schtasks _schtasks = new();

    public GoodbyeDpiViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _folder = Path.Combine(_root, "GoodbyeDPI");
        _probe = new Probe(_launcher);
        SynchronizationContext.SetSynchronizationContext(null);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class Elevation : IElevation
    {
        public bool IsElevated { get; set; } = true;

        public string? Relaunched { get; private set; }

        public bool RestartElevated(string? arguments = null)
        {
            Relaunched = arguments;
            return true;
        }
    }

    private sealed class Stub(string id) : IFix
    {
        public string Id => id;

        public string Title => "Başlık " + id;

        public bool IncludedInRepair => false;

        public bool RequiresAdmin => false;

        public Task<FixPreview> PreviewAsync(CancellationToken ct) => Task.FromResult(new FixPreview(["değişiklik " + id]));

        public Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct) => Task.FromResult(new FixResult(true, "ok"));

        public Task UndoAsync(UndoRecord record, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoReboot : IRebootService
    {
        public Task<bool> ScheduleRestartAsync(int delaySeconds, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> CancelAsync(CancellationToken ct) => Task.FromResult(true);
    }

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

        private static byte[] Content(string name) => System.Text.Encoding.UTF8.GetBytes("x:" + name);
    }

    private sealed class Proc(Launcher owner) : IGoodbyeDpiProcess
    {
        private bool _stopped;

        public bool HasExited => _stopped;

        public Task StopAsync()
        {
            _stopped = true;
            owner.Running = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class Launcher : IGoodbyeDpiLauncher
    {
        public List<string> Arguments { get; } = [];

        public IGoodbyeDpiProcess Start(string exePath, string arguments)
        {
            Arguments.Add(arguments);
            Running = true;
            return new Proc(this);
        }

        public Task<int> StopAllInFolderAsync(string folder, CancellationToken ct) => Task.FromResult(0);

        public bool Running { get; set; }

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

        public Task<bool> CreateAsync(ServiceConfig config, CancellationToken ct)
        {
            Configs[config.Name] = config;
            return Task.FromResult(true);
        }

        public Task<bool> StartAsync(string name, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Machine : IServiceSource, IProcessSource, IPathProbe, INetworkInfoProvider
    {
        public List<ServiceEntry> Services { get; } = [];

        public IReadOnlyList<ServiceEntry> GetServicesAndDrivers() => Services;

        public IReadOnlyList<ProcessEntry> GetProcesses() => [];

        public bool DirectoryExists(string path) => false;

        public IReadOnlyList<AdapterInfo> GetAdapters() => [];
    }

    private sealed class Probe(Launcher launcher) : IHttpsProbe
    {
        public Func<string, Uri, bool> Opens { get; set; } = (_, _) => false;

        public Task<HttpsProbeOutcome> ProbeAsync(Uri url, TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult(Opens(launcher.Arguments.LastOrDefault() ?? "", url) ? HttpsProbeOutcome.Ok : HttpsProbeOutcome.TlsFailed);
    }

    private sealed class Schtasks : Core.Commands.ICommandRunner
    {
        public bool Exists { get; set; }

        public Task<Core.Commands.CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            switch (arguments[0])
            {
                case "/Create":
                    Exists = true;
                    return Task.FromResult(new Core.Commands.CommandResult(0, "", ""));
                case "/Delete":
                    Exists = false;
                    return Task.FromResult(new Core.Commands.CommandResult(0, "", ""));
                default:
                    return Task.FromResult(new Core.Commands.CommandResult(Exists ? 0 : 1, "", ""));
            }
        }
    }

    private void UseAutoStart()
    {
        _autoStart = new FoxDpiAutoStart(_schtasks);
        _autoStartFix = new FoxDpiAutoStartFix(_autoStart, () => @"C:\FoxFix\FoxFix.exe", () => @"PC\kullanici");
    }

    private GoodbyeDpiViewModel Create(out FixFlowViewModel flow)
    {
        var detector = new DpiResidueDetector(_machine, _machine, _machine, _machine, [], _root);
        var status = new GoodbyeDpiStatusReader(_machine, _launcher, detector, _folder);
        var install = new GoodbyeDpiInstallFix(_payload, _launcher, _services, status, _folder, _payload.Hashes);
        var trial = new GoodbyeDpiTrial(
            _payload, _launcher, _probe, _services, _folder + "-trial", () => null, _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask, _payload.Hashes);
        var teardown = new GoodbyeDpiTeardown(_services, _launcher, _folder);
        var foxDpi = new FoxDpiSession(_payload, _launcher, status, _folder, (_, _) => Task.CompletedTask, _payload.Hashes);
        flow = new FixFlowViewModel(
            [new Stub(DpiCleanupFix.FixId), new GoodbyeDpiRemoveFix(status, teardown, _folder), new Stub(DnsResetFix.FixIdValue), install, .. _autoStartFix is null ? Array.Empty<IFix>() : new IFix[] { _autoStartFix }],
            new UndoJournal(Path.Combine(_root, "undo.json")), _elevation, new NoReboot());
        return new GoodbyeDpiViewModel(trial, install, status, teardown, foxDpi, flow, _elevation, _choice, _notice, _autoStart, _autoStartFix);
    }

    [Fact]
    public void It_starts_with_the_first_setting_selected_and_every_row_waiting()
    {
        var vm = Create(out _);

        vm.Rows.Should().HaveCount(GoodbyeDpiProfiles.All.Count);
        vm.Rows.Count(r => r.IsSelected).Should().Be(1);
        vm.Rows[0].IsSelected.Should().BeTrue();
        vm.SelectedProfile.Id.Should().Be("turkey-1");
        vm.Rows.Should().OnlyContain(r => r.StatusText == "Sırada");
    }

    [Fact]
    public void Picking_a_row_selects_that_setting_and_only_that_one()
    {
        var vm = Create(out _);

        vm.Rows[3].IsSelected = true;

        vm.SelectedProfile.Id.Should().Be(vm.Rows[3].Profile.Id);
        vm.Rows.Count(r => r.IsSelected).Should().Be(1);
    }

    [Fact]
    public void Nothing_can_be_started_without_administrator_rights_and_the_page_can_ask_for_them()
    {
        _elevation.IsElevated = false;
        var vm = Create(out _);

        vm.NeedsElevation.Should().BeTrue();
        vm.RunTrialCommand.CanExecute(null).Should().BeFalse();
        vm.InstallServiceCommand.CanExecute(null).Should().BeFalse();

        vm.ElevateCommand.Execute(null);

        _elevation.Relaunched.Should().Be("--sayfa=dpi", "the relaunch comes back to this page");
    }

    [Fact]
    public async Task The_trial_fills_the_rows_and_selects_the_best_setting()
    {
        _probe.Opens = (args, url) => url == TrialTargets.Control || args.StartsWith("-9 --dns-addr");
        var vm = Create(out _);

        await vm.RunTrialCommand.ExecuteAsync(null);

        vm.ResultText.Should().Contain("En uygun ayar");
        vm.SelectedProfile.Id.Should().Be("turkey-5");
        vm.Rows.Single(r => r.IsBest).Profile.Id.Should().Be("turkey-5");
        vm.Rows.Single(r => r.IsSelected).Profile.Id.Should().Be("turkey-5");
        vm.Rows.Single(r => r.Profile.Id == "turkey-5").Kind.Should().Be("Ok");
        vm.Rows.Single(r => r.Profile.Id == "turkey-1").Kind.Should().Be("Bad");
        vm.IsTesting.Should().BeFalse();
    }

    [Fact]
    public async Task A_site_list_without_a_valid_name_does_not_start_anything()
    {
        var vm = Create(out _);
        vm.Hosts = "???";

        await vm.RunTrialCommand.ExecuteAsync(null);

        vm.ResultText.Should().Contain("site adı");
        _launcher.Arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task Install_goes_through_the_preview_and_does_not_change_anything_before_confirmation()
    {
        var vm = Create(out var flow);
        vm.Rows[1].IsSelected = true;

        await vm.InstallServiceCommand.ExecuteAsync(null);

        flow.Stage.Should().Be(FlowStage.AwaitingConfirmation);
        flow.PreviewLines.Select(l => l.Text).Should().Contain(l => l.Contains(vm.SelectedProfile.Title));
        _services.Configs.Should().BeEmpty();
        Directory.Exists(_folder).Should().BeFalse();

        flow.IsConfirmed = true;
        await flow.ApplyCommand.ExecuteAsync(null);

        _services.Configs.Should().ContainKey(GoodbyeDpiLayout.ServiceName);
        _services.Configs[GoodbyeDpiLayout.ServiceName].BinaryPath.Should().EndWith(vm.SelectedProfile.Arguments);
        await vm.PendingRefresh;
    }


    [Fact]
    public async Task FoxDPI_start_without_administrator_rights_restarts_the_app_with_the_FoxDPI_argument()
    {
        _elevation.IsElevated = false;
        var vm = Create(out _);
        var started = new List<GoodbyeDpiProfile>();
        vm.FoxDpiStarted += started.Add;

        await vm.ToggleFoxDpiCommand.ExecuteAsync(null);

        _elevation.Relaunched.Should().Be("--foxdpi");
        started.Should().BeEmpty();
        _launcher.Arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task FoxDPI_start_runs_the_chosen_setting_opens_its_window_and_explains_it_in_a_pop_up()
    {
        var vm = Create(out _);
        var started = new List<GoodbyeDpiProfile>();
        vm.FoxDpiStarted += started.Add;
        vm.Rows[4].IsSelected = true;

        vm.FoxDpiButtonText.Should().Be("FoxDPI'yı başlat");
        await vm.ToggleFoxDpiCommand.ExecuteAsync(null);

        started.Should().ContainSingle().Which.Id.Should().Be(vm.Rows[4].Profile.Id);
        _launcher.Arguments.Should().Equal(vm.Rows[4].Profile.Arguments);
        _notice.IsOpen.Should().BeTrue("every start from FoxFix explains how FoxDPI works");
        _notice.Title.Should().Be(Strings.Get(Strings.FoxDpiExplainTitle));
        _notice.Message.Should().Contain("FoxDPI.exe");
        vm.IsActive.Should().BeTrue();
        vm.FoxDpiButtonText.Should().Be("Pencereyi göster", "there is no stop button; the window is closed to stop");
    }

    [Fact]
    public async Task Pressing_the_button_while_FoxDPI_runs_only_brings_its_window_forward()
    {
        var vm = Create(out _);
        var started = new List<GoodbyeDpiProfile>();
        vm.FoxDpiStarted += started.Add;
        await vm.ToggleFoxDpiCommand.ExecuteAsync(null);

        await vm.ToggleFoxDpiCommand.ExecuteAsync(null);

        started.Should().HaveCount(2, "the second press shows the window again");
        _launcher.Running.Should().BeTrue("nothing was stopped");
        _launcher.Arguments.Should().HaveCount(1, "and nothing was started a second time");
    }

    [Fact]
    public async Task While_the_service_runs_GoodbyeDPI_the_start_button_has_nothing_to_do()
    {
        _machine.Services.Add(new ServiceEntry(GoodbyeDpiLayout.ServiceName, Path.Combine(_folder, "goodbyedpi.exe"), IsDriver: false, IsRunning: true));
        var vm = Create(out _);

        await vm.RefreshStatusAsync();

        vm.IsActive.Should().BeTrue();
        vm.FoxDpiButtonText.Should().Be("Açık");
        vm.ToggleFoxDpiCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Opening_FoxFix_saves_the_setting_once_so_FoxDPI_exe_can_start_on_its_own()
    {
        var file = Path.Combine(_root, "first-run.txt");
        _choice = new DpiChoiceStore(file);
        var vm = Create(out _);
        File.Exists(file).Should().BeFalse("choosing nothing saves nothing");

        vm.RememberChoice();
        File.ReadAllText(file).Should().Be("turkey-1");

        vm.Rows[3].IsSelected = true;
        vm.RememberChoice();
        File.ReadAllText(file).Should().Be(vm.Rows[3].Profile.Id, "the later choice stays, the first run does not overwrite it");
    }

    [Fact]
    public void FoxDPI_exe_refuses_to_start_before_FoxFix_has_been_opened_once_and_says_so()
    {
        var (profile, error) = DirectStart.Resolve(null);

        profile.Should().BeNull();
        error.Should().Contain("FoxFix'i bir kez açman");
        DirectStart.Resolve("unknown-id").Profile.Should().BeNull();
    }

    [Fact]
    public void With_a_saved_setting_FoxDPI_exe_starts_with_that_setting()
    {
        var (profile, error) = DirectStart.Resolve("turkey-5");

        profile!.Id.Should().Be("turkey-5");
        error.Should().BeNull();
        DirectStart.Argument.Should().Be("--sadece-foxdpi");
    }
    [Fact]
    public async Task A_start_that_cannot_happen_is_explained_in_a_pop_up_in_the_middle_of_the_window()
    {
        _machine.Services.Add(new ServiceEntry("GoodbyeDPI", @"""C:\GoodbyeDPI\goodbyedpi.exe"" -5", IsDriver: false, IsRunning: true));
        var vm = Create(out _);
        var started = new List<GoodbyeDpiProfile>();
        vm.FoxDpiStarted += started.Add;

        await vm.ToggleFoxDpiCommand.ExecuteAsync(null);

        started.Should().BeEmpty();
        _notice.IsOpen.Should().BeTrue();
        _notice.Title.Should().Be("FoxDPI başlamadı");
        _notice.Message.Should().Be(Strings.Get(Strings.GdpiBlockedForeign));
    }

    [Fact]
    public async Task The_remove_service_button_shows_only_while_the_service_is_installed_and_asks_before_removing()
    {
        var vm = Create(out var flow);
        vm.IsServiceInstalled.Should().BeFalse();
        vm.RemoveServiceCommand.CanExecute(null).Should().BeFalse();

        _machine.Services.Add(new ServiceEntry(GoodbyeDpiLayout.ServiceName, Path.Combine(_folder, "goodbyedpi.exe"), IsDriver: false, IsRunning: false));
        await vm.RefreshStatusAsync();

        vm.IsServiceInstalled.Should().BeTrue();
        vm.RemoveServiceCommand.CanExecute(null).Should().BeTrue();

        await vm.RemoveServiceCommand.ExecuteAsync(null);

        flow.Stage.Should().Be(FlowStage.AwaitingConfirmation, "removing goes through the preview first");
        flow.PreviewLines.Select(l => l.Text).Should().Contain(l => l.Contains("FoxDPI"));
        await vm.PendingRefresh;
    }

    [Fact]
    public async Task While_the_service_is_installed_the_setting_is_locked_and_removing_it_unlocks_again()
    {
        var vm = Create(out _);
        vm.CanChangeSetting.Should().BeTrue();
        vm.RunTrialCommand.CanExecute(null).Should().BeTrue();

        var service = new ServiceEntry(GoodbyeDpiLayout.ServiceName, Path.Combine(_folder, "goodbyedpi.exe"), IsDriver: false, IsRunning: true);
        _machine.Services.Add(service);
        await vm.RefreshStatusAsync();

        vm.IsServiceInstalled.Should().BeTrue();
        vm.CanChangeSetting.Should().BeFalse("the installed service runs one fixed setting");
        vm.RunTrialCommand.CanExecute(null).Should().BeFalse();

        _machine.Services.Remove(service);
        await vm.RefreshStatusAsync();

        vm.CanChangeSetting.Should().BeTrue();
        vm.RunTrialCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void A_notice_opens_with_its_text_and_closes_again()
    {
        var notice = new NoticeViewModel();

        notice.Show("Başlık", "Mesaj");
        notice.IsOpen.Should().BeTrue();
        notice.Title.Should().Be("Başlık");
        notice.Message.Should().Be("Mesaj");

        notice.CloseCommand.Execute(null);
        notice.IsOpen.Should().BeFalse();
    }
    [Fact]
    public async Task The_chosen_setting_is_remembered_for_the_next_start()
    {
        var file = Path.Combine(_root, "choice.txt");
        _choice = new DpiChoiceStore(file);
        var first = Create(out _);

        first.Rows[5].IsSelected = true;
        var second = Create(out _);

        File.ReadAllText(file).Should().Be(first.Rows[5].Profile.Id);
        second.SelectedProfile.Id.Should().Be(first.Rows[5].Profile.Id);
        second.Rows.Single(r => r.IsSelected).Profile.Id.Should().Be(first.Rows[5].Profile.Id);
    }

    [Fact]
    public async Task DPI_reset_shows_one_preview_for_old_tools_FoxFixs_own_install_and_DNS()
    {
        var vm = Create(out var flow);

        await vm.ResetCommand.ExecuteAsync(null);

        flow.Stage.Should().Be(FlowStage.AwaitingConfirmation);
        flow.PreviewLines.Where(l => l.IsHeader).Select(l => l.Text).Should().Contain(["Başlık dpi-cleanup", "Başlık dns-reset"]);
        flow.PreviewLines.Select(l => l.Text).Should().NotContain(l => l.Contains(GoodbyeDpiInstallFix.FixIdValue));
        await vm.PendingRefresh;
    }

    [Fact]
    public async Task The_start_with_Windows_box_follows_the_scheduled_task_and_asks_before_changing_it()
    {
        UseAutoStart();
        var vm = Create(out var flow);
        await vm.RefreshStatusAsync();
        vm.IsAutoStart.Should().BeFalse();

        await vm.ToggleAutoStartCommand.ExecuteAsync(null);

        flow.Stage.Should().Be(FlowStage.AwaitingConfirmation, "a scheduled task is a change to the system, so it is previewed first");
        flow.PreviewLines.Select(l => l.Text).Should().Contain(l => l.Contains(FoxDpiAutoStart.TaskName));
        _schtasks.Exists.Should().BeFalse();

        flow.IsConfirmed = true;
        await flow.ApplyCommand.ExecuteAsync(null);
        await vm.PendingRefresh;

        _schtasks.Exists.Should().BeTrue();
        vm.IsAutoStart.Should().BeTrue();
    }

    [Fact]
    public async Task Ticking_the_box_again_turns_start_with_Windows_off()
    {
        UseAutoStart();
        _schtasks.Exists = true;
        var vm = Create(out var flow);
        await vm.RefreshStatusAsync();
        vm.IsAutoStart.Should().BeTrue();

        await vm.ToggleAutoStartCommand.ExecuteAsync(null);
        flow.PreviewLines.Select(l => l.Text).Should().Contain(l => l.Contains(FoxDpiAutoStart.TaskName));
        flow.IsConfirmed = true;
        await flow.ApplyCommand.ExecuteAsync(null);
        await vm.PendingRefresh;

        _schtasks.Exists.Should().BeFalse();
        vm.IsAutoStart.Should().BeFalse();
    }

    [Fact]
    public void Start_with_Windows_needs_administrator_rights()
    {
        UseAutoStart();
        _elevation.IsElevated = false;
        var vm = Create(out _);

        vm.ToggleAutoStartCommand.CanExecute(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("GoodbyeDpi.", "goodbyedpi.exe")]
    public void The_files_inside_the_app_match_their_pinned_hashes(string prefix, string exe)
    {
        var folder = Path.Combine(_root, "embedded-" + prefix.TrimEnd('.'));
        Directory.CreateDirectory(folder);
        new EmbeddedGoodbyeDpiPayload(prefix).WriteTo(folder);

        var hashes = GoodbyeDpiFiles.Sha256;

        File.Exists(Path.Combine(folder, exe)).Should().BeTrue();
        GoodbyeDpiFiles.FindProblems(folder, hashes).Should().BeEmpty();
    }

}