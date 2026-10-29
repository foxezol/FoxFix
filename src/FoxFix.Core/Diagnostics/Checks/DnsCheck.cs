using System.Net;
using FoxFix.Core.Network;

namespace FoxFix.Core.Diagnostics.Checks;

/// <summary>
/// Layer 4: can names be turned into addresses? The UDP-versus-TCP comparison is the strongest
/// evidence for an interceptor such as GoodbyeDPI's DNS redirection (plan sections 2 and 7).
/// </summary>
public sealed class DnsCheck : ICheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private static readonly string[] SystemHosts = ["google.com", "cloudflare.com"];

    private static readonly IPAddress[] DirectServers =
    [
        IPAddress.Parse("8.8.8.8"),
        IPAddress.Parse("1.1.1.1"),
    ];

    private const string DirectHost = "google.com";

    /// <summary>
    /// RFC 5737 TEST-NET-1: nobody can answer here. If a UDP DNS query to it gets a valid answer,
    /// something on this machine is redirecting UDP port 53 (e.g. GoodbyeDPI's --dns-addr).
    /// </summary>
    private static readonly IPAddress CanaryServer = IPAddress.Parse("192.0.2.1");

    private readonly IDnsProbe _probe;
    private readonly Func<bool> _redirectIsExpected;

    /// <param name="redirectIsExpected">True while FoxFix's own GoodbyeDPI redirects DNS on purpose.</param>
    public DnsCheck(IDnsProbe probe, Func<bool>? redirectIsExpected = null)
    {
        _probe = probe;
        _redirectIsExpected = redirectIsExpected ?? (() => false);
    }

    public string Id => "dns";

    public string Title => Strings.Get(Strings.DnsTitle);

    public async Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var canaryTask = _probe.QueryUdpAsync(CanaryServer, DirectHost, Timeout, ct);
        var systemResolves = await AnyAsync(SystemHosts, host => _probe.ResolveWithSystemAsync(host, Timeout, ct));
        var redirected = await canaryTask;

        if (systemResolves)
        {
            if (redirected && _redirectIsExpected())
            {
                return new CheckResult(CheckStatus.Passed, Strings.Get(Strings.DnsOkManagedRedirect));
            }

            return redirected
                ? new CheckResult(CheckStatus.Warning, Strings.Get(Strings.DnsRedirected))
                : new CheckResult(CheckStatus.Passed, Strings.Get(Strings.DnsOk));
        }

        var udp = await AnyAsync(DirectServers, server => _probe.QueryUdpAsync(server, DirectHost, Timeout, ct));
        var tcp = await AnyAsync(DirectServers, server => _probe.QueryTcpAsync(server, DirectHost, Timeout, ct));

        var verdict = DnsDecisionTree.Decide(systemResolves, udp, tcp);
        var detail = Strings.Format(Strings.DnsDetail, YesNo(systemResolves), YesNo(udp), YesNo(tcp));
        if (redirected)
        {
            detail += Environment.NewLine + Strings.Get(Strings.DnsRedirected);
        }

        return new CheckResult(CheckStatus.Failed, Strings.Get(SummaryKey(verdict)), detail);
    }

    private static string SummaryKey(DnsVerdict verdict) => verdict switch
    {
        DnsVerdict.LocalConfigBroken => Strings.DnsLocalConfig,
        DnsVerdict.UdpIntercepted => Strings.DnsUdpIntercepted,
        DnsVerdict.ServerUnreachable => Strings.DnsUnreachable,
        _ => throw new InvalidOperationException($"Beklenmeyen DNS sonucu: {verdict}"),
    };

    private static string YesNo(bool value) => Strings.Get(value ? Strings.Yes : Strings.No);

    private static async Task<bool> AnyAsync<T>(IEnumerable<T> items, Func<T, Task<bool>> probe)
    {
        var results = await Task.WhenAll(items.Select(probe));
        return results.Any(r => r);
    }
}
