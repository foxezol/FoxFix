using System.Security;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Tweaks;

/// <summary>
/// Applies the Windows settings the person ticked, or takes them back to the Windows default. Each setting is one entry in the
/// undo journal that holds the old value of every registry value it touches (or that it was not there) and the keys it had to
/// create, so "Geri Al" puts everything back exactly. A setting that already holds is left alone, and one that fails half way is
/// put back at once.
/// </summary>
public sealed class WindowsTweakFix : IFix
{
    public const string FixIdValue = "windows-tweaks";

    private readonly TweakCatalog _catalog;
    private readonly IRegistryStore _store;
    private readonly Func<int> _build;

    private IReadOnlyList<string> _selected = [];
    private IReadOnlyList<string> _reset = [];

    public WindowsTweakFix(TweakCatalog catalog, IRegistryStore store, Func<int>? build = null)
    {
        _catalog = catalog;
        _store = store;
        _build = build ?? (() => Environment.OSVersion.Version.Build);
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.TweaksTitle);

    public bool IncludedInRepair => false;

    public bool RequiresAdmin => Pending().Any(c => c.Entries.Any(e => e.Hive == RegHive.LocalMachine));

    /// <summary>The ids of the settings to apply, in the order they are applied: tick boxes and the chosen option of each group.</summary>
    public void Select(IEnumerable<string> tweakIds) => _selected = tweakIds.Distinct().ToList();

    /// <summary>The ids of settings to take back to the Windows default. Only those that hold now are changed.</summary>
    public void SelectDefaults(IEnumerable<string> tweakIds) => _reset = tweakIds.Distinct().ToList();

    public bool IsApplied(WindowsTweak tweak) => Holds(tweak.Entries);

    /// <summary>True when the Windows default is what is in the registry now.</summary>
    public bool IsDefault(WindowsTweak tweak) => tweak.Defaults is { } defaults && Holds(defaults);

    private bool Holds(IEnumerable<RegistryEntry> entries) => entries.All(e => TweakData.Matches(e, _store.Read(e.Hive, e.Key, e.Name)));

    public Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var pending = Pending();
        if (pending.Count == 0)
        {
            return Task.FromResult(new FixPreview([Strings.Get(Strings.TweaksNothing)], HasChanges: false));
        }

        var lines = pending
            .Select(c => c.Reset ? Strings.Format(Strings.TweaksPreviewReset, c.Tweak.UndoTitle ?? c.Tweak.Title) : c.Tweak.Title)
            .ToList();
        lines.Add(Strings.Get(Strings.TweaksRestartNote));
        lines.Add(Strings.Get(Strings.TweaksUndoNote));
        return Task.FromResult(new FixPreview(lines));
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var pending = Pending();
        if (pending.Count == 0)
        {
            return new FixResult(true, Strings.Get(Strings.TweaksNothing));
        }

        var changed = 0;
        var failures = new List<string>();

        foreach (var change in pending)
        {
            ct.ThrowIfCancellationRequested();

            var snapshot = TweakSnapshot.Take(_store, change.Entries);
            if (snapshot is null)
            {
                failures.Add(Strings.Format(Strings.TweaksUnsupportedValue, change.Tweak.Title));
                continue;
            }

            var record = new UndoRecord(
                Guid.NewGuid(), FixIdValue, DateTimeOffset.UtcNow,
                Strings.Format(Strings.TweaksUndoDescription, change.Tweak.Title),
                snapshot.ToData(change.Tweak.Id));

            // Journal first: the old values are saved before the first one is overwritten.
            await journal.AppendAsync(record, ct);

            try
            {
                foreach (var entry in change.Entries)
                {
                    Apply(_store, entry);
                }

                changed++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
            {
                // Half a setting is worse than none: what was changed is put back and the journal entry withdrawn.
                var undone = snapshot.TryRestore(_store);
                await journal.RemoveAsync(record.Id, ct);
                failures.Add(Strings.Format(undone ? Strings.TweaksFailed : Strings.TweaksFailedNotRestored, change.Tweak.Title, ex.Message));
            }
        }

        var summary = failures.Count == 0
            ? Strings.Format(Strings.TweaksResultDone, changed) + " " + Strings.Get(Strings.TweaksRestartNote)
            : Strings.Format(Strings.TweaksResultPartial, changed, failures.Count) + Environment.NewLine + string.Join(Environment.NewLine, failures);

        return new FixResult(failures.Count == 0, summary);
    }

    public Task UndoAsync(UndoRecord record, CancellationToken ct)
    {
        if (!TweakSnapshot.FromData(record).TryRestore(_store))
        {
            throw new InvalidOperationException(Strings.Format(Strings.TweaksUndoFailed, record.Description));
        }

        return Task.CompletedTask;
    }

    internal static void Apply(IRegistryStore store, RegistryEntry entry)
    {
        if (entry.Kind == RegKind.Delete)
        {
            store.DeleteValue(entry.Hive, entry.Key, entry.Name);
        }
        else
        {
            store.Write(entry.Hive, entry.Key, entry.Name, entry.Kind, entry.Data);
        }
    }

    private sealed record Change(WindowsTweak Tweak, IReadOnlyList<RegistryEntry> Entries, bool Reset);

    /// <summary>Known, suitable for this Windows, and not already in place.</summary>
    private List<Change> Pending()
    {
        var build = _build();
        var changes = new List<Change>();

        foreach (var tweak in _selected.Select(_catalog.Find).OfType<WindowsTweak>())
        {
            if (tweak.AppliesTo(build) && !IsApplied(tweak))
            {
                changes.Add(new Change(tweak, tweak.Entries, Reset: false));
            }
        }

        // Back to the Windows default: only what holds now (so what FoxFix or the person changed), and only when a default is known.
        foreach (var tweak in _reset.Select(_catalog.Find).OfType<WindowsTweak>())
        {
            if (tweak.AppliesTo(build) && tweak.Defaults is { } defaults && IsApplied(tweak) && !changes.Any(c => c.Tweak == tweak))
            {
                changes.Add(new Change(tweak, defaults, Reset: true));
            }
        }

        return changes;
    }
}
