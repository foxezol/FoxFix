using System.Text.RegularExpressions;

namespace FoxFix.Core.Dpi;

public sealed record DnsRedirect(string Address, int Port);

/// <summary>Reads GoodbyeDPI's command line (from the service's PathName) to see where it sends DNS.</summary>
public static partial class GoodbyeDpiArguments
{
    private const int DefaultDnsPort = 53;

    [GeneratedRegex(@"--dns-addr(?:\s+|=)(?<addr>[^\s""]+)", RegexOptions.IgnoreCase)]
    private static partial Regex V4Address();

    [GeneratedRegex(@"--dnsv6-addr(?:\s+|=)(?<addr>[^\s""]+)", RegexOptions.IgnoreCase)]
    private static partial Regex V6Address();

    [GeneratedRegex(@"--dns-port(?:\s+|=)(?<port>\d{1,5})", RegexOptions.IgnoreCase)]
    private static partial Regex V4Port();

    [GeneratedRegex(@"--dnsv6-port(?:\s+|=)(?<port>\d{1,5})", RegexOptions.IgnoreCase)]
    private static partial Regex V6Port();

    public static bool TryGetDnsRedirect(string? commandLine, out DnsRedirect redirect)
    {
        redirect = new DnsRedirect("", DefaultDnsPort);
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        var v4 = V4Address().Match(commandLine);
        if (v4.Success)
        {
            redirect = new DnsRedirect(v4.Groups["addr"].Value, ReadPort(V4Port(), commandLine));
            return true;
        }

        var v6 = V6Address().Match(commandLine);
        if (v6.Success)
        {
            redirect = new DnsRedirect(v6.Groups["addr"].Value, ReadPort(V6Port(), commandLine));
            return true;
        }

        return false;
    }

    private static int ReadPort(Regex pattern, string commandLine)
    {
        var match = pattern.Match(commandLine);
        return match.Success && int.TryParse(match.Groups["port"].Value, out var port) && port is > 0 and <= 65535
            ? port
            : DefaultDnsPort;
    }
}
