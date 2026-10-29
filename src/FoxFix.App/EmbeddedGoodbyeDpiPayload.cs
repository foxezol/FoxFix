using System.IO;
using System.Reflection;
using FoxFix.Core.GoodbyeDpi;

namespace FoxFix.App;

/// <summary>Writes the GoodbyeDPI files that travel inside FoxFix.exe. The caller checks them against the pinned hashes.</summary>
public sealed class EmbeddedGoodbyeDpiPayload : IGoodbyeDpiPayload
{
    private readonly string _prefix;

    /// <param name="prefix">The start of the resource names: "GoodbyeDpi.".</param>
    public EmbeddedGoodbyeDpiPayload(string prefix = "GoodbyeDpi.")
    {
        _prefix = prefix;
    }

    private static bool FileEquals(string path, byte[] bytes)
    {
        try
        {
            return new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void WriteTo(string folder)
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(_prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var target = Path.Combine(folder, resource[_prefix.Length..]);

            // A file that is already there and identical is left alone: the WinDivert driver stays loaded (and locked)
            // after a run, and writing over it would fail for no reason.
            if (File.Exists(target) && FileEquals(target, bytes))
            {
                continue;
            }

            File.WriteAllBytes(target, bytes);
        }
    }
}
