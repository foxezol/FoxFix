using System.Net;
using System.Text.RegularExpressions;

namespace FoxFix.Core.Reporting;

/// <summary>
/// Hides what identifies a person or a device before a report is shared (plan section 5.6):
/// the Windows user name, the computer name, MAC addresses and IP addresses. Addresses that say something about the
/// diagnosis and about nobody in particular stay: well-known public DNS servers, loopback (a DNS pointing at 127.0.0.1
/// is a finding), the unspecified address and subnet masks.
/// </summary>
public static partial class PersonalDataMasker
{
    public const string MaskedMac = "XX-XX-XX-XX-XX-XX";

    /// <summary>Names shorter than this are not masked: "al" or "pc" would shred ordinary words.</summary>
    private const int MinimumNameLength = 3;

    private static readonly HashSet<string> PublicDns = new(StringComparer.OrdinalIgnoreCase)
    {
        "1.1.1.1", "1.0.0.1", "8.8.8.8", "8.8.4.4", "9.9.9.9", "149.112.112.112",
        "208.67.222.222", "208.67.220.220", "94.140.14.14", "94.140.15.15",
        "2606:4700:4700::1111", "2606:4700:4700::1001", "2001:4860:4860::8888", "2001:4860:4860::8844",
    };

    [GeneratedRegex(@"\b[0-9A-Fa-f]{2}(?:[-:][0-9A-Fa-f]{2}){5}\b")]
    private static partial Regex MacAddress();

    [GeneratedRegex(@"(?<![\w.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\w.])")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"(?<![\w:])(?:(?:[0-9A-Fa-f]{1,4}:){3,7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,7}:(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?|::(?:[0-9A-Fa-f]{1,4}:){0,6}[0-9A-Fa-f]{1,4})(?:%\w+)?(?![\w:])")]
    private static partial Regex Ipv6();

    public static string Mask(string text, string userName, string machineName)
    {
        var masked = MacAddress().Replace(text, MaskedMac);
        masked = Ipv4().Replace(masked, MaskAddress);
        masked = Ipv6().Replace(masked, MaskAddress);
        masked = ReplaceWholeWord(masked, userName, Strings.Get(Strings.ReportMaskUser));
        return ReplaceWholeWord(masked, machineName, Strings.Get(Strings.ReportMaskMachine));
    }

    private static string MaskAddress(Match match)
    {
        var value = match.Value;
        var bare = value.Contains('%') ? value[..value.IndexOf('%')] : value;

        if (PublicDns.Contains(bare) || !IPAddress.TryParse(bare, out var address))
        {
            return value;
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || IsSubnetMask(address))
        {
            return value;
        }

        return Strings.Get(Strings.ReportMaskAddress);
    }

    /// <summary>255.255.255.0 is not an address of anyone: its ones come first and then only zeros.</summary>
    private static bool IsSubnetMask(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        var bits = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        return bits != 0 && (~bits & (~bits + 1)) == 0;
    }

    /// <summary>Whole-word and case-insensitive, so "ali" hides "ALI" and "C:\Users\ali\" but not "kalite".</summary>
    private static string ReplaceWholeWord(string text, string name, string replacement)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < MinimumNameLength)
        {
            return text;
        }

        var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name.Trim())}(?![\p{{L}}\p{{N}}])";
        return Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}