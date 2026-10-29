using System.Runtime.Versioning;
using FoxFix.Core.Dpi;
using Microsoft.Win32;

namespace FoxFix.Core.Startup;

public enum StartupSource
{
    CurrentUserRun,
    MachineRun,
    MachineRun32,
    CurrentUserFolder,
    CommonFolder,
}

public sealed record StartupEntry(string Name, string Command, StartupSource Source, bool Enabled)
{
    public string Id => $"{Source}|{Name}";

    /// <summary>Machine-wide entries live under HKLM or the shared Startup folder.</summary>
    public bool NeedsAdmin => Source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.CommonFolder;
}

public interface IStartupStore
{
    /// <summary>Entries the user may switch. Windows' own entries are not listed.</summary>
    IReadOnlyList<StartupEntry> List();

    /// <summary>The raw StartupApproved value as base64, or null when there is none (which means "enabled").</summary>
    string? ReadApproval(StartupEntry entry);

    /// <summary>Writes the raw value back; null removes it.</summary>
    void WriteApproval(StartupEntry entry, string? rawBase64);
}

/// <summary>
/// The "StartupApproved" format Task Manager uses: 12 bytes, first byte 02 means enabled, 03 means disabled
/// (followed by the time it was disabled). The entry itself is never removed.
/// </summary>
public static class StartupApproval
{
    private const int Length = 12;

    public static bool IsEnabled(byte[]? raw) => raw is null || raw.Length == 0 || (raw[0] & 1) == 0;

    public static byte[] EnabledValue()
    {
        var raw = new byte[Length];
        raw[0] = 2;
        return raw;
    }

    public static byte[] DisabledValue(DateTime utcNow)
    {
        var raw = new byte[Length];
        raw[0] = 3;
        BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(raw, 4);
        return raw;
    }
}

public static class StartupFilter
{
    /// <summary>Entries that belong to Windows or to its security: never offered for switching off.</summary>
    public static bool IsProtected(string name, string command, string windowsDirectory)
    {
        if (name.Equals("SecurityHealth", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var path = ServicePathParser.GetPath(Environment.ExpandEnvironmentVariables(command)) ?? "";
        var windows = windowsDirectory.TrimEnd('\\') + "\\";

        return path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Windows Defender\", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Reads Run keys and Startup folders; writes only StartupApproved. Needs a hand test on a real profile.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryStartupStore : IStartupStore
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private static readonly StartupSource[] AllSources = Enum.GetValues<StartupSource>();

    public IReadOnlyList<StartupEntry> List()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var entries = new List<StartupEntry>();

        foreach (var source in AllSources)
        {
            foreach (var (name, command) in Read(source))
            {
                if (!StartupFilter.IsProtected(name, command, windows))
                {
                    entries.Add(new StartupEntry(name, command, source, IsEnabled(source, name)));
                }
            }
        }

        return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public string? ReadApproval(StartupEntry entry)
    {
        using var key = ApprovalHive(entry.Source).OpenSubKey(ApprovalPath(entry.Source));
        return key?.GetValue(entry.Name) is byte[] raw ? Convert.ToBase64String(raw) : null;
    }

    public void WriteApproval(StartupEntry entry, string? rawBase64)
    {
        using var key = ApprovalHive(entry.Source).CreateSubKey(ApprovalPath(entry.Source), writable: true);

        if (rawBase64 is null)
        {
            key.DeleteValue(entry.Name, throwOnMissingValue: false);
            return;
        }

        key.SetValue(entry.Name, Convert.FromBase64String(rawBase64), RegistryValueKind.Binary);
    }

    private static IEnumerable<(string Name, string Command)> Read(StartupSource source)
    {
        switch (source)
        {
            case StartupSource.CurrentUserRun:
                return ReadRunKey(Registry.CurrentUser, RunPath);
            case StartupSource.MachineRun:
                return ReadRunKey(Registry.LocalMachine, RunPath);
            case StartupSource.MachineRun32:
                return ReadRunKey(Registry.LocalMachine, Run32Path);
            case StartupSource.CurrentUserFolder:
                return ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            default:
                return ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
        }
    }

    private static IEnumerable<(string, string)> ReadRunKey(RegistryKey hive, string path)
    {
        using var key = hive.OpenSubKey(path);
        if (key is null)
        {
            return [];
        }

        return key.GetValueNames()
            .Where(n => n.Length > 0)
            .Select(n => (n, key.GetValue(n, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? ""))
            .ToList();
    }

    private static IEnumerable<(string, string)> ReadFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder)
            .Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path.GetFileName(f), f))
            .ToList();
    }

    private bool IsEnabled(StartupSource source, string name)
    {
        using var key = ApprovalHive(source).OpenSubKey(ApprovalPath(source));
        return StartupApproval.IsEnabled(key?.GetValue(name) as byte[]);
    }

    private static RegistryKey ApprovalHive(StartupSource source) =>
        source is StartupSource.CurrentUserRun or StartupSource.CurrentUserFolder ? Registry.CurrentUser : Registry.LocalMachine;

    private static string ApprovalPath(StartupSource source) => ApprovedRoot + source switch
    {
        StartupSource.MachineRun32 => "Run32",
        StartupSource.CurrentUserFolder or StartupSource.CommonFolder => "StartupFolder",
        _ => "Run",
    };
}
