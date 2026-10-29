using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;

namespace FoxFix.Core.GoodbyeDpi;

public enum UninstallKind
{
    /// <summary>Put there by FoxFixSetup: its own uninstaller removes the program files.</summary>
    Installed,

    /// <summary>The ZIP version: a few known files and folders next to the exe.</summary>
    Portable,

    /// <summary>The program's own location is not known; only what FoxFix created elsewhere is removed.</summary>
    Unknown,
}

/// <summary>
/// What "FoxFix'i kaldır" takes away, and the command that finishes the job after FoxFix has closed. A running program cannot
/// delete its own exe, so the last part is done by a separate command that waits for FoxFix to end first.
/// </summary>
public sealed class UninstallPlan
{
    public const string UninstallerName = "unins000.exe";

    /// <summary>The only files FoxFix's ZIP version puts next to the exe; nothing else in that folder is ever touched.</summary>
    public static readonly IReadOnlyList<string> PortableFiles = ["FoxFix.exe", "FoxDPI.exe", FoxFix.Core.AppPaths.PortableMarker, "README.md"];

    public static readonly IReadOnlyList<string> PortableFolders = ["Licenses", FoxFix.Core.AppPaths.PortableFolderName];

    /// <summary>Seconds the finishing command waits, so FoxFix has certainly closed before files are deleted.</summary>
    public const int WaitSeconds = 8;

    private UninstallPlan(UninstallKind kind, string? programFolder, IReadOnlyList<string> dataFolders)
    {
        Kind = kind;
        ProgramFolder = programFolder;
        DataFolders = dataFolders;
    }

    public UninstallKind Kind { get; }

    public string? ProgramFolder { get; }

    /// <summary>FoxFix's own data folders that exist on this computer (the logs, the undo journal, the GoodbyeDPI copy).</summary>
    public IReadOnlyList<string> DataFolders { get; }

    public static UninstallPlan Create(string? exePath, string localRoot, string sharedRoot)
    {
        var programFolder = string.IsNullOrEmpty(exePath) ? null : Path.GetDirectoryName(Path.GetFullPath(exePath));

        var data = new[] { localRoot, sharedRoot }
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsOwnDataFolderName)
            .Where(d => programFolder is null || !Contains(d, programFolder))
            .ToList();

        var kind = programFolder is null
            ? UninstallKind.Unknown
            : File.Exists(Path.Combine(programFolder, UninstallerName)) ? UninstallKind.Installed
            : File.Exists(Path.Combine(programFolder, FoxFix.Core.AppPaths.PortableMarker)) ? UninstallKind.Portable
            : UninstallKind.Unknown;

        return new UninstallPlan(kind, programFolder, data);
    }

    /// <summary>
    /// The command line (for <c>cmd /d /s /c</c>) that waits, removes the data folders and then the program. Null when there is
    /// nothing to finish, or when a path holds a character cmd would treat specially: then nothing is run and the person is told.
    /// </summary>
    public string? BuildFinishCommand()
    {
        var paths = DataFolders.Concat(ProgramFolder is null ? [] : [ProgramFolder]).ToList();
        if (paths.Any(p => p.IndexOfAny(['%', '^', '"', '!']) >= 0))
        {
            return null;
        }

        var steps = new List<string> { $"ping -n {WaitSeconds + 1} 127.0.0.1 >nul" };
        foreach (var folder in DataFolders.Where(d => ProgramFolder is null || !Contains(ProgramFolder, d)))
        {
            steps.Add($"rmdir /s /q \"{folder}\"");
        }

        switch (Kind)
        {
            case UninstallKind.Installed:
                steps.Add($"\"{Path.Combine(ProgramFolder!, UninstallerName)}\" /SILENT /NORESTART");
                break;

            case UninstallKind.Portable:
                foreach (var folder in PortableFolders)
                {
                    steps.Add($"rmdir /s /q \"{Path.Combine(ProgramFolder!, folder)}\"");
                }

                foreach (var file in PortableFiles)
                {
                    steps.Add($"del /f /q \"{Path.Combine(ProgramFolder!, file)}\"");
                }

                // Only succeeds when nothing else is in the folder.
                steps.Add($"rmdir \"{ProgramFolder}\"");
                break;

            default:
                if (DataFolders.Count == 0)
                {
                    return null;
                }

                break;
        }

        return string.Join(" & ", steps);
    }

    /// <summary>FoxFix only deletes folders that carry its own name, never an arbitrary path.</summary>
    private static bool IsOwnDataFolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return name.Equals("FoxFix", StringComparison.OrdinalIgnoreCase)
            || name.Equals(FoxFix.Core.AppPaths.PortableFolderName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string outer, string inner)
    {
        var o = Path.GetFullPath(outer).TrimEnd('\\') + "\\";
        var i = Path.GetFullPath(inner).TrimEnd('\\') + "\\";
        return i.StartsWith(o, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// "FoxFix'i kaldır": takes FoxDPI away (window, service, driver, files), the start-with-Windows task and FoxFix's data, then starts the
/// command that removes the program itself once FoxFix has closed. It cannot be undone, so it says so in the preview and
/// leaves nothing in the undo journal.
/// </summary>
public sealed class UninstallFix : IFix
{
    public const string FixIdValue = "foxfix-uninstall";

    private readonly FoxDpiAutoStart _autoStart;
    private readonly GoodbyeDpiTeardown _teardown;
    private readonly Func<Task> _stopSession;
    private readonly UninstallPlan _plan;
    private readonly Func<string, bool> _startFinisher;
    private readonly Action _exitApp;

    /// <param name="startFinisher">Starts the finishing command detached; true when it started.</param>
    /// <param name="exitApp">Closes FoxFix a few seconds later, so the result can still be read.</param>
    public UninstallFix(
        FoxDpiAutoStart autoStart,
        GoodbyeDpiTeardown teardown,
        Func<Task> stopSession,
        UninstallPlan plan,
        Func<string, bool> startFinisher,
        Action exitApp)
    {
        _autoStart = autoStart;
        _teardown = teardown;
        _stopSession = stopSession;
        _plan = plan;
        _startFinisher = startFinisher;
        _exitApp = exitApp;
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.UninstallTitle);

    public bool IncludedInRepair => false;

    public Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var lines = new List<string>
        {
            Strings.Get(Strings.UninstallPreviewDpi),
            Strings.Get(Strings.UninstallPreviewTask),
        };

        if (_plan.DataFolders.Count > 0)
        {
            lines.Add(Strings.Format(Strings.UninstallPreviewData, string.Join(", ", _plan.DataFolders)));
        }

        lines.Add(Strings.Get(_plan.Kind switch
        {
            UninstallKind.Installed => Strings.UninstallPreviewInstalled,
            UninstallKind.Portable => Strings.UninstallPreviewPortable,
            _ => Strings.UninstallPreviewManual,
        }));
        lines.Add(Strings.Get(Strings.UninstallIrreversible));

        return Task.FromResult(new FixPreview(lines));
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        await _stopSession();
        await _autoStart.DisableAsync(ct);
        var clean = await _teardown.RemoveAsync(deleteFolder: true, ct);

        var command = _plan.BuildFinishCommand();
        var started = command is not null && _startFinisher(command);

        var lines = new List<string>();
        if (!clean)
        {
            lines.Add(Strings.Get(Strings.GdpiRemovePartial));
        }

        if (started)
        {
            lines.Add(Strings.Get(_plan.Kind == UninstallKind.Installed ? Strings.UninstallResultInstalled : Strings.UninstallResultClosing));
            _exitApp();
        }
        else
        {
            lines.Add(Strings.Get(Strings.UninstallResultManual));
        }

        return new FixResult(true, string.Join(Environment.NewLine, lines), RequiresReboot: !clean);
    }

    /// <summary>A removal is not undone: the program would have to be installed again.</summary>
    public Task UndoAsync(UndoRecord record, CancellationToken ct) =>
        throw new InvalidOperationException(Strings.Get(Strings.GdpiRemoveUndoUnsupported));
}