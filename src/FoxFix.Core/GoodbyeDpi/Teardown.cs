using FoxFix.Core.Dpi;

namespace FoxFix.Core.GoodbyeDpi;

/// <summary>
/// Takes FoxFix's own GoodbyeDPI apart again: its service, its process, the WinDivert driver registered from its
/// folder, and (optionally) the folder. It never touches a service or process that runs from anywhere else.
/// </summary>
public sealed class GoodbyeDpiTeardown
{
    private readonly IServiceManager _services;
    private readonly IGoodbyeDpiLauncher _launcher;
    private readonly string _folder;
    private readonly string? _serviceName;

    /// <param name="serviceName">The service that belongs to this folder; null when the folder never has one (the trial copy).</param>
    public GoodbyeDpiTeardown(IServiceManager services, IGoodbyeDpiLauncher launcher, string folder, string? serviceName = GoodbyeDpiLayout.ServiceName)
    {
        _services = services;
        _launcher = launcher;
        _folder = folder;
        _serviceName = serviceName;
    }

    /// <summary>Stops the service and the process but leaves everything installed.</summary>
    public async Task StopAsync(CancellationToken ct)
    {
        if (_serviceName is not null)
        {
            await _services.StopAsync(_serviceName, ct);
        }

        await _launcher.StopAllInFolderAsync(_folder, ct);
    }

    /// <summary>
    /// Lets the WinDivert driver go once FoxDPI has ended, so it is not sitting in memory while a game runs (some anti-cheat
    /// programs object to that driver). Only FoxFix's own driver is touched, and only when nothing of FoxFix is using it: not while
    /// its program still runs and not when its service is installed. Returns true when a driver was unloaded.
    /// </summary>
    public async Task<bool> UnloadDriversAsync(CancellationToken ct)
    {
        if (_launcher.IsRunningFromFolder(_folder))
        {
            return false;
        }

        if (_serviceName is not null && await _services.ReadConfigAsync(_serviceName, ct) is not null)
        {
            return false;
        }

        var unloaded = false;
        foreach (var driver in GoodbyeDpiLayout.DriverNames)
        {
            var config = await _services.ReadConfigAsync(driver, ct);
            if (config is null || !GoodbyeDpiLayout.IsManaged(config.BinaryPath, GoodbyeDpiLayout.OwnRoot(_folder)))
            {
                continue;
            }

            await _services.StopAsync(driver, ct);
            await _services.DeleteAsync(driver, ct);
            unloaded = true;
        }

        return unloaded;
    }

    /// <summary>Removes the service, the driver registration and, when asked, the folder. Returns false if files had to stay behind.</summary>
    public async Task<bool> RemoveAsync(bool deleteFolder, CancellationToken ct)
    {
        await StopAsync(ct);

        if (_serviceName is not null && await _services.ReadConfigAsync(_serviceName, ct) is not null)
        {
            await _services.DeleteAsync(_serviceName, ct);
        }

        foreach (var driver in GoodbyeDpiLayout.DriverNames)
        {
            var config = await _services.ReadConfigAsync(driver, ct);
            // A driver loaded from the install folder or from the trial copy next to it is FoxFix's own.
            if (config is null || !GoodbyeDpiLayout.IsManaged(config.BinaryPath, GoodbyeDpiLayout.OwnRoot(_folder)))
            {
                continue;
            }

            await _services.StopAsync(driver, ct);
            await _services.DeleteAsync(driver, ct);
        }

        if (!deleteFolder || !Directory.Exists(_folder))
        {
            return true;
        }

        try
        {
            Directory.Delete(_folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The driver can stay loaded until a restart; the leftover files are harmless and recognised as FoxFix's own.
            return false;
        }
    }
}
