using FoxFix.Core.Network;

namespace FoxFix.Core.Diagnostics.Checks;

/// <summary>
/// Layer 5: can a TLS connection be made? A bare IP (no DNS, no SNI name) and named sites are compared:
/// IP works but names do not points at SNI/DPI filtering or a firewall.
/// </summary>
public sealed class HttpsCheck : ICheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private static readonly Uri IpTarget = new("https://1.1.1.1");

    private static readonly Uri[] SiteTargets =
    [
        new("https://www.cloudflare.com"),
        new("https://www.microsoft.com"),
    ];

    private readonly IHttpsProbe _probe;

    public HttpsCheck(IHttpsProbe probe)
    {
        _probe = probe;
    }

    public string Id => "https";

    public string Title => Strings.Get(Strings.HttpsTitle);

    public async Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var ipTask = _probe.ProbeAsync(IpTarget, Timeout, ct);
        var siteTasks = SiteTargets.Select(u => _probe.ProbeAsync(u, Timeout, ct)).ToList();

        var ip = await ipTask;
        var sites = await Task.WhenAll(siteTasks);

        var verdict = HttpsDecision.Decide(ip, sites);
        if (verdict == HttpsVerdict.Healthy)
        {
            return new CheckResult(CheckStatus.Passed, Strings.Get(Strings.HttpsOk));
        }

        var detail = Strings.Format(Strings.HttpsDetail, OutcomeText(ip), string.Join(", ", sites.Select(OutcomeText)));

        return verdict switch
        {
            HttpsVerdict.SiteBlocked => new CheckResult(CheckStatus.Warning, Strings.Get(Strings.HttpsSiteBlocked), detail),
            HttpsVerdict.CertificateProblem => new CheckResult(CheckStatus.Failed, Strings.Get(Strings.HttpsCertificate), detail),
            _ => new CheckResult(CheckStatus.Failed, Strings.Get(Strings.HttpsBroken), detail),
        };
    }

    private static string OutcomeText(HttpsProbeOutcome outcome) => Strings.Get(outcome switch
    {
        HttpsProbeOutcome.Ok => Strings.HttpsOutcomeOk,
        HttpsProbeOutcome.Timeout => Strings.HttpsOutcomeTimeout,
        HttpsProbeOutcome.TlsFailed => Strings.HttpsOutcomeTls,
        HttpsProbeOutcome.CertificateError => Strings.HttpsOutcomeCertificate,
        HttpsProbeOutcome.DnsFailed => Strings.HttpsOutcomeDns,
        _ => Strings.HttpsOutcomeOther,
    });
}
