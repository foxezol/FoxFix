using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FoxFix.Core.Cleanup;

public sealed record CleanupOutcome(long FreedBytes, int SkippedFiles);

/// <summary>One thing that can be cleaned. The list of targets is fixed in code; nothing comes from user input.</summary>
public interface ICleanupTarget
{
    string Id { get; }

    string Title { get; }

    string Description { get; }

    bool NeedsAdmin { get; }

    long MeasureBytes(CancellationToken ct);

    CleanupOutcome Clean(CancellationToken ct);

    /// <summary>The same, telling how many bytes were freed since the last report. Targets that cannot tell just clean.</summary>
    CleanupOutcome Clean(IProgress<long>? freedBytes, CancellationToken ct) => Clean(ct);
}

public static class ByteSize
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"0 B", "512 KB", "1,2 GB". Binary units, one decimal for GB.</summary>
    public static string Format(long bytes)
    {
        const double Kilo = 1024;

        return bytes switch
        {
            < 1024 => $"{Math.Max(bytes, 0)} B",
            < 1024 * 1024 => $"{(bytes / Kilo).ToString("0", Turkish)} KB",
            < 1024L * 1024 * 1024 => $"{(bytes / Kilo / Kilo).ToString("0.#", Turkish)} MB",
            _ => $"{(bytes / Kilo / Kilo / Kilo).ToString("0.0", Turkish)} GB",
        };
    }
}

/// <summary>
/// Measures and empties the CONTENTS of a folder, never the folder itself. Links (junctions, symlinks) are never
/// followed, drive roots and very short paths are refused, files in use are skipped and counted.
/// </summary>
public static class SafeFolderCleaner
{
    private const int MinimumSegments = 3;

    /// <summary>A usable root is absolute and has at least three parts, like C:\Windows\Temp. "C:\" is never one.</summary>
    public static bool IsSafeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            return false;
        }

        var full = Path.GetFullPath(root).TrimEnd('\\', '/');
        return full.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length >= MinimumSegments;
    }

    public static long Measure(string root, CancellationToken ct)
    {
        var directory = OpenRoot(root);
        return directory is null ? 0 : MeasureDirectory(directory, ct);
    }

    public static CleanupOutcome DeleteContents(string root, CancellationToken ct, Action<long>? onFreed = null)
    {
        var directory = OpenRoot(root);
        return directory is null ? new CleanupOutcome(0, 0) : DeleteDirectoryContents(directory, ct, onFreed);
    }

    private static DirectoryInfo? OpenRoot(string root)
    {
        if (!IsSafeRoot(root))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(root));
        return directory.Exists && !IsLink(directory) ? directory : null;
    }

    private static bool IsLink(FileSystemInfo info) => info.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private static long MeasureDirectory(DirectoryInfo directory, CancellationToken ct)
    {
        long total = 0;

        try
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();

                if (IsLink(entry))
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    total += MeasureDirectory(child, ct);
                }
                else if (entry is FileInfo file)
                {
                    total += SafeLength(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder we cannot read counts as empty.
        }

        return total;
    }

    private static CleanupOutcome DeleteDirectoryContents(DirectoryInfo directory, CancellationToken ct, Action<long>? onFreed)
    {
        long freed = 0;
        var skipped = 0;
        List<FileSystemInfo> entries;

        try
        {
            entries = directory.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CleanupOutcome(0, 0);
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (IsLink(entry))
            {
                continue; // never follow, never delete through a link
            }

            if (entry is DirectoryInfo child)
            {
                var inner = DeleteDirectoryContents(child, ct, onFreed);
                freed += inner.FreedBytes;
                skipped += inner.SkippedFiles;
                TryDeleteEmptyDirectory(child);
            }
            else if (entry is FileInfo file)
            {
                var length = SafeLength(file);
                if (TryDelete(file))
                {
                    freed += length;
                    onFreed?.Invoke(length);
                }
                else
                {
                    skipped++;
                }
            }
        }

        return new CleanupOutcome(freed, skipped);
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Attributes = FileAttributes.Normal; // read-only files would otherwise refuse to go
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // in use or protected: leave it
        }
    }

    private static void TryDeleteEmptyDirectory(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not empty (something was skipped) or protected
        }
    }
}

public sealed class FolderTarget : ICleanupTarget
{
    private readonly IReadOnlyList<string> _roots;

    public FolderTarget(string id, string title, string description, bool needsAdmin, params string[] roots)
    {
        Id = id;
        Title = title;
        Description = description;
        NeedsAdmin = needsAdmin;
        _roots = roots;
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public bool NeedsAdmin { get; }

    public long MeasureBytes(CancellationToken ct) => _roots.Sum(r => SafeFolderCleaner.Measure(r, ct));

    public CleanupOutcome Clean(CancellationToken ct) => Clean(null, ct);

    public CleanupOutcome Clean(IProgress<long>? freedBytes, CancellationToken ct)
    {
        long freed = 0;
        var skipped = 0;

        foreach (var root in _roots)
        {
            var outcome = SafeFolderCleaner.DeleteContents(root, ct, freedBytes is null ? null : freedBytes.Report);
            freed += outcome.FreedBytes;
            skipped += outcome.SkippedFiles;
        }

        return new CleanupOutcome(freed, skipped);
    }
}

/// <summary>Real recycle bin through the shell. Needs a hand test: it permanently deletes what is in the bin.</summary>
[SupportedOSPlatform("windows")]
public sealed class RecycleBinTarget : ICleanupTarget
{
    private const uint NoConfirmation = 0x1;
    private const uint NoProgressUi = 0x2;
    private const uint NoSound = 0x4;

    public RecycleBinTarget(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Id => CleanupTargets.RecycleBinId;

    public string Title { get; }

    public string Description { get; }

    public bool NeedsAdmin => false;

    public long MeasureBytes(CancellationToken ct)
    {
        var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.TotalBytes : 0;
    }

    public CleanupOutcome Clean(CancellationToken ct)
    {
        var before = MeasureBytes(ct);
        if (before == 0)
        {
            return new CleanupOutcome(0, 0);
        }

        var result = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        return result == 0
            ? new CleanupOutcome(before, 0)
            : throw new IOException($"Çöp kutusu boşaltılamadı (0x{result:X8}).");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long TotalBytes;
        public long ItemCount;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}

public static class CleanupTargets
{
    public const string RecycleBinId = "recycle-bin";

    /// <summary>The fixed list. Folders come from well-known locations, never from user input.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<ICleanupTarget> Default()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            new FolderTarget("user-temp", Strings.Get(Strings.TargetUserTemp), Strings.Get(Strings.TargetUserTempInfo), false,
                Path.GetTempPath()),
            new FolderTarget("windows-temp", Strings.Get(Strings.TargetWindowsTemp), Strings.Get(Strings.TargetWindowsTempInfo), true,
                Path.Combine(windows, "Temp")),
            new FolderTarget("update-cache", Strings.Get(Strings.TargetUpdateCache), Strings.Get(Strings.TargetUpdateCacheInfo), true,
                Path.Combine(windows, "SoftwareDistribution", "Download")),
            new FolderTarget("browser-cache", Strings.Get(Strings.TargetBrowserCache), Strings.Get(Strings.TargetBrowserCacheInfo), false,
                Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data"),
                Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Code Cache"),
                Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data"),
                Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Code Cache")),
            new RecycleBinTarget(Strings.Get(Strings.TargetRecycleBin), Strings.Get(Strings.TargetRecycleBinInfo)),
        ];
    }
}
