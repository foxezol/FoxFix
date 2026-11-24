using System.IO;
using System.Media;
using System.Text;

namespace FoxFix.App;

/// <summary>
/// The test sound: a short, soft sine tone made in memory and played on the default output with the system's own sound player.
/// Nothing is written to disk.
/// </summary>
public sealed class SoundPlayerToneTester : IToneTester
{
    private const int SampleRate = 44100;
    private const double Frequency = 660;
    private const double Seconds = 1.6;

    private SoundPlayer? _player;

    public Task PlayAsync(CancellationToken ct)
    {
        var wave = BuildWave();
        var player = new SoundPlayer(new MemoryStream(wave));
        _player = player;

        // PlaySync blocks, so it runs off the window's thread; Stop() ends it from outside.
        return Task.Run(
            () =>
            {
                try
                {
                    player.Load();
                    player.PlaySync();
                }
                finally
                {
                    player.Dispose();
                    if (ReferenceEquals(_player, player))
                    {
                        _player = null;
                    }
                }
            },
            ct);
    }

    public void Stop() => _player?.Stop();

    /// <summary>A 16-bit mono WAV file: a sine tone that fades in and out so it never clicks.</summary>
    public static byte[] BuildWave()
    {
        var samples = (int)(SampleRate * Seconds);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples * 2);

        for (var i = 0; i < samples; i++)
        {
            var t = (double)i / SampleRate;
            var fade = Math.Min(1.0, Math.Min(t / 0.08, (Seconds - t) / 0.12));
            writer.Write((short)(Math.Sin(2 * Math.PI * Frequency * t) * 0.25 * fade * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }
}
