namespace FoxFix.Core.Dpi;

public enum StopOutcome
{
    Stopped,
    AlreadyStopped,
    NotFound,
    Failed,
}

public enum DeleteOutcome
{
    Deleted,

    /// <summary>Windows accepted the delete but keeps the entry until a reboot (error 1072).</summary>
    MarkedForDeletion,
    NotFound,
    Failed,
}

/// <summary>Everything needed to recreate a service or driver exactly as it was.</summary>
public sealed record ServiceConfig(
    string Name,
    string DisplayName,
    string BinaryPath,
    int StartType,
    int ServiceType,
    bool IsDriver);

/// <summary>The only mutating door to Windows services. Fakes stand in for it in tests.</summary>
public interface IServiceManager
{
    Task<ServiceConfig?> ReadConfigAsync(string name, CancellationToken ct);

    Task<StopOutcome> StopAsync(string name, CancellationToken ct);

    Task<DeleteOutcome> DeleteAsync(string name, CancellationToken ct);

    Task<bool> CreateAsync(ServiceConfig config, CancellationToken ct);

    Task<bool> StartAsync(string name, CancellationToken ct);
}

public interface IProcessKiller
{
    /// <summary>Ends every process with this exact name (no extension). Returns how many were ended.</summary>
    Task<int> KillAsync(string processName, CancellationToken ct);
}
