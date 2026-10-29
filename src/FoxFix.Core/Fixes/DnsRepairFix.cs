using System.Globalization;
using System.Net;
using FoxFix.Core.Dpi;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Fixes;

/// <summary>
/// Puts an adapter back on automatic DNS when it points at this very computer (127.0.0.1 / ::1) and no
/// DnsCrypt-Proxy is running to answer. That is the "DNS poisoning" left behind by a removed DNS proxy.
/// A running DnsCrypt-Proxy means a deliberate setup and is never touched (plan section 16.5).
/// </summary>
public sealed class DnsRepairFix : IFix
{
    public const string FixId = "dns-repair";

    private const char ListSeparator = ',';

    private readonly INetworkInfoProvider _network;
    private readonly IServiceSource _services;
    private readonly IDnsConfigurator _configurator;

    public DnsRepairFix(INetworkInfoProvider network, IServiceSource services, IDnsConfigurator configurator)
    {
        _network = network;
        _services = services;
        _configurator = configurator;
    }

    public string Id => FixId;

    public string Title => Strings.Get(Strings.DnsRepairTitle);

    public Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var (targets, proxyRunning, protectedAdapters) = Plan();
        var lines = new List<string>();

        if (targets.Count == 0)
        {
            lines.Add(Strings.Get(proxyRunning && protectedAdapters.Count > 0 ? Strings.DnsRepairProxyRunning : Strings.DnsRepairNothing));
            return Task.FromResult(new FixPreview(lines, HasChanges: false));
        }

        foreach (var adapter in targets)
        {
            lines.Add(Strings.Format(Strings.DnsRepairPreviewAdapter, adapter.Name, string.Join(", ", adapter.Dns)));
        }

        lines.Add(Strings.Get(Strings.DnsRepairPreviewFlush));
        lines.Add(Strings.Get(Strings.DnsRepairPreviewUndo));
        return Task.FromResult(new FixPreview(lines));
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var (targets, _, _) = Plan();
        if (targets.Count == 0)
        {
            return new FixResult(true, Strings.Get(Strings.DnsRepairResultNothing));
        }

        var changed = 0;
        var failures = new List<string>();

        foreach (var adapter in targets)
        {
            var snapshot = await _configurator.ReadAsync(adapter, ct);
            if (snapshot is null)
            {
                failures.Add(Strings.Format(Strings.DnsRepairNoBackup, adapter.Name));
                continue;
            }

            var record = new UndoRecord(
                Guid.NewGuid(), FixId, DateTimeOffset.UtcNow,
                Strings.Format(Strings.DnsRepairUndoDescription, adapter.Name),
                ToData(snapshot));

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
        var snapshot = FromData(record);

        if (!await _configurator.RestoreAsync(snapshot, ct))
        {
            throw new InvalidOperationException(Strings.Format(Strings.DnsRepairUndoFailed, snapshot.AdapterName));
        }

        await _configurator.FlushCacheAsync(ct);
    }

    private (List<AdapterInfo> Targets, bool ProxyRunning, List<AdapterInfo> Protected) Plan()
    {
        var proxyRunning = DpiResidueDetector.IsDnsCryptRunning(_services.GetServicesAndDrivers());

        var loopback = _network.GetAdapters()
            .Where(a => a.IsPhysical && a.IsUp && a.Dns.Any(IPAddress.IsLoopback))
            .ToList();

        return proxyRunning ? ([], true, loopback) : (loopback, false, []);
    }

    internal static Dictionary<string, string> ToData(DnsSnapshot snapshot) => new()
    {
        ["adapter"] = snapshot.AdapterName,
        ["v4"] = string.Join(ListSeparator, snapshot.V4Servers),
        ["v6"] = string.Join(ListSeparator, snapshot.V6Servers),
    };

    internal static DnsSnapshot FromData(UndoRecord record)
    {
        return new DnsSnapshot(Field(record, "adapter"), Split(Field(record, "v4")), Split(Field(record, "v6")));
    }

    private static List<string> Split(string value) =>
        value.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Field(UndoRecord record, string key)
    {
        return record.Data.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(Strings.Format(Strings.UndoRecordBroken, key));
    }
}
