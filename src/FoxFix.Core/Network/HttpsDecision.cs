namespace FoxFix.Core.Network;

public enum HttpsVerdict
{
    Healthy,

    /// <summary>The raw IP answers over HTTPS but the named sites do not: SNI/DPI filtering, a firewall or a blocked site.</summary>
    SiteBlocked,

    /// <summary>Certificates are rejected: wrong date/time, or antivirus/proxy intercepting TLS.</summary>
    CertificateProblem,

    Broken,
}

public static class HttpsDecision
{
    public static HttpsVerdict Decide(HttpsProbeOutcome ip, IReadOnlyList<HttpsProbeOutcome> sites)
    {
        if (sites.Any(s => s == HttpsProbeOutcome.Ok))
        {
            return HttpsVerdict.Healthy;
        }

        if (ip == HttpsProbeOutcome.CertificateError || sites.Any(s => s == HttpsProbeOutcome.CertificateError))
        {
            return HttpsVerdict.CertificateProblem;
        }

        return ip == HttpsProbeOutcome.Ok ? HttpsVerdict.SiteBlocked : HttpsVerdict.Broken;
    }
}
