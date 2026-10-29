using System.Diagnostics;
using System.Runtime.Versioning;
using FoxFix.Core.Commands;
using Microsoft.Win32;

namespace FoxFix.Core.Dpi;

/// <summary>
/// Real Windows implementation. NOT exercised by automated tests (it changes the system):
/// only its argument/exit-code mapping is unit tested. Must be tried by hand in a VM snapshot.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemServiceManager : IServiceManager
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";

    private static readonly string ScPath = Path.Combine(Environment.SystemDirectory, "sc.exe");

    private readonly ICommandRunner _runner;

    public SystemServiceManager(ICommandRunner runner)
    {
        _runner = runner;
    }

    public Task<ServiceConfig?> ReadConfigAsync(string name, CancellationToken ct)
    {
        RequireSafeName(name);

        using var key = Registry.LocalMachine.OpenSubKey(ServicesKey + name);
        if (key is null)
        {
            return Task.FromResult<ServiceConfig?>(null);
        }

        var image = key.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (string.IsNullOrWhiteSpace(image))
        {
            return Task.FromResult<ServiceConfig?>(null);
        }

        var start = key.GetValue("Start") as int? ?? 3;
        var type = key.GetValue("Type") as int? ?? 0x10;
        var display = key.GetValue("DisplayName") as string ?? name;

        return Task.FromResult<ServiceConfig?>(new ServiceConfig(name, display, image, start, type, (type & 0x0F) != 0));
    }

    public async Task<StopOutcome> StopAsync(string name, CancellationToken ct)
    {
        RequireSafeName(name);
        var result = await _runner.RunAsync(ScPath, ScArguments.Stop(name), ct);
        return ScArguments.MapStop(result.ExitCode);
    }

    public async Task<DeleteOutcome> DeleteAsync(string name, CancellationToken ct)
    {
        RequireSafeName(name);
        var result = await _runner.RunAsync(ScPath, ScArguments.Delete(name), ct);
        return ScArguments.MapDelete(result.ExitCode);
    }

    public async Task<bool> CreateAsync(ServiceConfig config, CancellationToken ct)
    {
        RequireSafeName(config.Name);

        IReadOnlyList<string> arguments;
        try
        {
            arguments = ScArguments.Create(config);
        }
        catch (NotSupportedException)
        {
            return false;
        }

        var result = await _runner.RunAsync(ScPath, arguments, ct);
        return result.Success;
    }

    public async Task<bool> StartAsync(string name, CancellationToken ct)
    {
        RequireSafeName(name);
        var result = await _runner.RunAsync(ScPath, ScArguments.Start(name), ct);
        return result.Success;
    }

    private static void RequireSafeName(string name)
    {
        if (!DpiCleanupPolicy.IsSafeServiceName(name))
        {
            throw new ArgumentException($"Güvenli olmayan hizmet adı: {name}", nameof(name));
        }
    }
}

public sealed class SystemProcessKiller : IProcessKiller
{
    public Task<int> KillAsync(string processName, CancellationToken ct)
    {
        var killed = 0;

        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                ct.ThrowIfCancellationRequested();
                process.Kill(entireProcessTree: true);
                killed++;
            }
        }

        return Task.FromResult(killed);
    }
}
