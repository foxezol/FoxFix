using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Tweaks;

/// <summary>The Windows settings as they were when the person saved them, so that they can be put back if something goes wrong.</summary>
public sealed record TweakBackup(string Id, string Name, DateTimeOffset CreatedAt, int Build, TweakSnapshot Snapshot);

/// <param name="Unreadable">Names of saved files that could not be read; they are shown, not hidden.</param>
public sealed record BackupListing(IReadOnlyList<TweakBackup> Backups, IReadOnlyList<string> Unreadable);

/// <summary>Where the saved settings live: one small JSON file each, in a folder of FoxFix's own.</summary>
public sealed class TweakBackupStore
{
    public const string FolderName = "ayar-yedekleri";

    private static readonly Regex SafeId = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.Compiled);

    private readonly string _folder;

    public TweakBackupStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(AppPaths.LocalRoot, FolderName);
    }

    public string Folder => _folder;

    /// <summary>An id that is also a safe file name: letters, digits and dashes only, so it can never point outside the folder.</summary>
    public static bool IsValidId(string? id) => id is not null && SafeId.IsMatch(id);

    public static string NewId(DateTimeOffset now) => $"yedek-{now:yyyyMMdd-HHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()}";

    public BackupListing List()
    {
        var backups = new List<TweakBackup>();
        var unreadable = new List<string>();

        if (!Directory.Exists(_folder))
        {
            return new BackupListing(backups, unreadable);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(_folder, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BackupListing(backups, [_folder]);
        }

        foreach (var file in files)
        {
            var backup = Read(file);
            if (backup is null)
            {
                unreadable.Add(Path.GetFileName(file));
            }
            else
            {
                backups.Add(backup);
            }
        }

        return new BackupListing(backups.OrderByDescending(b => b.CreatedAt).ToList(), unreadable);
    }

    public TweakBackup? Find(string id) => IsValidId(id) && File.Exists(PathFor(id)) ? Read(PathFor(id)) : null;

    /// <returns>Null on success, otherwise why it could not be saved.</returns>
    public string? Save(TweakBackup backup)
    {
        if (!IsValidId(backup.Id))
        {
            return Strings.Get(Strings.BackupInvalidId);
        }

        try
        {
            Directory.CreateDirectory(_folder);

            // Written to a side file and moved into place, so a crash never leaves half a backup.
            var temp = PathFor(backup.Id) + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(backup, TweakSnapshot.Json));
            File.Move(temp, PathFor(backup.Id), overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return ex.Message;
        }
    }

    /// <returns>Null on success, otherwise why it could not be deleted. Only a file with a valid id inside this folder is ever removed.</returns>
    public string? Delete(string id)
    {
        if (!IsValidId(id))
        {
            return Strings.Get(Strings.BackupInvalidId);
        }

        try
        {
            // Nothing there is nothing to delete (File.Delete would complain if even the folder is missing).
            if (File.Exists(PathFor(id)))
            {
                File.Delete(PathFor(id));
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private string PathFor(string id) => Path.Combine(_folder, id + ".json");

    private static TweakBackup? Read(string file)
    {
        try
        {
            var backup = JsonSerializer.Deserialize<TweakBackup>(File.ReadAllText(file), TweakSnapshot.Json);
            return backup is { Snapshot: not null } && IsValidId(backup.Id) && Path.GetFileNameWithoutExtension(file) == backup.Id ? backup : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

public sealed record BackupResult(bool Success, string Message, TweakBackup? Backup = null);

/// <summary>"Şu anki ayarları kaydet": reads every registry value the Windows settings could touch and keeps it.</summary>
public sealed class TweakBackupService
{
    private readonly TweakCatalog _catalog;
    private readonly IRegistryStore _registry;
    private readonly TweakBackupStore _files;
    private readonly Func<int> _build;
    private readonly Func<DateTimeOffset> _now;

    public TweakBackupService(TweakCatalog catalog, IRegistryStore registry, TweakBackupStore files, Func<int>? build = null, Func<DateTimeOffset>? now = null)
    {
        _catalog = catalog;
        _registry = registry;
        _files = files;
        _build = build ?? (() => Environment.OSVersion.Version.Build);
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public BackupResult Create(string? name = null)
    {
        var now = _now();
        var build = _build();

        // Every value any setting writes (or would write to go back to the default), once each.
        var entries = _catalog.Tweaks
            .Where(t => t.AppliesTo(build))
            .SelectMany(t => t.Entries.Concat(t.Defaults ?? []))
            .ToList();

        var snapshot = TweakSnapshot.Take(_registry, entries);
        if (snapshot is null)
        {
            return new BackupResult(false, Strings.Format(Strings.BackupSaveFailed, Strings.Get(Strings.BackupUnsupportedValue)));
        }

        var finalName = string.IsNullOrWhiteSpace(name) ? Strings.Format(Strings.BackupDefaultName, now.LocalDateTime) : name.Trim();
        var backup = new TweakBackup(TweakBackupStore.NewId(now), finalName, now, build, snapshot);

        return _files.Save(backup) is { } error
            ? new BackupResult(false, Strings.Format(Strings.BackupSaveFailed, error))
            : new BackupResult(true, Strings.Format(Strings.BackupSaved, finalName), backup);
    }
}

/// <summary>
/// Puts saved Windows settings back: every value in the backup is written as it was (or removed when it was not there). What it
/// replaces is first saved in the undo journal, so even a restore can be undone, and a restore that fails half way is put back at once.
/// </summary>
public sealed class TweakRestoreFix : IFix
{
    public const string FixIdValue = "windows-tweaks-restore";

    private readonly TweakBackupStore _files;
    private readonly IRegistryStore _store;

    private string? _backupId;

    public TweakRestoreFix(TweakBackupStore files, IRegistryStore store)
    {
        _files = files;
        _store = store;
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.RestoreTitle);

    public bool IncludedInRepair => false;

    public bool RequiresAdmin => Load()?.Snapshot.NeedsAdmin ?? true;

    public void Select(string backupId) => _backupId = backupId;

    private TweakBackup? Load() => _backupId is null ? null : _files.Find(_backupId);

    public Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var backup = Load();
        if (backup is null)
        {
            return Task.FromResult(new FixPreview([Strings.Get(Strings.BackupNotFound)], HasChanges: false));
        }

        var different = backup.Snapshot.CountDifferences(_store);
        if (different == 0)
        {
            return Task.FromResult(new FixPreview([Strings.Get(Strings.RestoreNothing)], HasChanges: false));
        }

        return Task.FromResult(new FixPreview(
        [
            Strings.Format(Strings.RestorePreviewMain, backup.Name, backup.CreatedAt.LocalDateTime),
            Strings.Format(Strings.RestorePreviewCount, different),
            Strings.Get(Strings.TweaksRestartNote),
            Strings.Get(Strings.TweaksUndoNote),
        ]));
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var backup = Load();
        if (backup is null)
        {
            return new FixResult(false, Strings.Get(Strings.BackupNotFound));
        }

        if (backup.Snapshot.CountDifferences(_store) == 0)
        {
            return new FixResult(true, Strings.Get(Strings.RestoreNothing));
        }

        // What the backup writes, as entries: a saved value is written, a value that was not there is removed.
        var entries = backup.Snapshot.Values
            .Select(v => v.Exists
                ? new RegistryEntry(v.Hive, v.Key, v.Name, v.Kind, v.Data)
                : new RegistryEntry(v.Hive, v.Key, v.Name, RegKind.Delete, ""))
            .ToList();

        var before = TweakSnapshot.Take(_store, entries);
        if (before is null)
        {
            return new FixResult(false, Strings.Format(Strings.RestoreFailed, backup.Name, Strings.Get(Strings.BackupUnsupportedValue)));
        }

        var record = new UndoRecord(
            Guid.NewGuid(), FixIdValue, DateTimeOffset.UtcNow,
            Strings.Format(Strings.RestoreUndoDescription, backup.Name),
            before.ToData(backup.Id));

        // Journal first: what is there now is saved before the first value is replaced.
        await journal.AppendAsync(record, ct);

        try
        {
            foreach (var entry in entries)
            {
                WindowsTweakFix.Apply(_store, entry);
            }

            // Keys that did not exist when the backup was made go again, when nothing else is in them.
            foreach (var key in Enumerable.Reverse(backup.Snapshot.CreatedKeys))
            {
                _store.DeleteKeyIfEmpty(key.Hive, key.Key);
            }

            return new FixResult(true, Strings.Format(Strings.RestoreResultDone, backup.Name) + " " + Strings.Get(Strings.TweaksRestartNote));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
        {
            var undone = before.TryRestore(_store);
            await journal.RemoveAsync(record.Id, ct);
            return new FixResult(false, Strings.Format(undone ? Strings.RestoreFailed : Strings.RestoreFailedNotRestored, backup.Name, ex.Message));
        }
    }

    public Task UndoAsync(UndoRecord record, CancellationToken ct)
    {
        if (!TweakSnapshot.FromData(record).TryRestore(_store))
        {
            throw new InvalidOperationException(Strings.Format(Strings.TweaksUndoFailed, record.Description));
        }

        return Task.CompletedTask;
    }
}
