using System.Globalization;
using System.Text;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Dpi;

namespace FoxFix.Core.Reporting;

/// <summary>
/// Turns what FoxFix knows into a plain-text report a user can hand to a friend or a support forum.
/// Personal data is masked by default; the caller decides to show it.
/// </summary>
public static class DiagnosticReportBuilder
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Build(ReportInput input, bool maskPersonalData)
    {
        var text = new StringBuilder();

        AppendHeader(text, input, maskPersonalData);
        AppendSummary(text, input.Checks);
        AppendChecks(text, input.Checks);
        AppendAdapters(text, input.Adapters);
        AppendFindings(text, input.Findings);
        AppendChanges(text, input);

        var report = text.ToString().TrimEnd() + Environment.NewLine;
        return maskPersonalData ? PersonalDataMasker.Mask(report, input.UserName, input.MachineName) : report;
    }

    private static void AppendHeader(StringBuilder text, ReportInput input, bool masked)
    {
        text.AppendLine(Strings.Get(Strings.ReportTitle));
        text.AppendLine(new string('=', Strings.Get(Strings.ReportTitle).Length));
        text.AppendLine(Strings.Format(Strings.ReportCreated, input.Time.ToString("dd.MM.yyyy HH:mm (zzz)", Turkish)));
        text.AppendLine(Strings.Format(Strings.ReportVersion, input.FoxFixVersion));
        text.AppendLine(Strings.Format(Strings.ReportWindows, input.OsDescription));

        if (masked)
        {
            text.AppendLine(Strings.Get(Strings.ReportMasked));
        }
        else
        {
            text.AppendLine(Strings.Format(Strings.ReportUser, input.UserName));
            text.AppendLine(Strings.Format(Strings.ReportMachine, input.MachineName));
            text.AppendLine(Strings.Get(Strings.ReportUnmasked));
        }

        text.AppendLine();
    }

    private static void AppendSummary(StringBuilder text, IReadOnlyList<CheckReport>? checks)
    {
        Section(text, Strings.ReportSectionSummary);

        if (checks is null)
        {
            text.AppendLine(Strings.Get(Strings.ReportNoChecks));
            text.AppendLine();
            return;
        }

        var failure = checks.FirstOrDefault(c => c.Result.Status == CheckStatus.Failed);
        var notes = checks.Count(c => c.Result.Status is CheckStatus.Warning or CheckStatus.Error);

        text.AppendLine(failure is not null
            ? Strings.Format(Strings.ReportSummaryProblem, failure.Title)
            : notes > 0
                ? Strings.Format(Strings.ReportSummaryNotes, notes)
                : Strings.Get(Strings.ReportSummaryOk));
        text.AppendLine();
    }

    private static void AppendChecks(StringBuilder text, IReadOnlyList<CheckReport>? checks)
    {
        if (checks is null)
        {
            return;
        }

        Section(text, Strings.ReportSectionChecks);
        foreach (var check in checks)
        {
            text.AppendLine($"[{StatusText(check.Result.Status)}] {check.Title}: {check.Result.Summary}");

            if (!string.IsNullOrWhiteSpace(check.Result.Detail))
            {
                foreach (var line in check.Result.Detail.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
                {
                    text.AppendLine("    " + line.Trim());
                }
            }
        }

        text.AppendLine();
    }

    private static void AppendAdapters(StringBuilder text, IReadOnlyList<ReportAdapter> adapters)
    {
        Section(text, Strings.ReportSectionAdapters);

        if (adapters.Count == 0)
        {
            text.AppendLine(Strings.Get(Strings.ReportNoAdapters));
            text.AppendLine();
            return;
        }

        foreach (var adapter in adapters)
        {
            var state = Strings.Get(adapter.IsUp ? Strings.ReportAdapterUp : Strings.ReportAdapterDown);
            text.AppendLine($"{adapter.Name} ({adapter.Description}): {state}");

            if (adapter.MacAddress is not null)
            {
                text.AppendLine("    " + Strings.Format(Strings.ReportMac, adapter.MacAddress));
            }

            text.AppendLine("    " + Strings.Format(Strings.ReportIPv4, Join(adapter.IPv4Addresses)));
            text.AppendLine("    " + Strings.Format(Strings.ReportGateway, Join(adapter.Gateways)));
            var dnsKey = adapter.DnsIsStatic switch { true => Strings.ReportDnsStatic, false => Strings.ReportDnsAutomatic, _ => Strings.ReportDns };
            text.AppendLine("    " + Strings.Format(dnsKey, Join(adapter.DnsServers)));
        }

        text.AppendLine();
    }

    private static void AppendFindings(StringBuilder text, IReadOnlyList<ResidueFinding> findings)
    {
        Section(text, Strings.ReportSectionFindings);

        if (findings.Count == 0)
        {
            text.AppendLine(Strings.Get(Strings.ReportNoFindings));
            text.AppendLine();
            return;
        }

        foreach (var finding in findings)
        {
            var certainty = Strings.Get(finding.Confidence == ResidueConfidence.Confirmed ? Strings.ReportFindingConfident : Strings.ReportFindingPossible);
            var handling = Strings.Get(finding.Handling == ResidueHandling.Removable ? Strings.ReportFindingRemovable : Strings.ReportFindingReportOnly);
            var running = Strings.Get(finding.IsActive ? Strings.ReportFindingActive : Strings.ReportFindingInactive);

            text.AppendLine($"- {finding.Message}");
            text.AppendLine($"    {certainty}, {handling}, {running}" + (finding.Path is null ? "" : $", {finding.Path}"));
        }

        text.AppendLine();
    }

    private static void AppendChanges(StringBuilder text, ReportInput input)
    {
        Section(text, Strings.ReportSectionChanges);

        if (input.Changes.Count == 0)
        {
            text.AppendLine(Strings.Get(Strings.ReportNoChanges));
            return;
        }

        foreach (var change in input.Changes.OrderBy(c => c.Timestamp))
        {
            text.AppendLine($"- {change.Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Turkish)}: {change.Description}");
        }
    }

    private static void Section(StringBuilder text, string titleKey)
    {
        var title = Strings.Get(titleKey);
        text.AppendLine(title);
        text.AppendLine(new string('-', title.Length));
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? Strings.Get(Strings.ReportNone) : string.Join(", ", values);

    private static string StatusText(CheckStatus status) => Strings.Get(status switch
    {
        CheckStatus.Passed => Strings.StatusOkLabel,
        CheckStatus.Failed => Strings.StatusBadLabel,
        CheckStatus.Skipped => Strings.StatusSkippedLabel,
        _ => Strings.StatusWarnLabel,
    });
}
