namespace FoxFix.Core.GoodbyeDpi;

public sealed record FoxDpiStartResult(bool Started, string Message);

/// <summary>Everything the session needs to run one DPI program: its description, where its files come from, how it starts, its folder.</summary>
public sealed record DpiRuntime(
    DpiEngine Engine, IGoodbyeDpiPayload Payload, IGoodbyeDpiLauncher Launcher, string Folder, IReadOnlyDictionary<string, string>? Hashes = null);

/// <summary>
/// "FoxDPI Başlat": runs GoodbyeDPI (or Zapret, depending on the chosen setting) hidden from FoxFix's own folder and keeps hold of it. It lives exactly as long as
/// this object says: stopping it, or FoxFix ending, ends GoodbyeDPI, so nothing is installed and nothing is left running.
/// </summary>
public sealed class FoxDpiSession
{
    private readonly Dictionary<string, DpiRuntime> _runtimes = [];
    private readonly GoodbyeDpiStatusReader _status;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private IGoodbyeDpiProcess? _process;

    public FoxDpiSession(
        IGoodbyeDpiPayload payload,
        IGoodbyeDpiLauncher launcher,
        GoodbyeDpiStatusReader status,
        string folder,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IReadOnlyDictionary<string, string>? expectedHashes = null,
        IEnumerable<DpiRuntime>? otherEngines = null)
    {
        _status = status;
        _delay = delay ?? Task.Delay;
        _runtimes[DpiEngines.GoodbyeDpiId] = new DpiRuntime(DpiEngines.GoodbyeDpi, payload, launcher, folder, expectedHashes);

        foreach (var runtime in otherEngines ?? [])
        {
            _runtimes[runtime.Engine.Id] = runtime;
        }
    }

    /// <summary>True while the GoodbyeDPI this session started is alive.</summary>
    public bool IsRunning => _process is { HasExited: false };

    public GoodbyeDpiProfile? Profile { get; private set; }

    public async Task<FoxDpiStartResult> StartAsync(GoodbyeDpiProfile profile, CancellationToken ct)
    {
        if (IsRunning)
        {
            return new FoxDpiStartResult(false, Strings.Get(Strings.FoxDpiAlreadyRunning));
        }

        var status = _status.Read();
        if (status.OtherRunning)
        {
            return new FoxDpiStartResult(false, Strings.Get(Strings.GdpiBlockedForeign));
        }

        if (status.IsActive)
        {
            return new FoxDpiStartResult(false, Strings.Get(Strings.FoxDpiAlreadyRunning));
        }

        if (!_runtimes.TryGetValue(profile.Engine, out var runtime))
        {
            return new FoxDpiStartResult(false, Strings.Format(Strings.FoxDpiStartFailed, profile.Engine));
        }

        var problems = GoodbyeDpiFiles.Prepare(runtime.Payload, runtime.Folder, runtime.Hashes ?? runtime.Engine.Hashes);
        if (problems.Count > 0)
        {
            return new FoxDpiStartResult(false, Strings.Format(Strings.GdpiFilesBroken, string.Join(", ", problems)));
        }

        try
        {
            new GoodbyeDpiState(profile.Id).Write(runtime.Folder);
            _process = runtime.Launcher.Start(runtime.Engine.ExePath(runtime.Folder), runtime.Engine.BuildArguments(runtime.Folder, profile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new FoxDpiStartResult(false, Strings.Format(Strings.FoxDpiStartFailed, ex.Message));
        }

        await _delay(TimeSpan.FromSeconds(2), ct);

        if (!IsRunning)
        {
            _process.Dispose();
            _process = null;
            return new FoxDpiStartResult(false, Strings.Get(Strings.FoxDpiNotRunning));
        }

        Profile = profile;
        return new FoxDpiStartResult(true, Strings.Get(Strings.FoxDpiStarted));
    }

    /// <summary>Ends GoodbyeDPI. Safe to call when it is not running.</summary>
    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        Profile = null;

        if (process is null)
        {
            return;
        }

        try
        {
            await process.StopAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            // Already gone.
        }
        finally
        {
            process.Dispose();
        }
    }
}
