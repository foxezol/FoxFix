using System.IO;
using System.Text;

namespace FoxFix.App;

/// <summary>Writes unhandled exceptions to %LocalAppData%\FoxFix\logs so a crash always leaves a trace.</summary>
public static class CrashLog
{
    public static string Folder => Path.Combine(FoxFix.Core.AppPaths.LocalRoot, "logs");

    /// <returns>The log file path, even when writing failed (the path is still worth showing).</returns>
    public static string Write(Exception? exception)
    {
        var path = Path.Combine(Folder, "crash.txt");

        try
        {
            Directory.CreateDirectory(Folder);
            var text = new StringBuilder()
                .AppendLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}")
                .AppendLine(exception?.ToString() ?? "(bilinmeyen hata)")
                .AppendLine(new string('-', 60))
                .ToString();
            File.AppendAllText(path, text, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done if even the log cannot be written.
        }

        return path;
    }
}
