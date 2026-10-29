using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FoxFix.Core.Commands;

public sealed class ProcessCommandRunner : ICommandRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var encoding = ResolveOemEncoding();
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DefaultTimeout);

        var stdOut = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stdErr = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new CommandResult(process.ExitCode, await stdOut, await stdErr);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"'{fileName}' {DefaultTimeout.TotalSeconds:0} saniyede bitmedi.");
        }
    }

    /// <summary>sc/netsh/ipconfig write in the OEM code page (e.g. CP857 on Turkish Windows).</summary>
    private static Encoding ResolveOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }
}
