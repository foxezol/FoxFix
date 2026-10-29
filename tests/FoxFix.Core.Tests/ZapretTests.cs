using System.Security.Cryptography;
using FluentAssertions;
using FoxFix.Core.GoodbyeDpi;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class ZapretTests
{
    [Fact]
    public void Every_Zapret_setting_belongs_to_Zapret_has_a_unique_id_and_a_readable_title()
    {
        ZapretProfiles.All.Should().HaveCount(7);
        ZapretProfiles.All.Should().OnlyContain(p => p.Engine == DpiEngines.ZapretId && !p.RedirectsDns);
        ZapretProfiles.All.Select(p => p.Id).Should().OnlyHaveUniqueItems();
        ZapretProfiles.All.Select(p => p.Title).Should().OnlyContain(t => t.Length > 2);
        ZapretProfiles.All.Select(p => p.Title).Should().Contain(["Türk Telekom", "Superonline", "Kablonet"]);
    }

    [Fact]
    public void Ids_do_not_clash_with_GoodbyeDPIs_so_one_choice_file_can_hold_either()
    {
        GoodbyeDpiProfiles.All.Select(p => p.Id).Intersect(ZapretProfiles.All.Select(p => p.Id)).Should().BeEmpty();
        GoodbyeDpiProfiles.Find("zapret-kablonet")!.Engine.Should().Be(DpiEngines.ZapretId);
        GoodbyeDpiProfiles.Find("turkey-1")!.Engine.Should().Be(DpiEngines.GoodbyeDpiId);
        GoodbyeDpiProfiles.Find("yok").Should().BeNull();
    }

    [Fact]
    public void Zapret_is_started_with_the_exclude_list_from_its_own_folder()
    {
        var profile = ZapretProfiles.All[0];

        var arguments = DpiEngines.Zapret.BuildArguments(@"C:\Veri\Zapret", profile);

        arguments.Should().StartWith(profile.Arguments).And.EndWith(@"--hostlist-exclude=""C:\Veri\Zapret\splitwire-exclude.txt""");
        DpiEngines.GoodbyeDpi.BuildArguments(@"C:\x", GoodbyeDpiProfiles.All[0]).Should().Be(GoodbyeDpiProfiles.All[0].Arguments);
        DpiEngines.Zapret.ExePath(@"C:\Veri\Zapret").Should().Be(@"C:\Veri\Zapret\winws.exe");
    }

    [Fact]
    public void The_pinned_Zapret_files_are_five_and_every_hash_is_a_sha256()
    {
        ZapretFiles.Sha256.Keys.Should().BeEquivalentTo("winws.exe", "WinDivert.dll", "WinDivert64.sys", "cygwin1.dll", "splitwire-exclude.txt");
        ZapretFiles.Sha256.Values.Should().OnlyContain(h => h.Length == 64 && h.All(Uri.IsHexDigit));
    }

    // ---------- the session starts either program ----------

    private sealed class Payload(string name) : IGoodbyeDpiPayload
    {
        public void WriteTo(string folder) => File.WriteAllText(Path.Combine(folder, name), "x");
    }

    private sealed class Proc : IGoodbyeDpiProcess
    {
        public bool HasExited => false;

        public Task StopAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class Launcher : IGoodbyeDpiLauncher
    {
        public List<(string Exe, string Args)> Starts { get; } = [];

        public IGoodbyeDpiProcess Start(string exePath, string arguments)
        {
            Starts.Add((exePath, arguments));
            return new Proc();
        }

        public Task<int> StopAllInFolderAsync(string folder, CancellationToken ct) => Task.FromResult(0);

        public bool IsRunningFromFolder(string folder) => false;
    }

    [Fact]
    public async Task A_Zapret_setting_starts_winws_from_the_Zapret_folder_and_a_GoodbyeDPI_setting_still_starts_goodbyedpi()
    {
        var root = Path.Combine(Path.GetTempPath(), "foxfix-zapret-" + Guid.NewGuid().ToString("N"));
        try
        {
            var goodbye = new Launcher();
            var zapret = new Launcher();
            var gdpiHashes = new Dictionary<string, string> { ["goodbyedpi.exe"] = Hash("x") };
            var zapretHashes = new Dictionary<string, string> { ["winws.exe"] = Hash("x") };
            var detector = new Dpi.DpiResidueDetector(new NoServices(), new NoProcesses(), new NoPaths(), new NoNetwork(), [], root);
            var status = new GoodbyeDpiStatusReader(new NoServices(), goodbye, detector, Path.Combine(root, "GoodbyeDPI"));
            var session = new FoxDpiSession(
                new Payload("goodbyedpi.exe"), goodbye, status, Path.Combine(root, "GoodbyeDPI"), (_, _) => Task.CompletedTask, gdpiHashes,
                [new DpiRuntime(DpiEngines.Zapret, new Payload("winws.exe"), zapret, Path.Combine(root, "Zapret"), zapretHashes)]);

            var result = await session.StartAsync(ZapretProfiles.All[0], CancellationToken.None);

            result.Started.Should().BeTrue();
            zapret.Starts.Should().ContainSingle().Which.Exe.Should().Be(Path.Combine(root, "Zapret", "winws.exe"));
            zapret.Starts[0].Args.Should().Contain("--dpi-desync=fake").And.Contain("splitwire-exclude.txt");
            goodbye.Starts.Should().BeEmpty();
            session.Profile.Should().Be(ZapretProfiles.All[0]);

            await session.StopAsync();
            (await session.StartAsync(GoodbyeDpiProfiles.All[0], CancellationToken.None)).Started.Should().BeTrue();
            goodbye.Starts.Should().ContainSingle().Which.Exe.Should().EndWith("goodbyedpi.exe");
            await session.StopAsync();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private sealed class NoServices : Dpi.IServiceSource
    {
        public IReadOnlyList<Dpi.ServiceEntry> GetServicesAndDrivers() => [];
    }

    private sealed class NoProcesses : Dpi.IProcessSource
    {
        public IReadOnlyList<Dpi.ProcessEntry> GetProcesses() => [];
    }

    private sealed class NoPaths : Dpi.IPathProbe
    {
        public bool DirectoryExists(string path) => false;
    }

    private sealed class NoNetwork : Network.INetworkInfoProvider
    {
        public IReadOnlyList<Network.AdapterInfo> GetAdapters() => [];
    }
}

public sealed class ZapretServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "foxfix-zsvc-" + Guid.NewGuid().ToString("N"));

    public ZapretServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class Payload : IGoodbyeDpiPayload
    {
        public void WriteTo(string folder) => File.WriteAllText(Path.Combine(folder, "winws.exe"), "x");
    }

    private sealed class Launcher : IGoodbyeDpiLauncher
    {
        public bool Running { get; set; }

        public IGoodbyeDpiProcess Start(string exePath, string arguments) => throw new NotSupportedException();

        public Task<int> StopAllInFolderAsync(string folder, CancellationToken ct) => Task.FromResult(0);

        public bool IsRunningFromFolder(string folder) => Running;
    }

    private sealed class Services : Dpi.IServiceManager, Dpi.IServiceSource
    {
        public Dictionary<string, Dpi.ServiceConfig> Configs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<Dpi.ServiceConfig?> ReadConfigAsync(string name, CancellationToken ct) => Task.FromResult(Configs.GetValueOrDefault(name));

        public Task<Dpi.StopOutcome> StopAsync(string name, CancellationToken ct) => Task.FromResult(Dpi.StopOutcome.Stopped);

        public Task<Dpi.DeleteOutcome> DeleteAsync(string name, CancellationToken ct)
        {
            Configs.Remove(name);
            return Task.FromResult(Dpi.DeleteOutcome.Deleted);
        }

        public Task<bool> CreateAsync(Dpi.ServiceConfig config, CancellationToken ct)
        {
            Configs[config.Name] = config;
            return Task.FromResult(true);
        }

        public Task<bool> StartAsync(string name, CancellationToken ct) => Task.FromResult(true);

        public IReadOnlyList<Dpi.ServiceEntry> GetServicesAndDrivers() =>
            Configs.Values.Select(c => new Dpi.ServiceEntry(c.Name, c.BinaryPath, c.IsDriver, IsRunning: true)).ToList();
    }

    private sealed class NoProcesses : Dpi.IProcessSource
    {
        public IReadOnlyList<Dpi.ProcessEntry> GetProcesses() => [];
    }

    private sealed class NoPaths : Dpi.IPathProbe
    {
        public bool DirectoryExists(string path) => false;
    }

    private sealed class NoNetwork : Network.INetworkInfoProvider
    {
        public IReadOnlyList<Network.AdapterInfo> GetAdapters() => [];
    }

    private (GoodbyeDpiInstallFix Fix, Services Services, Launcher Goodbye, string Folder) Create()
    {
        var services = new Services();
        var goodbye = new Launcher();
        var detector = new Dpi.DpiResidueDetector(services, new NoProcesses(), new NoPaths(), new NoNetwork(), [], _root);
        var status = new GoodbyeDpiStatusReader(services, goodbye, detector, Path.Combine(_root, "GoodbyeDPI"));
        var folder = Path.Combine(_root, "Zapret");
        var hashes = new Dictionary<string, string> { ["winws.exe"] = Convert.ToHexString(SHA256.HashData("x"u8.ToArray())) };
        var fix = new GoodbyeDpiInstallFix(
            new Payload(), new Launcher(), services, status, folder, hashes,
            DpiEngines.Zapret, ZapretLayout.ServiceName, ZapretLayout.InstallFixId);
        return (fix, services, goodbye, folder);
    }

    [Fact]
    public async Task The_Zapret_service_runs_winws_from_its_own_folder_with_the_setting_and_undo_takes_it_away()
    {
        var (fix, services, _, folder) = Create();
        var journal = new Safety.UndoJournal(Path.Combine(_root, "undo.json"));
        fix.Configure(ZapretProfiles.All[2]);
        fix.Id.Should().Be("zapret-install");

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        var config = services.Configs[ZapretLayout.ServiceName];
        config.BinaryPath.Should().StartWith($"\"{Path.Combine(folder, "winws.exe")}\"").And.Contain("--dpi-desync-fooling=md5sig").And.Contain("splitwire-exclude.txt");
        config.StartType.Should().Be(2);
        var record = (await journal.LoadAsync(CancellationToken.None)).Should().ContainSingle(r => r.FixId == "zapret-install").Subject;

        await fix.UndoAsync(record, CancellationToken.None);

        services.Configs.Should().NotContainKey(ZapretLayout.ServiceName);
        Directory.Exists(folder).Should().BeFalse();
    }

    [Fact]
    public async Task A_setting_the_user_edited_is_what_the_service_runs()
    {
        var (fix, services, _, _) = Create();
        fix.Configure(ZapretProfiles.Custom("--wf-tcp=443 --dpi-desync=fake"));

        await fix.ApplyAsync(new Safety.UndoJournal(Path.Combine(_root, "undo.json")), CancellationToken.None);

        services.Configs[ZapretLayout.ServiceName].BinaryPath.Should().Contain("--wf-tcp=443 --dpi-desync=fake");
    }

    [Fact]
    public async Task Zapret_is_not_installed_while_GoodbyeDPI_runs()
    {
        var (fix, services, goodbye, _) = Create();
        goodbye.Running = true;
        fix.Configure(ZapretProfiles.All[0]);

        var preview = await fix.PreviewAsync(CancellationToken.None);
        var result = await fix.ApplyAsync(new Safety.UndoJournal(Path.Combine(_root, "undo.json")), CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        result.Success.Should().BeFalse();
        services.Configs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--wf-tcp=80,443 --dpi-desync=fake", true)]
    [InlineData("  --wf-udp=443  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("-5 --set-ttl 5", false)]
    [InlineData("--dpi-desync=fake", false)]
    [InlineData("--wf-tcp=443\r\n--debug", false)]
    public void An_edited_setting_must_be_one_line_start_with_dashes_and_name_a_filter(string text, bool usable)
    {
        (ZapretProfiles.Validate(text) is null).Should().Be(usable);
    }

    [Fact]
    public void A_custom_setting_has_its_own_id_and_title()
    {
        var custom = ZapretProfiles.Custom("  --wf-tcp=443  ");

        custom.Id.Should().Be(ZapretProfiles.CustomId);
        custom.Engine.Should().Be(DpiEngines.ZapretId);
        custom.Arguments.Should().Be("--wf-tcp=443");
        custom.Title.Should().Be("Özel ayar");
    }
}