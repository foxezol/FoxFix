namespace FoxFix.Core.GoodbyeDpi;

/// <summary>One ready-made way of starting GoodbyeDPI (or Zapret, see <see cref="Engine"/>). The arguments come from the Turkey build's own batch files.</summary>
public sealed record GoodbyeDpiProfile(string Id, string TitleKey, string Arguments, bool RedirectsDns, string Engine = DpiEngines.GoodbyeDpiId)
{
    public string Title => Strings.Get(TitleKey);
}

public static class GoodbyeDpiProfiles
{
    private const string YandexDns = "--dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253";

    /// <summary>In trial order: the Turkey build's default first, the ones that skip DNS redirection last.</summary>
    public static IReadOnlyList<GoodbyeDpiProfile> All { get; } =
    [
        new("turkey-1", Strings.DpiProfile1, $"-5 --set-ttl 5 {YandexDns}", RedirectsDns: true),
        new("turkey-4", Strings.DpiProfile4, $"-5 {YandexDns}", RedirectsDns: true),
        new("turkey-5", Strings.DpiProfile5, $"-9 {YandexDns}", RedirectsDns: true),
        new("turkey-3", Strings.DpiProfile3, $"--set-ttl 3 {YandexDns}", RedirectsDns: true),
        new("turkey-2", Strings.DpiProfile2, "-5", RedirectsDns: false),
        new("turkey-6", Strings.DpiProfile6, "-9", RedirectsDns: false),
        new("turkey-7", Strings.DpiProfile7, "--set-ttl 3", RedirectsDns: false),
    ];

    /// <summary>Looks in GoodbyeDPI's settings and then in Zapret's: the ids are unique across both.</summary>
    public static GoodbyeDpiProfile? Find(string? id) => All.Concat(ZapretProfiles.All).FirstOrDefault(p => p.Id == id);
}
