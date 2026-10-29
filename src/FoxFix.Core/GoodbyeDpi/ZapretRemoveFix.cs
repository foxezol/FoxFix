using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;

namespace FoxFix.Core.GoodbyeDpi;

/// <summary>Takes FoxFix's own Zapret away: the service, the running program, the WinDivert driver registered from its folder, and the files.</summary>
public sealed class ZapretRemoveFix : IFix
{
    public const string FixIdValue = "zapret-remove";

    private readonly GoodbyeDpiTeardown _teardown;
    private readonly IGoodbyeDpiLauncher _launcher;
    private readonly string _folder;

    public ZapretRemoveFix(GoodbyeDpiTeardown teardown, IGoodbyeDpiLauncher launcher, string folder)
    {
        _teardown = teardown;
        _launcher = launcher;
        _folder = folder;
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.ZapretRemoveTitle);

    public bool IncludedInRepair => false;

    public Task<FixPreview> PreviewAsync(CancellationToken ct) =>
        Task.FromResult(Directory.Exists(_folder) || _launcher.IsRunningFromFolder(_folder)
            ? new FixPreview([Strings.Get(Strings.ZapretRemovePreview)])
            : new FixPreview([Strings.Get(Strings.ZapretRemoveNothing)], HasChanges: false));

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var clean = await _teardown.RemoveAsync(deleteFolder: true, ct);

        // The install is gone, so its "Geri Al" entries have nothing left to undo.
        foreach (var record in await journal.LoadAsync(ct))
        {
            if (record.FixId == ZapretLayout.InstallFixId)
            {
                await journal.RemoveAsync(record.Id, ct);
            }
        }

        return new FixResult(true, Strings.Get(clean ? Strings.ZapretRemoveDone : Strings.GdpiRemovePartial));
    }

    /// <summary>Removing is not something to undo: starting Zapret again copies the files back.</summary>
    public Task UndoAsync(UndoRecord record, CancellationToken ct) =>
        throw new InvalidOperationException(Strings.Get(Strings.GdpiRemoveUndoUnsupported));
}