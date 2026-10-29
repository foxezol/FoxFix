using System.Text.RegularExpressions;

namespace FoxFix.Core.Dpi;

public static partial class ServicePathParser
{
    [GeneratedRegex(@"^(?<path>.*?\.(?:exe|sys))(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex UnquotedPath();

    /// <summary>
    /// Extracts the file name from a service PathName, which may be quoted, unquoted with
    /// spaces and arguments, or a driver path like \??\C:\x\y.sys.
    /// </summary>
    public static string? GetFileName(string? pathName)
    {
        var path = GetPath(pathName);
        if (path is null)
        {
            return null;
        }

        var separator = path.LastIndexOfAny(['\\', '/']);
        var name = separator >= 0 ? path[(separator + 1)..] : path;
        return name.Length == 0 ? null : name;
    }

    public static string? GetPath(string? pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName))
        {
            return null;
        }

        var trimmed = pathName.Trim();

        if (trimmed.StartsWith('"'))
        {
            var close = trimmed.IndexOf('"', 1);
            return close > 1 ? trimmed[1..close] : null;
        }

        var match = UnquotedPath().Match(trimmed);
        return match.Success ? match.Groups["path"].Value : trimmed;
    }
}
