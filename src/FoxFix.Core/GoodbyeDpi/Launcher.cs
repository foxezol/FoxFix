using System.Diagnostics;
using System.Runtime.Versioning;

namespace FoxFix.Core.GoodbyeDpi;

/// <summary>A running goodbyedpi.exe. Disposing only lets go of the handle; it does not end the program.</summary>
public interface IGoodbyeDpiProcess : IDisposable
{
    bool HasExited { get; }

    Task StopAsync();
}

public interface IGoodbyeDpiLauncher
{
    /// <summary>Starts it hidden. Throws when Windows refuses (no admin rights, blocked by antivirus).</summary>
    IGoodbyeDpiProcess Start(string exePath, string arguments);

    /// <summary>Ends every goodbyedpi.exe that runs from this folder (and no other). Returns how many were ended.</summary>
    Task<int> StopAllInFolderAsync(string folder, CancellationToken ct);

    bool IsRunningFromFolder(string folder);
}

[SupportedOSPlatform("windows")]
public sealed class SystemGoodbyeDpiLauncher : IGoodbyeDpiLauncher
{
    private readonly string _processName;

    /// <param name="processName">The program's process name without ".exe": "goodbyedpi", or "winws" for Zapret.</param>
    public SystemGoodbyeDpiLauncher(string processName = "goodbyedpi")
    {
        _processName = processName;
    }

    public IGoodbyeDpiProcess Start(string exePath, string arguments)
    {
        var process = Process.Start(new ProcessStartInfo(exePath, arguments)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"{_processName}.exe başlatılamadı.");

        // If FoxFix itself dies (crash, killed), Windows ends GoodbyeDPI with it instead of leaving it running unseen.
        KillOnCloseJob.TryAdd(process);

        return new Handle(process);
    }

    public async Task<int> StopAllInFolderAsync(string folder, CancellationToken ct)
    {
        var ended = 0;

        foreach (var process in Process.GetProcessesByName(_processName))
        {
            using (process)
            {
                if (!IsFromFolder(process, folder))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
                ended++;
            }
        }

        return ended;
    }

    public bool IsRunningFromFolder(string folder)
    {
        foreach (var process in Process.GetProcessesByName(_processName))
        {
            using (process)
            {
                if (IsFromFolder(process, folder))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsFromFolder(Process process, string folder)
    {
        try
        {
            return GoodbyeDpiLayout.IsManaged(process.MainModule?.FileName, folder);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private sealed class Handle(Process process) : IGoodbyeDpiProcess
    {
        public bool HasExited => process.HasExited;

        public async Task StopAsync()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Dispose() => process.Dispose();
    }
}

/// <summary>A Windows job object that ends every process in it when FoxFix's own handle to it closes.</summary>
[SupportedOSPlatform("windows")]
internal static class KillOnCloseJob
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private static IntPtr _job;

    public static void TryAdd(Process process)
    {
        try
        {
            if (_job == IntPtr.Zero)
            {
                _job = CreateJobObject(IntPtr.Zero, null);

                var info = new ExtendedLimits { BasicLimits = new BasicLimits { LimitFlags = KillOnJobClose } };
                var size = System.Runtime.InteropServices.Marshal.SizeOf<ExtendedLimits>();
                var pointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
                try
                {
                    System.Runtime.InteropServices.Marshal.StructureToPtr(info, pointer, fDeleteOld: false);
                    SetInformationJobObject(_job, ExtendedLimitInformation, pointer, (uint)size);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(pointer);
                }
            }

            AssignProcessToJobObject(_job, process.Handle);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Only a safety net: without it GoodbyeDPI still stops through FoxFix's own Stop.
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits BasicLimits;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}