using FoxFix.Core.Safety;
using FoxFix.Core.Startup;

namespace FoxFix.Core.Fixes;

/// <summary>
/// Switches programs on or off at Windows start. Nothing is deleted: only the StartupApproved flag changes,
/// the same way Task Manager does it. The old flag goes into the undo journal before it is changed.
/// </summary>
public sealed class StartupFix : IFix
{
    public const string FixIdValue = "startup";

    private readonly IStartupStore _store;
    private readonly Func<DateTime> _utcNow;

    private IReadOnlyDictionary<string, bool> _desired = new Dictionary<string, bool>();

    public StartupFix(IStartupStore store, Func<DateTime>? utcNow = null)
    {
        _store = store;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.StartupTitle);

    public bool IncludedInRepair => false;

    public bool RequiresAdmin => Pending().Any(e => e.NeedsAdmin);

    /// <summary>Entry id mapped to whether it should start with Windows. Entries already in that state are ignored.</summary>
    public void SetDesired(IReadOnlyDictionary<string, bool> desired) => _desired = desired;

    public Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var pending = Pending();
        if (pending.Count == 0)
        {
            return Task.FromResult(new FixPreview([Strings.Get(Strings.StartupNoChanges)], HasChanges: false));
        }

        var lines = pending
            .Select(e => Strings.Format(_desired[e.Id] ? Strings.StartupPreviewEnable : Strings.StartupPreviewDisable, e.Name))
            .ToList();
        lines.Add(Strings.Get(Strings.StartupPreviewNote));

        return Task.FromResult(new FixPreview(lines));
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var pending = Pending();
        if (pending.Count == 0)
        {
            return new FixResult(true, Strings.Get(Strings.StartupNothingToDo));
        }

        var changed = 0;
        var failures = new List<string>();

        foreach (var entry in pending)
        {
            var enable = _desired[entry.Id];
            var previous = _store.ReadApproval(entry);

            var record = new UndoRecord(
                Guid.NewGuid(), FixIdValue, DateTimeOffset.UtcNow,
                Strings.Format(Strings.StartupUndoDescription, entry.Name),
                new Dictionary<string, string>
                {
                    ["source"] = entry.Source.ToString(),
                    ["name"] = entry.Name,
                    ["command"] = entry.Command,
                    ["hadValue"] = (previous is not null).ToString(),
                    ["previous"] = previous ?? "",
                });

            // Journal first: the old flag is saved before it is overwritten.
            await journal.AppendAsync(record, ct);

            try
            {
                var raw = enable ? StartupApproval.EnabledValue() : StartupApproval.DisabledValue(_utcNow());
                _store.WriteApproval(entry, Convert.ToBase64String(raw));
                changed++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                await journal.RemoveAsync(record.Id, ct);
                failures.Add(Strings.Format(Strings.StartupFailed, entry.Name, ex.Message));
            }
        }

        var summary = failures.Count == 0
            ? Strings.Format(Strings.StartupResultDone, changed)
            : Strings.Format(Strings.StartupResultPartial, changed, failures.Count) + Environment.NewLine + string.Join(Environment.NewLine, failures);

        return new FixResult(failures.Count == 0, summary);
    }

    public Task UndoAsync(UndoRecord record, CancellationToken ct)
    {
        var entry = new StartupEntry(Field(record, "name"), Field(record, "command"), ParseSource(Field(record, "source")), true);
        var hadValue = Field(record, "hadValue") == bool.TrueString;

        _store.WriteApproval(entry, hadValue ? Field(record, "previous") : null);
        return Task.CompletedTask;
    }

    private List<StartupEntry> Pending()
    {
        return _store.List()
            .Where(e => _desired.TryGetValue(e.Id, out var wanted) && wanted != e.Enabled)
            .ToList();
    }

    private static StartupSource ParseSource(string value) =>
        Enum.TryParse<StartupSource>(value, out var source)
            ? source
            : throw new InvalidOperationException(Strings.Format(Strings.UndoRecordBroken, "source"));

    private static string Field(UndoRecord record, string key) =>
        record.Data.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(Strings.Format(Strings.UndoRecordBroken, key));
}
