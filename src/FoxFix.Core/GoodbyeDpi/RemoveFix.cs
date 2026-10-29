using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;

namespace FoxFix.Core.GoodbyeDpi;

/// <summary>Takes FoxFix's own GoodbyeDPI away: service, running window or process, driver registration, files.</summary>
public sealed class GoodbyeDpiRemoveFix : IFix
{
    public const string FixIdValue = "goodbyedpi-remove";

    private readonly GoodbyeDpiStatusReader _status;
    private readonly GoodbyeDpiTeardown _teardown;
    private readonly string _folder;

    public GoodbyeDpiRemoveFix(GoodbyeDpiStatusReader status, GoodbyeDpiTeardown teardown, string folder)
    {
        _status = status;
        _teardown = teardown;
        _folder = folder;
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.GdpiRemoveTitle);

    public bool IncludedInRepair => false;

    public Task<FixPreview> PreviewAsync(CancellationToken ct) =>
        Task.FromResult(IsPresent()
            ? new FixPreview([Strings.Get(Strings.GdpiRemovePreview)])
            : new FixPreview([Strings.Get(Strings.GdpiRemoveNothing)], HasChanges: false));

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var clean = await _teardown.RemoveAsync(deleteFolder: true, ct);

        // The install is gone, so its "Geri Al" entries have nothing left to undo.
        foreach (var record in await journal.LoadAsync(ct))
        {
            if (record.FixId == GoodbyeDpiInstallFix.FixIdValue)
            {
                await journal.RemoveAsync(record.Id, ct);
            }
        }

        return new FixResult(true, Strings.Get(clean ? Strings.GdpiRemoveDone : Strings.GdpiRemovePartial));
    }

    /// <summary>Removing is not something to undo: installing again is one click on the GoodbyeDPI page.</summary>
    public Task UndoAsync(UndoRecord record, CancellationToken ct) =>
        throw new InvalidOperationException(Strings.Get(Strings.GdpiRemoveUndoUnsupported));

    private bool IsPresent()
    {
        var status = _status.Read();
        return status.IsInstalled || status.IsActive || status.OwnDriverRegistered || status.State is not null || Directory.Exists(_folder);
    }
}
