using System.Buffers.Binary;
using System.Text;

namespace FoxFix.Core.Network;

/// <summary>Minimal DNS wire format: one A-record question, and a strict "is this a real answer" check.</summary>
public static class DnsPacket
{
    private const int HeaderLength = 12;
    private const ushort RecursionDesired = 0x0100;
    private const ushort ResponseFlag = 0x8000;
    private const ushort ResponseCodeMask = 0x000F;

    public static byte[] BuildQuery(string host, ushort id)
    {
        var labels = host.TrimEnd('.').Split('.');
        if (labels.Any(l => l.Length is 0 or > 63))
        {
            throw new ArgumentException($"Geçersiz alan adı: {host}", nameof(host));
        }

        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteUInt16BigEndian(header[0..], id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], RecursionDesired);
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1); // QDCOUNT
        stream.Write(header);

        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(tail[0..], 1); // TYPE A
        BinaryPrimitives.WriteUInt16BigEndian(tail[2..], 1); // CLASS IN
        stream.Write(tail);

        return stream.ToArray();
    }

    /// <summary>True only for a response to our query that succeeded and carries at least one answer.</summary>
    public static bool IsValidAnswer(ReadOnlySpan<byte> response, ushort expectedId)
    {
        if (response.Length < HeaderLength)
        {
            return false;
        }

        var id = BinaryPrimitives.ReadUInt16BigEndian(response[0..]);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(response[2..]);
        var answers = BinaryPrimitives.ReadUInt16BigEndian(response[6..]);

        return id == expectedId
            && (flags & ResponseFlag) != 0
            && (flags & ResponseCodeMask) == 0
            && answers > 0;
    }
}
