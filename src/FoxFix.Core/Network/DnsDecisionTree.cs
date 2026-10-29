namespace FoxFix.Core.Network;

public enum DnsVerdict
{
    Healthy,

    /// <summary>Direct DNS works but the system's own settings do not.</summary>
    LocalConfigBroken,

    /// <summary>UDP DNS dies while TCP DNS works: something intercepts UDP 53 (DPI tool, filter driver, security software).</summary>
    UdpIntercepted,

    /// <summary>Neither UDP nor TCP reaches any DNS server.</summary>
    ServerUnreachable,
}

/// <summary>The DNS decision tree from plan section 5.1. Assumes ping by IP already succeeded.</summary>
public static class DnsDecisionTree
{
    public static DnsVerdict Decide(bool systemResolves, bool udpDirect, bool tcpDirect)
    {
        if (systemResolves)
        {
            return DnsVerdict.Healthy;
        }

        if (udpDirect)
        {
            return DnsVerdict.LocalConfigBroken;
        }

        return tcpDirect ? DnsVerdict.UdpIntercepted : DnsVerdict.ServerUnreachable;
    }
}
