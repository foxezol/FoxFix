using FluentAssertions;
using FoxFix.Core.Dpi;
using FoxFix.Core.GoodbyeDpi;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class DriverUnloadTests
{
    private const string Folder = @"C:\Veri\FoxFix\GoodbyeDPI";

    private sealed class Services : IServiceManager
    {
        public Dictionary<string, ServiceConfig> Configs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Stopped { get; } = [];

        public List<string> Deleted { get; } = [];

        public Task<ServiceConfig?> ReadConfigAsync(string name, CancellationToken ct) => Task.FromResult(Configs.GetValueOrDefault(name));

        public Task<StopOutcome> StopAsync(string name, CancellationToken ct)
        {
            Stopped.Add(name);
            return Task.FromResult(StopOutcome.Stopped);
        }

        public Task<DeleteOutcome> DeleteAsync(string name, CancellationToken ct)
        {
            Deleted.Add(name);
            Configs.Remove(name);
            return Task.FromResult(DeleteOutcome.Deleted);
        }

        public Task<bool> CreateAsync(ServiceConfig config, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> StartAsync(string name, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Launcher : IGoodbyeDpiLauncher
    {
        public bool Running { get; set; }

        public IGoodbyeDpiProcess Start(string exePath, string arguments) => throw new NotSupportedException();

        public Task<int> StopAllInFolderAsync(string folder, CancellationToken ct) => Task.FromResult(0);

        public bool IsRunningFromFolder(string folder) => Running;
    }

    private static ServiceConfig Driver(string name, string path) => new(name, name, $"\\??\\{path}", 1, 1, true);

    [Fact]
    public async Task FoxFixs_own_driver_is_stopped_and_removed_so_a_game_finds_nothing_of_it()
    {
        var services = new Services();
        services.Configs["WinDivert1.4"] = Driver("WinDivert1.4", Folder + @"\WinDivert64.sys");

        var unloaded = await new GoodbyeDpiTeardown(services, new Launcher(), Folder).UnloadDriversAsync(CancellationToken.None);

        unloaded.Should().BeTrue();
        services.Stopped.Should().Equal("WinDivert1.4");
        services.Deleted.Should().Equal("WinDivert1.4");
    }

    [Fact]
    public async Task Someone_elses_driver_is_never_touched()
    {
        var services = new Services();
        services.Configs["WinDivert"] = Driver("WinDivert", @"C:\Program Files\Baska\WinDivert64.sys");

        var unloaded = await new GoodbyeDpiTeardown(services, new Launcher(), Folder).UnloadDriversAsync(CancellationToken.None);

        unloaded.Should().BeFalse();
        services.Stopped.Should().BeEmpty();
        services.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task The_driver_stays_while_FoxFixs_program_still_runs_or_its_service_is_installed()
    {
        var services = new Services();
        services.Configs["WinDivert1.4"] = Driver("WinDivert1.4", Folder + @"\WinDivert64.sys");

        (await new GoodbyeDpiTeardown(services, new Launcher { Running = true }, Folder).UnloadDriversAsync(CancellationToken.None)).Should().BeFalse();

        services.Configs[GoodbyeDpiLayout.ServiceName] =
            new ServiceConfig(GoodbyeDpiLayout.ServiceName, "x", Folder + @"\goodbyedpi.exe", 2, 0x10, false);
        (await new GoodbyeDpiTeardown(services, new Launcher(), Folder).UnloadDriversAsync(CancellationToken.None)).Should().BeFalse();

        services.Stopped.Should().BeEmpty();
    }
}