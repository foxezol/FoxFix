using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FoxFix.Core.SpeedTest;

/// <summary>
/// Cloudflare's public speed test endpoints (speed.cloudflare.com/__down and /__up). Only run when the user
/// presses the button. Latency is the TCP connect time to port 443, which is a clean round trip.
/// Needs a hand test on other networks; checked once against the real endpoints.
/// </summary>
public sealed class CloudflareSpeedTransport : ISpeedTestTransport, IDisposable
{
    public const string Host = "speed.cloudflare.com";

    private readonly HttpClient _http;
    private IPAddress? _address;

    public CloudflareSpeedTransport()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 16,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<double> MeasureLatencyMsAsync(CancellationToken ct)
    {
        _address ??= await ResolveAsync(ct);

        using var socket = new Socket(_address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        var stopwatch = Stopwatch.StartNew();
        await socket.ConnectAsync(_address, 443, ct);
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    public async Task DownloadAsync(long bytes, Action<long> onBytes, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"https://{Host}/__down?bytes={bytes}", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            onBytes(read);
        }
    }

    public async Task UploadAsync(long bytes, Action<long> onBytes, CancellationToken ct)
    {
        using var content = new CountingContent(bytes, onBytes);
        using var response = await _http.PostAsync($"https://{Host}/__up", content, ct);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();

    private static async Task<IPAddress> ResolveAsync(CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(Host, ct);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException($"{Host} adresi bulunamadı.");
    }

    /// <summary>Random-looking payload written in blocks, reporting every block as it is handed to the socket.</summary>
    private sealed class CountingContent : HttpContent
    {
        private const int BlockSize = 64 * 1024;

        private static readonly byte[] Block = CreateBlock();

        private readonly long _length;
        private readonly Action<long> _onBytes;

        public CountingContent(long length, Action<long> onBytes)
        {
            _length = length;
            _onBytes = onBytes;
            Headers.ContentType = new("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            long remaining = _length;
            while (remaining > 0)
            {
                var size = (int)Math.Min(BlockSize, remaining);
                await stream.WriteAsync(Block.AsMemory(0, size), cancellationToken);
                _onBytes(size);
                remaining -= size;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        private static byte[] CreateBlock()
        {
            var block = new byte[BlockSize];
            Random.Shared.NextBytes(block);
            return block;
        }
    }
}
