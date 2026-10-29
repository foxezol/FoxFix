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

    public Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        if (!IsPresent())
        {
            return Task.FromResult(new FixPreview([Strings.Get(Strings.GdpiRemoveNothing)], HasChanges: false));
        }

        // The WinDivert driver stays loaded (and its file locked) until Windows restarts, so with it registered the restart
        // is offered up front, before anything is changed.
        var driverStays = _status.Read().OwnDriverRegistered;
        var lines = new List<string> { Strings.Get(Strings.GdpiRemovePreview) };
        if (driverStays)
        {
            lines.Add(Strings.Get(Strings.PreviewReboot));
        }

        return Task.FromResult(new FixPreview(lines, RequiresReboot: driverStays));
    }

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

        // Files that could not be deleted are removed by Windows only after a restart.
        return new FixResult(true, Strings.Get(clean ? Strings.GdpiRemoveDone : Strings.GdpiRemovePartial), RequiresReboot: !clean);
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
