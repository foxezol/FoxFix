using System.IO;
using System.Reflection;
using FoxFix.Core.GoodbyeDpi;

namespace FoxFix.App;

/// <summary>Writes the GoodbyeDPI files that travel inside FoxFix.exe. The caller checks them against the pinned hashes.</summary>
public sealed class EmbeddedGoodbyeDpiPayload : IGoodbyeDpiPayload
{
    private readonly string _prefix;

    /// <param name="prefix">The start of the resource names: "GoodbyeDpi." or "Zapret.".</param>
    public EmbeddedGoodbyeDpiPayload(string prefix = "GoodbyeDpi.")
    {
        _prefix = prefix;
    }

    public void WriteTo(string folder)
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(_prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var file = File.Create(Path.Combine(folder, resource[_prefix.Length..]));
            stream.CopyTo(file);
        }
    }
}
