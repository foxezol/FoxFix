using System.Net;
using System.Net.NetworkInformation;

namespace FoxFix.Core.Network;

public interface IPinger
{
    /// <summary>True when the host answered an echo request within the timeout.</summary>
    Task<bool> PingAsync(IPAddress address, TimeSpan timeout, CancellationToken ct);
}

public sealed class SystemPinger : IPinger
{
    public async Task<bool> PingAsync(IPAddress address, TimeSpan timeout, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(address, timeout, cancellationToken: ct);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            // Raised e.g. when no route exists; for our purposes that is "no answer".
            return false;
        }
    }
}
