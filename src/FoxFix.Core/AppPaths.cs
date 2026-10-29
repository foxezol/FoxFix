namespace FoxFix.Core;

/// <summary>
/// Where FoxFix keeps its own files. Normally under %LocalAppData% (journal, logs, choices) and %ProgramData% (the GoodbyeDPI
/// copy). If a file named <see cref="PortableMarker"/> sits next to FoxFix.exe (the ZIP package has one, and the setup can
/// add it), everything goes into a <c>Veri</c> folder next to the exe instead, so deleting that folder removes all traces.
/// </summary>
public static class AppPaths
{
    public const string PortableMarker = "FoxFix.portable";
    public const string PortableFolderName = "Veri";

    private static readonly Lazy<string?> Portable = new(() => PortableRootFor(Path.GetDirectoryName(Environment.ProcessPath)));

    /// <summary>The data folder next to the exe when the marker is there; null otherwise.</summary>
    public static string? PortableRoot => Portable.Value;

    public static bool IsPortable => PortableRoot is not null;

    /// <summary>Journal, logs and small choices.</summary>
    public static string LocalRoot => PortableRoot
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoxFix");

    /// <summary>Files that belong to the machine rather than to one user: the GoodbyeDPI copy.</summary>
    public static string SharedRoot => PortableRoot
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FoxFix");

    public static string? PortableRootFor(string? exeDirectory) =>
        !string.IsNullOrEmpty(exeDirectory) && File.Exists(Path.Combine(exeDirectory, PortableMarker))
            ? Path.Combine(exeDirectory, PortableFolderName)
            : null;
}
