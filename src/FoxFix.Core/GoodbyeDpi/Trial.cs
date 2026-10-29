using System.ComponentModel;
using System.Diagnostics;
using FoxFix.Core.Dpi;
using FoxFix.Core.Network;

namespace FoxFix.Core.GoodbyeDpi;

public enum TrialStatus
{
    Waiting,
    Testing,
    AllOpened,
    SomeOpened,
    NothingOpened,
    FailedToStart,
}

public enum TrialConclusion
{
    NotNeeded,
    Found,
    NothingHelped,
    CouldNotStart,
    Blocked,
}

public sealed record TrialStep(string ProfileId, TrialStatus Status, int Reached, int Total);

public sealed record ProfileResult(GoodbyeDpiProfile Profile, TrialStatus Status, int Reached, int Total, bool ControlOk, TimeSpan Elapsed);

public sealed record TrialReport(
    TrialConclusion Conclusion,
    int BaselineReached,
    int Total,
    IReadOnlyList<ProfileResult> Results,
    GoodbyeDpiProfile? Best,
    string Message);

/// <summary>Turns the user's site list into HTTPS addresses. Anything that is not a plain host name is dropped.</summary>
public static class TrialTargets
{
    public static readonly Uri Control = new("https://www.microsoft.com/");

    public static IReadOnlyList<Uri> Parse(string? text)
    {
        var uris = new List<Uri>();

        foreach (var piece in (text ?? "").Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var host = piece.Trim();
            host = host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? host["https://".Length..] : host;
            host = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? host["http://".Length..] : host;
            host = host.Split('/', '?', '#')[0].TrimEnd('.');

            if (host.Length > 0 && host.Contains('.') && Uri.CheckHostName(host) == UriHostNameType.Dns
                && Uri.TryCreate($"https://{host}/", UriKind.Absolute, out var uri)
                && uris.All(u => u.Host != uri.Host))
            {
                uris.Add(uri);
            }
        }

        return uris;
    }
}

/// <summary>
/// Tries the ready-made GoodbyeDPI settings one after another against the user's own sites and reports which one
/// works best on this connection. It works on a temporary copy and removes everything again, whatever the outcome.
/// </summary>
public sealed class GoodbyeDpiTrial
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(6);

    private readonly IGoodbyeDpiPayload _payload;
    private readonly IGoodbyeDpiLauncher _launcher;
    private readonly IHttpsProbe _probe;
    private readonly GoodbyeDpiTeardown _teardown;
    private readonly Func<string?> _blocker;
    private readonly Func<CancellationToken, Task> _flushDns;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly string _folder;
    private readonly IReadOnlyDictionary<string, string>? _expectedHashes;
    private readonly DpiEngine _engine;

    public static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan SettleWait = TimeSpan.FromSeconds(1);

    /// <param name="blocker">Returns the reason the trial cannot run now (another GoodbyeDPI is running), or null.</param>
    public GoodbyeDpiTrial(
        IGoodbyeDpiPayload payload,
        IGoodbyeDpiLauncher launcher,
        IHttpsProbe probe,
        IServiceManager services,
        string trialFolder,
        Func<string?> blocker,
        Func<CancellationToken, Task> flushDns,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IReadOnlyDictionary<string, string>? expectedHashes = null,
        DpiEngine? engine = null)
    {
        _engine = engine ?? DpiEngines.GoodbyeDpi;
        _expectedHashes = expectedHashes;
        _payload = payload;
        _launcher = launcher;
        _probe = probe;
        _folder = trialFolder;
        _teardown = new GoodbyeDpiTeardown(services, launcher, trialFolder, serviceName: null);
        _blocker = blocker;
        _flushDns = flushDns;
        _delay = delay ?? Task.Delay;
    }

    public async Task<TrialReport> RunAsync(IReadOnlyList<Uri> targets, IProgress<TrialStep>? progress, CancellationToken ct)
    {
        var profiles = _engine.Profiles;

        if (_blocker() is { } reason)
        {
            return new TrialReport(TrialConclusion.Blocked, 0, targets.Count, [], null, reason);
        }

        foreach (var profile in profiles)
        {
            progress?.Report(new TrialStep(profile.Id, TrialStatus.Waiting, 0, targets.Count));
        }

        var baseline = (await ProbeAsync(targets, ct)).Reached;
        if (baseline == targets.Count)
        {
            return new TrialReport(TrialConclusion.NotNeeded, baseline, targets.Count, [], null, Strings.Get(Strings.TrialNotNeeded));
        }

        var problems = GoodbyeDpiFiles.Prepare(_payload, _folder, _expectedHashes ?? _engine.Hashes);
        if (problems.Count > 0)
        {
            await _teardown.RemoveAsync(deleteFolder: true, CancellationToken.None);
            return new TrialReport(TrialConclusion.CouldNotStart, baseline, targets.Count, [], null,
                Strings.Format(Strings.GdpiFilesBroken, string.Join(", ", problems)));
        }

        var results = new List<ProfileResult>();

        try
        {
            foreach (var profile in profiles)
            {
                ct.ThrowIfCancellationRequested();
                var result = await TryProfileAsync(profile, targets, progress, ct);
                results.Add(result);

                // If the very first start fails, the cause (antivirus, no rights) hits every setting alike.
                if (result.Status == TrialStatus.FailedToStart && results.Count == 1)
                {
                    return new TrialReport(TrialConclusion.CouldNotStart, baseline, targets.Count, results, null,
                        Strings.Get(Strings.TrialCouldNotStart));
                }
            }
        }
        finally
        {
            await _teardown.RemoveAsync(deleteFolder: true, CancellationToken.None);
        }

        var best = results
            .Select((r, index) => (Result: r, Index: index))
            .Where(x => x.Result.Status is TrialStatus.AllOpened or TrialStatus.SomeOpened && x.Result.ControlOk && x.Result.Reached > baseline)
            .OrderByDescending(x => x.Result.Reached)
            .ThenBy(x => x.Result.Elapsed)
            .ThenBy(x => x.Index)
            .Select(x => x.Result)
            .FirstOrDefault();

        return best is null
            ? new TrialReport(TrialConclusion.NothingHelped, baseline, targets.Count, results, null, Strings.Get(Strings.TrialNothingHelped))
            : new TrialReport(TrialConclusion.Found, baseline, targets.Count, results, best.Profile,
                Strings.Format(Strings.TrialFound, best.Profile.Title, best.Reached, best.Total));
    }

    private async Task<ProfileResult> TryProfileAsync(GoodbyeDpiProfile profile, IReadOnlyList<Uri> targets, IProgress<TrialStep>? progress, CancellationToken ct)
    {
        progress?.Report(new TrialStep(profile.Id, TrialStatus.Testing, 0, targets.Count));

        IGoodbyeDpiProcess? process = null;
        try
        {
            process = _launcher.Start(_engine.ExePath(_folder), _engine.BuildArguments(_folder, profile));
            await _delay(StartupWait, ct);

            if (process.HasExited)
            {
                return Done(profile, TrialStatus.FailedToStart, 0, targets.Count, false, TimeSpan.Zero, progress);
            }

            await _flushDns(ct);

            var clock = Stopwatch.StartNew();
            var (reached, controlOk) = await ProbeAsync(targets, ct);
            clock.Stop();

            var status = reached == targets.Count ? TrialStatus.AllOpened : reached > 0 ? TrialStatus.SomeOpened : TrialStatus.NothingOpened;
            return Done(profile, status, reached, targets.Count, controlOk, clock.Elapsed, progress);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            return Done(profile, TrialStatus.FailedToStart, 0, targets.Count, false, TimeSpan.Zero, progress);
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    await process.StopAsync();
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or TimeoutException)
                {
                    // Already gone; the teardown at the end ends anything still running from the trial folder.
                }

                process.Dispose();
                await _delay(SettleWait, CancellationToken.None);
            }
        }
    }

    private static ProfileResult Done(
        GoodbyeDpiProfile profile, TrialStatus status, int reached, int total, bool controlOk, TimeSpan elapsed, IProgress<TrialStep>? progress)
    {
        progress?.Report(new TrialStep(profile.Id, status, reached, total));
        return new ProfileResult(profile, status, reached, total, controlOk, elapsed);
    }

    private async Task<(int Reached, bool ControlOk)> ProbeAsync(IReadOnlyList<Uri> targets, CancellationToken ct)
    {
        var all = targets.Append(TrialTargets.Control).ToList();
        var outcomes = await Task.WhenAll(all.Select(uri => _probe.ProbeAsync(uri, ProbeTimeout, ct)));

        var reached = outcomes.Take(targets.Count).Count(o => o == HttpsProbeOutcome.Ok);
        return (reached, outcomes[^1] == HttpsProbeOutcome.Ok);
    }
}
