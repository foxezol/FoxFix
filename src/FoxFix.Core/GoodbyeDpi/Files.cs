using System.Security.Cryptography;
using System.Text.Json;
using FoxFix.Core.Dpi;

namespace FoxFix.Core.GoodbyeDpi;

/// <summary>Where FoxFix keeps its own copy of GoodbyeDPI, and how to tell it apart from anyone else's.</summary>
public static class GoodbyeDpiLayout
{
    public const string ServiceName = "FoxFixGoodbyeDPI";
    public const string ExeName = "goodbyedpi.exe";

    /// <summary>The WinDivert driver is registered under one of these names depending on its version.</summary>
    public static readonly IReadOnlyList<string> DriverNames = ["WinDivert", "WinDivert14", "WinDivert1.4"];

    public static string DefaultFolder => Path.Combine(AppPaths.SharedRoot, "GoodbyeDPI");

    public static string ExePath(string folder) => Path.Combine(folder, ExeName);

    /// <summary>The folder that holds everything FoxFix owns (the install folder and the trial copy sit next to each other in it).</summary>
    public static string OwnRoot(string folder) => Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd('\\')) ?? folder;

    /// <summary>True when the path (or a service command line) points inside FoxFix's own folder.</summary>
    public static bool IsManaged(string? pathOrCommandLine, string folder)
    {
        // Drivers are registered as \??\C:\... (the NT form of the path).
        var text = pathOrCommandLine?.TrimStart();
        if (text is not null && text.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        var path = ServicePathParser.GetPath(text);
        if (path is null)
        {
            return false;
        }

        var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static string ServiceCommandLine(string folder, GoodbyeDpiProfile profile) =>
        $"\"{ExePath(folder)}\" {profile.Arguments}";
}

/// <summary>Source of the bundled GoodbyeDPI 0.2.3rc3 (Turkey build) files. Implemented by the app from its embedded resources.</summary>
public interface IGoodbyeDpiPayload
{
    void WriteTo(string folder);
}

/// <summary>The files are pinned by SHA-256: anything that differs from the build that was reviewed is refused.</summary>
public static class GoodbyeDpiFiles
{
    public static readonly IReadOnlyDictionary<string, string> Sha256 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["goodbyedpi.exe"] = "8d412b094bb9c137ff25ba9a794d1122ecc84bb776debff6c249723a13cc31cd",
        ["WinDivert.dll"] = "6110bfa44667405179c3e15e12af1b62037e447ed59b054b19042032995e6c7e",
        ["WinDivert64.sys"] = "e69b5ba3f0cd6cfb2983e442636e7f0b342b61b15264b0328317d4559c82cf50",
    };

    /// <summary>Names of the pinned files that are missing or have a different hash. Empty means all good.</summary>
    public static IReadOnlyList<string> FindProblems(string folder, IReadOnlyDictionary<string, string>? expected = null)
    {
        var problems = new List<string>();

        foreach (var (name, expectedHash) in expected ?? Sha256)
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
                problems.Add(name);
                continue;
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(name);
            }
        }

        return problems;
    }

    /// <summary>Copies the bundled files in and checks them. Returns the problem file names (empty when fine).</summary>
    public static IReadOnlyList<string> Prepare(IGoodbyeDpiPayload payload, string folder, IReadOnlyDictionary<string, string>? expected = null)
    {
        Directory.CreateDirectory(folder);

        try
        {
            payload.WriteTo(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be written (a driver still loaded from here, antivirus) is only a problem if it is not
            // already the right one; the check below decides that.
        }

        return FindProblems(folder, expected);
    }
}

/// <summary>Which setting FoxFix's GoodbyeDPI was last set up or started with, kept next to the files.</summary>
public sealed record GoodbyeDpiState(string ProfileId)
{
    private const string FileName = "foxfix-state.json";

    public static GoodbyeDpiState? Read(string folder)
    {
        try
        {
            var path = Path.Combine(folder, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<GoodbyeDpiState>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(string folder) => File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this));
}
