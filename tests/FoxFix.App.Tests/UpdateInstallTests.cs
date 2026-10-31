using System.Security.Cryptography;
using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.GoodbyeDpi;
using FoxFix.Core.Updates;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class UpdateInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "foxfix-install-vm-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _events = [];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Source(ReleaseInfo release) : IReleaseSource
    {
        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct) => Task.FromResult<ReleaseInfo?>(release);
    }

    private sealed class Downloader(Func<byte[]> content, Action? onStart = null) : IPackageDownloader
    {
        public Task DownloadAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct)
        {
            onStart?.Invoke();
            File.WriteAllBytes(destination, content());
            progress?.Report(0.5);
            return Task.CompletedTask;
        }
    }

    private static readonly byte[] Package = [1, 2, 3, 4, 5];

    private static ReleaseInfo Release(string? sha = null) => new(
        new Version(0, 9, 16), "v0.9.16", "https://github.com/foxezol/FoxFix/releases/tag/v0.9.16", "",
        [new ReleaseAsset(
            "FoxFixSetup.exe", "https://github.com/foxezol/FoxFix/releases/download/v0.9.16/FoxFixSetup.exe",
            Package.Length, sha ?? Convert.ToHexString(SHA256.HashData(Package)))]);

    private UpdateViewModel Create(
        UninstallKind kind = UninstallKind.Installed,
        Func<byte[]>? content = null,
        bool finisherStarts = true,
        string? sha = null,
        bool withUpdater = true)
    {
        var updater = withUpdater
            ? new SelfUpdater(new Downloader(content ?? (() => Package), () => _events.Add("download")), kind, Path.Combine(_root, "program"), Path.Combine(_root, "FoxFix", "guncelleme"))
            : null;

        return new UpdateViewModel(
            new UpdateChecker(new Source(Release(sha))),
            new Version(0, 9, 15),
            _ => _events.Add("page"),
            log: message => _events.Add("log:" + message),
            updater,
            stopFoxDpi: () =>
            {
                _events.Add("stop-foxdpi");
                return Task.CompletedTask;
            },
            startFinisher: command =>
            {
                _events.Add("start:" + command);
                return finisherStarts;
            },
            exitApp: () => _events.Add("exit"));
    }

    [Fact]
    public async Task Without_an_updater_the_pop_up_can_only_open_the_release_page()
    {
        var vm = Create(withUpdater: false);

        await vm.RunStartupCheckAsync();

        vm.HasUpdate.Should().BeTrue();
        vm.CanInstall.Should().BeFalse();
        vm.InstallCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_copy_of_unknown_kind_cannot_install_itself()
    {
        var vm = Create(UninstallKind.Unknown);

        await vm.RunStartupCheckAsync();

        vm.CanInstall.Should().BeFalse();
    }

    [Fact]
    public async Task A_release_whose_file_has_no_hash_cannot_be_installed_by_the_app()
    {
        var vm = Create(sha: "yok");

        await vm.RunStartupCheckAsync();

        vm.CanInstall.Should().BeFalse();
    }

    [Fact]
    public async Task Install_downloads_stops_FoxDPI_starts_the_command_and_then_closes_in_that_order()
    {
        var vm = Create();
        await vm.RunStartupCheckAsync();
        vm.CanInstall.Should().BeTrue();

        await vm.InstallCommand.ExecuteAsync(null);

        _events.Should().HaveCount(4);
        _events[0].Should().Be("download");
        _events[1].Should().Be("stop-foxdpi");
        _events[2].Should().StartWith("start:").And.Contain("/VERYSILENT").And.Contain("/YENIDEN=1");
        _events[3].Should().Be("exit");
        vm.HasInstallError.Should().BeFalse();
        vm.InstallPercent.Should().Be(100);
        vm.InstallText.Should().Contain("kapanacak");
        vm.IsInstalling.Should().BeFalse();
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_hash_stops_everything_and_says_why()
    {
        var vm = Create(content: () => [9, 9, 9, 9, 9]);
        await vm.RunStartupCheckAsync();

        await vm.InstallCommand.ExecuteAsync(null);

        _events.Should().NotContain("stop-foxdpi").And.NotContain("exit").And.NotContain(e => e.StartsWith("start:"));
        vm.InstallError.Should().StartWith("[FX-6004]");
        vm.HasInstallError.Should().BeTrue();
        _events.Should().Contain(e => e.StartsWith("log:[FX-6004]"));
        vm.IsInstalling.Should().BeFalse();
        vm.IsPromptOpen.Should().BeTrue("the person can still open the release page");
    }

    [Fact]
    public async Task A_command_that_cannot_be_started_leaves_FoxFix_open_and_says_so()
    {
        var vm = Create(finisherStarts: false);
        await vm.RunStartupCheckAsync();

        await vm.InstallCommand.ExecuteAsync(null);

        _events.Should().NotContain("exit");
        vm.InstallError.Should().Contain("başlatılamadı");
    }

    [Fact]
    public async Task While_installing_the_pop_up_cannot_be_closed_or_the_page_opened_or_a_second_install_started()
    {
        UpdateViewModel? seen = null;
        var gate = new TaskCompletionSource();
        var slow = new UpdateViewModel(
            new UpdateChecker(new Source(Release())), new Version(0, 9, 15), _ => { }, null,
            new SelfUpdater(new Downloader(() => Package, () => seen!.IsInstalling.Should().BeTrue()), UninstallKind.Installed, Path.Combine(_root, "program"), Path.Combine(_root, "FoxFix", "guncelleme")),
            stopFoxDpi: async () =>
            {
                seen!.ClosePromptCommand.CanExecute(null).Should().BeFalse();
                seen.OpenReleaseCommand.CanExecute(null).Should().BeFalse();
                seen.InstallCommand.CanExecute(null).Should().BeFalse();
                seen.CheckNowCommand.CanExecute(null).Should().BeFalse();
                await Task.CompletedTask;
                gate.SetResult();
            },
            startFinisher: _ => true,
            exitApp: () => { });
        seen = slow;
        await slow.RunStartupCheckAsync();

        await slow.InstallCommand.ExecuteAsync(null);

        gate.Task.IsCompletedSuccessfully.Should().BeTrue("the checks inside ran");
        slow.ClosePromptCommand.CanExecute(null).Should().BeTrue("it is free again afterwards");
    }

    [Fact]
    public async Task Install_does_nothing_when_there_is_no_update()
    {
        var vm = Create();

        await vm.InstallCommand.ExecuteAsync(null);

        _events.Should().BeEmpty();
    }
}
