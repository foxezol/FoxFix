using System.Diagnostics;
using FoxFix.Core.Cleanup;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Fixes;

/// <summary>
/// Empties the selected cleanup targets. This cannot be undone and so writes nothing to the undo journal:
/// the preview says so in plain words and the user confirms before anything is deleted.
/// </summary>
public sealed class DiskCleanupFix : IFix
{
    public const string FixIdValue = "disk-cleanup";

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(150);

    private readonly IReadOnlyList<ICleanupTarget> _targets;

    private IReadOnlySet<string> _selected = new HashSet<string>();

    /// <summary>What the preview measured, so the progress bar knows the total without measuring everything twice.</summary>
    private readonly Dictionary<string, long> _measured = [];

    public DiskCleanupFix(IReadOnlyList<ICleanupTarget> targets)
    {
        _targets = targets;
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.CleanupDiskTitle);

    public bool IncludedInRepair => false;

    public bool RequiresAdmin => Selected().Any(t => t.NeedsAdmin);

    public void Select(IEnumerable<string> ids) => _selected = ids.ToHashSet();

    public async Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            return new FixPreview([Strings.Get(Strings.CleanupNothingSelected)], HasChanges: false);
        }

        var sizes = await Task.Run(() => selected.Select(t => (Target: t, Bytes: t.MeasureBytes(ct))).ToList(), ct);

        _measured.Clear();
        foreach (var (target, bytes) in sizes)
        {
            _measured[target.Id] = bytes;
        }

        var lines = sizes
            .Select(s => Strings.Format(Strings.CleanupPreviewItem, s.Target.Title, ByteSize.Format(s.Bytes)))
            .ToList();
        lines.Add(Strings.Format(Strings.CleanupPreviewTotal, ByteSize.Format(sizes.Sum(s => s.Bytes))));
        lines.Add(Strings.Get(Strings.CleanupPreviewIrreversible));

        return new FixPreview(lines);
    }

    public Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct) => ApplyAsync(journal, null, ct);

    public async Task<FixResult> ApplyAsync(UndoJournal journal, IProgress<FixProgress>? progress, CancellationToken ct)
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            return new FixResult(true, Strings.Get(Strings.CleanupNothingSelected));
        }

        // The bar is as honest as the preview's measurement: files that appear meanwhile only make it finish early.
        var total = selected.Sum(t => _measured.GetValueOrDefault(t.Id));

        long freed = 0;
        long finishedBytes = 0;
        var skipped = 0;
        var failures = new List<string>();
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        void Report(long currentTargetFreed, long currentTargetSize, bool force = false)
        {
            if (progress is null || total <= 0 || (!force && clock.Elapsed - lastReport < ReportInterval))
            {
                return;
            }

            lastReport = clock.Elapsed;
            var done = finishedBytes + Math.Min(currentTargetFreed, currentTargetSize);
            progress.Report(new FixProgress(
                Math.Clamp((double)done / total, 0, 1),
                Strings.Format(Strings.CleanupProgress, ByteSize.Format(done), ByteSize.Format(total))));
        }

        foreach (var target in selected)
        {
            var size = _measured.GetValueOrDefault(target.Id);
            long current = 0;
            var tracker = new DelegateProgress(delta => Report(Interlocked.Add(ref current, delta), size));

            try
            {
                var outcome = await Task.Run(() => target.Clean(tracker, ct), ct);
                freed += outcome.FreedBytes;
                skipped += outcome.SkippedFiles;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(Strings.Format(Strings.CleanupFailed, target.Title, ex.Message));
            }

            finishedBytes += size;
            Report(size, size, force: true);
        }

        var summary = Strings.Format(Strings.CleanupResultDone, ByteSize.Format(freed));
        if (skipped > 0)
        {
            summary += Strings.Format(Strings.CleanupResultSkipped, skipped);
        }

        if (failures.Count > 0)
        {
            summary += Environment.NewLine + string.Join(Environment.NewLine, failures);
        }

        return new FixResult(failures.Count == 0, summary);
    }

    public Task UndoAsync(UndoRecord record, CancellationToken ct) =>
        throw new InvalidOperationException(Strings.Get(Strings.CleanupUndoUnsupported));

    private List<ICleanupTarget> Selected() => _targets.Where(t => _selected.Contains(t.Id)).ToList();

    private sealed class DelegateProgress(Action<long> handler) : IProgress<long>
    {
        public void Report(long value) => handler(value);
    }
}
