using FoxFix.Core.Dpi;

namespace FoxFix.Core.Diagnostics.Checks;

/// <summary>
/// Layer 6: leftovers of DPI tools. Only a loopback DNS with nobody answering is a hard failure;
/// everything else is a warning, because a running tool may be intentional.
/// </summary>
public sealed class DpiResidueCheck : ICheck
{
    private readonly DpiResidueDetector _detector;

    public DpiResidueCheck(DpiResidueDetector detector)
    {
        _detector = detector;
    }

    public string Id => "dpi-residue";

    // A DNS or HTTPS failure is exactly when the user needs to hear about leftover DPI tools.
    public bool RunsAfterFailure => true;

    public string Title => Strings.Get(Strings.ResidueTitle);

    public Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var findings = _detector.Scan();
        if (findings.Count == 0)
        {
            return Task.FromResult(new CheckResult(CheckStatus.Passed, Strings.Get(Strings.ResidueNone)));
        }

        var detail = string.Join(Environment.NewLine, findings.Select(f => f.Message));

        if (findings.Any(f => f.RuleId == "dns-loopback-orphan"))
        {
            return Task.FromResult(new CheckResult(CheckStatus.Failed, Strings.Get(Strings.ResidueDnsPoisoned), detail));
        }

        return Task.FromResult(new CheckResult(CheckStatus.Warning, Strings.Format(Strings.ResidueFound, findings.Count), detail));
    }
}
