using FoxFix.Core.Network;

namespace FoxFix.Core.Diagnostics.Checks;

/// <summary>Layer 1: is there a physical adapter that is actually connected?</summary>
public sealed class AdapterCheck : ICheck
{
    private readonly INetworkInfoProvider _network;

    public AdapterCheck(INetworkInfoProvider network)
    {
        _network = network;
    }

    public string Id => "adapter";

    public string Title => Strings.Get(Strings.AdapterTitle);

    public Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var physical = _network.GetAdapters().Where(a => a.IsPhysical).ToList();

        if (physical.Count == 0)
        {
            return Task.FromResult(new CheckResult(CheckStatus.Failed, Strings.Get(Strings.AdapterNoneFound)));
        }

        var up = physical.Where(a => a.IsUp).ToList();
        if (up.Count == 0)
        {
            var names = string.Join(", ", physical.Select(a => a.Name));
            return Task.FromResult(new CheckResult(CheckStatus.Failed, Strings.Get(Strings.AdapterNoneUp), names));
        }

        var upNames = string.Join(", ", up.Select(a => a.Name));
        return Task.FromResult(new CheckResult(CheckStatus.Passed, Strings.Format(Strings.AdapterOk, upNames)));
    }
}
