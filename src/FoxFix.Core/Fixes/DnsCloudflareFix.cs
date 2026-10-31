using System.Net;
using FoxFix.Core.Dpi;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Fixes;

/// <summary>
/// Sets the DNS of every connected physical connection to Cloudflare's 1.1.1.1 and 1.0.0.1. Some connections need this for
/// FoxDPI to work. It is a choice, not a repair, so "Beni Düzelt" skips it. The old servers are saved for "Geri Al". A
/// running DnsCrypt-Proxy is a deliberate setup, so a connection that points at this computer is left alone then.
/// </summary>
public sealed class DnsCloudflareFix : IFix
{
    public const string FixIdValue = "dns-cloudflare";

    public static readonly IReadOnlyList<string> Servers = ["1.1.1.1", "1.0.0.1"];

    private readonly INetworkInfoProvider _network;
    private readonly IServiceSource _services;
    private readonly IDnsConfigurator _configurator;

    public DnsCloudflareFix(INetworkInfoProvider network, IServiceSource services, IDnsConfigurator configurator)
    {
        _network = network;
        _services = services;
        _configurator = configurator;
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.DnsCloudflareTitle);

    public bool IncludedInRepair => false;

    public async Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var plan = await PlanAsync(ct);
        if (plan.Targets.Count == 0 && plan.NoBackup.Count == 0)
        {
            return new FixPreview([Strings.Get(Strings.DnsCloudflareNothing)], HasChanges: false);
        }

        var lines = plan.Targets
            .Select(t => Strings.Format(Strings.DnsCloudflarePreviewAdapter, t.Adapter.Name, Describe(t.Snapshot)))
            .ToList();
        lines.AddRange(plan.NoBackup.Select(a => Strings.Format(Strings.DnsRepairNoBackup, a.Name)));

        if (plan.Targets.Count == 0)
        {
            return new FixPreview(lines, HasChanges: false);
        }

        lines.Add(Strings.Get(Strings.DnsRepairPreviewFlush));
        lines.Add(Strings.Get(Strings.DnsRepairPreviewUndo));
        return new FixPreview(lines);
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var plan = await PlanAsync(ct);
        if (plan.Targets.Count == 0)
        {
            return new FixResult(true, Strings.Get(Strings.DnsCloudflareNothing));
        }

        var changed = 0;
        var failures = plan.NoBackup.Select(a => Strings.Format(Strings.DnsRepairNoBackup, a.Name)).ToList();

        foreach (var (adapter, snapshot) in plan.Targets)
        {
            var record = new UndoRecord(
                Guid.NewGuid(), FixIdValue, DateTimeOffset.UtcNow,
                Strings.Format(Strings.DnsCloudflareUndoDescription, adapter.Name),
                DnsRepairFix.ToData(snapshot));

            // Journal first, so the old servers can always be put back.
            await journal.AppendAsync(record, ct);

            // IPv6 servers somebody typed in stay as they are; only the IPv4 ones are replaced.
            if (await _configurator.SetStaticAsync(adapter.Name, Servers, snapshot.V6Servers, ct))
            {
                changed++;
            }
            else
            {
                await journal.RemoveAsync(record.Id, ct);
                failures.Add(Strings.Format(Strings.DnsRepairSetFailed, adapter.Name));
            }
        }

        if (changed > 0 && !await _configurator.FlushCacheAsync(ct))
        {
            failures.Add(Strings.Get(Strings.DnsRepairFlushFailed));
        }

        var summary = failures.Count == 0
            ? Strings.Format(Strings.DnsCloudflareResultDone, changed)
            : Strings.Format(Strings.DnsRepairResultPartial, changed, failures.Count)
              + Environment.NewLine + string.Join(Environment.NewLine, failures);

        return new FixResult(failures.Count == 0, summary);
    }

    public async Task UndoAsync(UndoRecord record, CancellationToken ct)
    {
        var snapshot = DnsRepairFix.FromData(record);

        if (!await _configurator.RestoreAsync(snapshot, ct))
        {
            throw new InvalidOperationException(Strings.Format(Strings.DnsRepairUndoFailed, snapshot.AdapterName));
        }

        await _configurator.FlushCacheAsync(ct);
    }

    private static string Describe(DnsSnapshot snapshot) =>
        snapshot.V4Servers.Count == 0 ? Strings.Get(Strings.DnsCloudflareAutomatic) : string.Join(", ", snapshot.V4Servers);

    private async Task<(List<(AdapterInfo Adapter, DnsSnapshot Snapshot)> Targets, List<AdapterInfo> NoBackup)> PlanAsync(CancellationToken ct)
    {
        var proxyRunning = DpiResidueDetector.IsDnsCryptRunning(_services.GetServicesAndDrivers());
        var targets = new List<(AdapterInfo, DnsSnapshot)>();
        var noBackup = new List<AdapterInfo>();

        foreach (var adapter in _network.GetAdapters().Where(a => a.IsPhysical && a.IsUp))
        {
            if (proxyRunning && adapter.Dns.Any(IPAddress.IsLoopback))
            {
                continue;
            }

            // Without the old servers there would be no way back, so such a connection is reported and left alone.
            var snapshot = await _configurator.ReadAsync(adapter, ct);
            if (snapshot is null)
            {
                noBackup.Add(adapter);
                continue;
            }

            if (!snapshot.V4Servers.SequenceEqual(Servers))
            {
                targets.Add((adapter, snapshot));
            }
        }

        return (targets, noBackup);
    }
}
