using System.Diagnostics;
using FoxFix.Core.Network;

namespace FoxFix.Core.GoodbyeDpi;

/// <summary>How one site answered the access test.</summary>
public sealed record AccessResult(string Host, HttpsProbeOutcome Outcome, TimeSpan Elapsed)
{
    public bool Opened => Outcome == HttpsProbeOutcome.Ok;

    /// <summary>The outcome in plain words ("açıldı", "zaman aşımı", ...).</summary>
    public string Text => Strings.Get(Outcome switch
    {
        HttpsProbeOutcome.Ok => Strings.HttpsOutcomeOk,
        HttpsProbeOutcome.Timeout => Strings.HttpsOutcomeTimeout,
        HttpsProbeOutcome.TlsFailed => Strings.HttpsOutcomeTls,
        HttpsProbeOutcome.CertificateError => Strings.HttpsOutcomeCertificate,
        HttpsProbeOutcome.DnsFailed => Strings.HttpsOutcomeDns,
        _ => Strings.HttpsOutcomeOther,
    });
}

/// <summary>
/// "Erişim testi": asks the user's sites for a secure connection, the way a browser would, and reports which open.
/// It only looks; run it with FoxDPI off and on to see what FoxDPI changes.
/// </summary>
public sealed class AccessTester
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private readonly IHttpsProbe _probe;

    public AccessTester(IHttpsProbe probe)
    {
        _probe = probe;
    }

    /// <summary>All sites are tried at the same time; results come back in the order the sites were given.</summary>
    public async Task<IReadOnlyList<AccessResult>> RunAsync(IReadOnlyList<Uri> sites, IProgress<AccessResult>? progress, CancellationToken ct)
    {
        var tasks = sites.Select(async site =>
        {
            var clock = Stopwatch.StartNew();
            var outcome = await _probe.ProbeAsync(site, Timeout, ct);
            clock.Stop();

            var result = new AccessResult(site.Host, outcome, clock.Elapsed);
            progress?.Report(result);
            return result;
        });

        return await Task.WhenAll(tasks);
    }
}
