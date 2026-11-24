using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace FoxFix.Core.Overlay;

/// <summary>A number read from the system now and then (the processor's or the graphics card's load); null when it cannot be read.</summary>
public interface IMetricSource
{
    double? Read();
}

/// <summary>Tells, for every program, when it puts a finished picture on the screen.</summary>
public interface IPresentFeed : IDisposable
{
    /// <summary>Starts listening; <paramref name="onPresent"/> gets the process id each time. Throws when it cannot listen.</summary>
    void Start(Action<int> onPresent);
}

/// <summary>The processor's load from the times Windows keeps (no performance-counter package needed).</summary>
[SupportedOSPlatform("windows")]
public sealed class SystemCpuSource : IMetricSource
{
    private CpuTimes? _last;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    public double? Read()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        var now = new CpuTimes(idle.Value, kernel.Value, user.Value);
        var before = _last;
        _last = now;
        return before is { } b ? CpuTimes.Percent(b, now) : null;
    }
}

/// <summary>The graphics card's load from Windows' "GPU Engine" counters, read through WMI (Windows 10 1709 and later).</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiGpuSource : IMetricSource
{
    /// <summary>Why the last reading failed (null when it worked); the overlay shows N/A and the page can say why.</summary>
    public string? LastError { get; private set; }

    public double? Read()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            var instances = new List<(string, double)>();

            foreach (var item in searcher.Get())
            {
                instances.Add((item["Name"] as string ?? "", Convert.ToDouble(item["UtilizationPercentage"] ?? 0)));
                item.Dispose();
            }

            LastError = null;
            return GpuLoad.Percent(instances);
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastError = ex.Message;
            return null;
        }
    }
}

/// <summary>
/// Frames per second without touching any game: Windows itself reports every picture a program shows (the DXGI "Present" event) and
/// that is only counted. Needs administrator rights (FoxFix has them). Needs a hand test on a real game.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EtwPresentFeed : IPresentFeed
{
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const int PresentStart = 42;

    private TraceEventSession? _session;
    private Task? _processing;

    public void Start(Action<int> onPresent)
    {
        var session = new TraceEventSession("FoxFix-Present-" + Environment.ProcessId) { StopOnDispose = true };
        try
        {
            session.EnableProvider(Dxgi, TraceEventLevel.Informational, ulong.MaxValue);
            session.Source.AllEvents += data =>
            {
                if (data.ProviderGuid == Dxgi && (int)data.ID == PresentStart)
                {
                    onPresent(data.ProcessID);
                }
            };
        }
        catch
        {
            session.Dispose();
            throw;
        }

        _session = session;
        _processing = Task.Run(() => session.Source.Process());
    }

    public void Dispose()
    {
        var session = _session;
        _session = null;
        session?.Dispose();

        try
        {
            _processing?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The listening thread ends with an error when its session is stopped under it; that is the way it ends.
        }
    }
}

/// <summary>
/// Puts the numbers of the overlay together once a second. Only what is shown is read, and nothing at all is measured while the
/// overlay is not running.
/// </summary>
public sealed class OverlayService : IDisposable
{
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "explorer", "csrss", "FoxFix", "FoxDPI", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "TextInputHost",
    };

    private readonly IPresentFeed _feed;
    private readonly IMetricSource _cpu;
    private readonly IMetricSource _gpu;
    private readonly PresentCounter _counter;
    private readonly Func<int> _foreground;
    private readonly Func<int, bool> _ignore;
    private bool _running;

    public OverlayService(
        IPresentFeed feed, IMetricSource cpu, IMetricSource gpu, PresentCounter? counter = null,
        Func<int>? foreground = null, Func<int, bool>? ignore = null)
    {
        _feed = feed;
        _cpu = cpu;
        _gpu = gpu;
        _counter = counter ?? new PresentCounter();
        _foreground = foreground ?? (() => 0);
        _ignore = ignore ?? (_ => false);
    }

    /// <summary>Set when the frame counting could not start (no administrator rights, for example); frames then show as N/A.</summary>
    public string? FeedError { get; private set; }

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        FeedError = null;

        try
        {
            _feed.Start(_counter.OnPresent);
        }
        catch (Exception ex)
        {
            // Not hidden: the page says why the frame numbers are missing; the processor and graphics numbers still work.
            FeedError = ex.Message;
        }

        // The first processor reading only sets the starting point.
        _cpu.Read();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _feed.Dispose();
    }

    /// <summary>The numbers now, for what is shown. Values for things that are not shown are not even read.</summary>
    public OverlayReading Sample(OverlaySettings shown)
    {
        if (!_running)
        {
            return OverlayReading.Empty;
        }

        PresentStats? frames = shown.ShowFps || shown.ShowFrameTime ? _counter.Snapshot(_foreground(), _ignore) : null;

        return new OverlayReading(
            shown.ShowFps ? frames?.Fps : null,
            shown.ShowFrameTime ? frames?.FrameMs : null,
            shown.ShowGpu ? _gpu.Read() : null,
            shown.ShowCpu ? _cpu.Read() : null);
    }

    public void Dispose() => Stop();

    /// <summary>True for the desktop itself and FoxFix: they draw all the time but are not what the person wants to see.</summary>
    [SupportedOSPlatform("windows")]
    public static Func<int, bool> IgnoreSystemPrograms()
    {
        var names = new Dictionary<int, bool>();
        return pid =>
        {
            if (names.TryGetValue(pid, out var known))
            {
                return known;
            }

            bool ignored;
            try
            {
                using var process = Process.GetProcessById(pid);
                ignored = NotGames.Contains(process.ProcessName);
            }
            catch (ArgumentException)
            {
                // The program is already gone.
                ignored = true;
            }

            names[pid] = ignored;
            return ignored;
        };
    }
}
