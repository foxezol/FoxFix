using System.Net.NetworkInformation;

namespace FoxFix.Core.Network;

/// <summary>Measures how long a host takes to answer, in milliseconds.</summary>
public interface ILatencyProbe
{
    /// <summary>The average of the replies that came back, or null when none did.</summary>
    Task<double?> AverageMillisecondsAsync(string host, int count, TimeSpan timeout, CancellationToken ct);
}

public sealed class SystemLatencyProbe : ILatencyProbe
{
    public async Task<double?> AverageMillisecondsAsync(string host, int count, TimeSpan timeout, CancellationToken ct)
    {
        var times = new List<long>();

        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var reply = await ping.SendPingAsync(host, timeout, cancellationToken: ct);
                if (reply.Status == IPStatus.Success)
                {
                    times.Add(reply.RoundtripTime);
                }
            }
            catch (PingException)
            {
                // No route or the name did not resolve: that reply simply does not count.
            }
        }

        return times.Count == 0 ? null : times.Average();
    }
}