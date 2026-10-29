namespace FoxFix.Core.Diagnostics;

public sealed record DiagnosticsReport(IReadOnlyList<CheckReport> Reports)
{
    public CheckReport? FirstFailure => Reports.FirstOrDefault(r => r.Result.Status == CheckStatus.Failed);

    public bool Healthy => FirstFailure is null;
}

/// <summary>
/// Runs the layered checks in order and stops at the first failed layer,
/// because the layers above a broken one are meaningless.
/// </summary>
public sealed class DiagnosticsEngine
{
    private readonly IReadOnlyList<ICheck> _checks;

    public DiagnosticsEngine(IEnumerable<ICheck> checks)
    {
        _checks = checks.ToList();
    }

    public async Task<DiagnosticsReport> RunAsync(IProgress<CheckReport>? progress, CancellationToken ct)
    {
        var reports = new List<CheckReport>();
        var layerFailed = false;

        foreach (var check in _checks)
        {
            ct.ThrowIfCancellationRequested();

            if (layerFailed && !check.RunsAfterFailure)
            {
                continue;
            }

            var report = new CheckReport(check.Id, check.Title, await RunOneAsync(check, ct));
            reports.Add(report);
            progress?.Report(report);

            layerFailed |= report.Result.Status == CheckStatus.Failed;
        }

        return new DiagnosticsReport(reports);
    }

    private static async Task<CheckResult> RunOneAsync(ICheck check, CancellationToken ct)
    {
        try
        {
            // Checks ask Windows things (adapters, services); on the thread pool they cannot freeze the window.
            return await Task.Run(() => check.RunAsync(ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CheckResult(CheckStatus.Error, Strings.Format(Strings.EngineError, ex.Message), ex.ToString());
        }
    }
}
