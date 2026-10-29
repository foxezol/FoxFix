using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace FoxFix.Core.Dpi;

public sealed record ServiceEntry(string Name, string? PathName, bool IsDriver, bool IsRunning);

public sealed record ProcessEntry(string Name, string? Path);

/// <summary>Read-only views of the machine, so the detector can be tested without touching Windows.</summary>
public interface IServiceSource
{
    IReadOnlyList<ServiceEntry> GetServicesAndDrivers();
}

public interface IProcessSource
{
    IReadOnlyList<ProcessEntry> GetProcesses();
}

public interface IPathProbe
{
    bool DirectoryExists(string path);
}

[SupportedOSPlatform("windows")]
public sealed class WmiServiceSource : IServiceSource
{
    public IReadOnlyList<ServiceEntry> GetServicesAndDrivers()
    {
        var entries = new List<ServiceEntry>();
        entries.AddRange(Query("Win32_Service", isDriver: false));
        entries.AddRange(Query("Win32_SystemDriver", isDriver: true));
        return entries;
    }

    private static IEnumerable<ServiceEntry> Query(string wmiClass, bool isDriver)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT Name, PathName, State FROM {wmiClass}");
        using var results = searcher.Get();

        foreach (var item in results)
        {
            using (item)
            {
                var name = item["Name"] as string;
                if (name is null)
                {
                    continue;
                }

                var running = string.Equals(item["State"] as string, "Running", StringComparison.OrdinalIgnoreCase);
                yield return new ServiceEntry(name, item["PathName"] as string, isDriver, running);
            }
        }
    }
}

public sealed class SystemProcessSource : IProcessSource
{
    public IReadOnlyList<ProcessEntry> GetProcesses()
    {
        var entries = new List<ProcessEntry>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                entries.Add(new ProcessEntry(process.ProcessName, TryGetPath(process)));
            }
        }

        return entries;
    }

    private static string? TryGetPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Protected/elevated processes refuse access; the name is enough for our allowlist.
            return null;
        }
    }
}

public sealed class FileSystemPathProbe : IPathProbe
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
}
