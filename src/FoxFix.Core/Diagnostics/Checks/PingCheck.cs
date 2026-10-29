using System.Net;
using FoxFix.Core.Network;

namespace FoxFix.Core.Diagnostics.Checks;

/// <summary>
/// Layer 3: can we reach the modem and the internet by IP address (no DNS involved)?
/// A silent gateway alone is not a failure: many modems ignore ping (plan section 7, item 6).
/// </summary>
public sealed class PingCheck : ICheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly IPAddress[] InternetTargets =
    [
        IPAddress.Parse("8.8.8.8"),
        IPAddress.Parse("1.1.1.1"),
    ];

    private readonly INetworkInfoProvider _network;
    private readonly IPinger _pinger;

    public PingCheck(INetworkInfoProvider network, IPinger pinger)
    {
        _network = network;
        _pinger = pinger;
    }

    public string Id => "ping";

    public string Title => Strings.Get(Strings.PingTitle);

    public async Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var gateways = _network.GetAdapters()
            .Where(a => a.IsPhysical && a.IsUp)
            .SelectMany(a => a.Gateways)
            .Distinct()
            .ToList();

        var gatewayTasks = gateways.Select(g => _pinger.PingAsync(g, Timeout, ct)).ToList();
        var internetTasks = InternetTargets.Select(t => _pinger.PingAsync(t, Timeout, ct)).ToList();

        var gatewayReplies = await Task.WhenAll(gatewayTasks);
        var internetReplies = await Task.WhenAll(internetTasks);

        var gatewayAnswered = gatewayReplies.Any(r => r);
        var internetAnswered = internetReplies.Any(r => r);

        if (internetAnswered)
        {
            var detail = gateways.Count > 0 && !gatewayAnswered ? Strings.Get(Strings.PingGatewaySilent) : null;
            return new CheckResult(CheckStatus.Passed, Strings.Get(Strings.PingOk), detail);
        }

        var summary = gatewayAnswered ? Strings.PingGatewayOnly : Strings.PingNone;
        return new CheckResult(CheckStatus.Failed, Strings.Get(summary));
    }
}
