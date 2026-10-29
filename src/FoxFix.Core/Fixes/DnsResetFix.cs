using System.Net;
using FoxFix.Core.Dpi;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Fixes;

/// <summary>
/// Takes every physical connection back to Windows' own default for DNS: automatic, from the modem. Servers somebody
/// typed in by hand (for example a leftover Yandex or 127.0.0.1) are removed. The old values are saved for "Geri Al".
/// A running DnsCrypt-Proxy is a deliberate setup, so a connection that points at this computer is left alone then.
/// </summary>
public sealed class DnsResetFix : IFix
{
    public const string FixIdValue = "dns-reset";

    private readonly INetworkInfoProvider _network;
    private readonly IServiceSource _services;
    private readonly IDnsConfigurator _configurator;

    public DnsResetFix(INetworkInfoProvider network, IServiceSource services, IDnsConfigurator configurator)
    {
        _network = network;
        _services = services;
        _configurator = configurator;
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.DnsResetTitle);

    public bool IncludedInRepair => false;

    public async Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var targets = await PlanAsync(ct);
        if (targets.Count == 0)
        {
            return new FixPreview([Strings.Get(Strings.DnsResetNothing)], HasChanges: false);
        }

        var lines = targets
            .Select(t => Strings.Format(Strings.DnsResetPreviewAdapter, t.Snapshot.AdapterName, string.Join(", ", t.Snapshot.V4Servers.Concat(t.Snapshot.V6Servers))))
            .ToList();
        lines.Add(Strings.Get(Strings.DnsRepairPreviewFlush));
        lines.Add(Strings.Get(Strings.DnsRepairPreviewUndo));
        return new FixPreview(lines);
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var targets = await PlanAsync(ct);
        if (targets.Count == 0)
        {
            return new FixResult(true, Strings.Get(Strings.DnsResetNothing));
        }

        var changed = 0;
        var failures = new List<string>();

        foreach (var (adapter, snapshot) in targets)
        {
            var record = new UndoRecord(
                Guid.NewGuid(), FixIdValue, DateTimeOffset.UtcNow,
                Strings.Format(Strings.DnsRepairUndoDescription, adapter.Name),
                DnsRepairFix.ToData(snapshot));

            // Journal first, so the old servers can always be put back.
            await journal.AppendAsync(record, ct);

            if (await _configurator.SetAutomaticAsync(adapter.Name, ct))
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
            ? Strings.Format(Strings.DnsRepairResultDone, changed)
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

    private async Task<List<(AdapterInfo Adapter, DnsSnapshot Snapshot)>> PlanAsync(CancellationToken ct)
    {
        var proxyRunning = DpiResidueDetector.IsDnsCryptRunning(_services.GetServicesAndDrivers());
        var targets = new List<(AdapterInfo, DnsSnapshot)>();

        foreach (var adapter in _network.GetAdapters().Where(a => a.IsPhysical && a.IsUp))
        {
            if (proxyRunning && adapter.Dns.Any(IPAddress.IsLoopback))
            {
                continue;
            }

            // Only what was typed in by hand counts: an empty list already is the Windows default.
            var snapshot = await _configurator.ReadAsync(adapter, ct);
            if (snapshot is not null && (snapshot.V4Servers.Count > 0 || snapshot.V6Servers.Count > 0))
            {
                targets.Add((adapter, snapshot));
            }
        }

        return targets;
    }
}
