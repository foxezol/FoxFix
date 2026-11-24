using FluentAssertions;
using FoxFix.App;
using FoxFix.Core;
using FoxFix.Core.Diagnostics;
using Xunit;

namespace FoxFix.App.Tests;

public class RepairComparisonTests
{
    private static CheckReport Check(string id, CheckStatus status) => new(id, "Başlık " + id, new CheckResult(status, "özet", ""));

    private static DiagnosticsReport Report(params CheckReport[] reports) => new(reports);

    [Fact]
    public void Only_the_checks_that_changed_are_listed()
    {
        var before = Report(Check("link", CheckStatus.Passed), Check("dns", CheckStatus.Warning), Check("dpi-residue", CheckStatus.Warning));
        var after = Report(Check("link", CheckStatus.Passed), Check("dns", CheckStatus.Passed), Check("dpi-residue", CheckStatus.Warning));

        var lines = RepairComparison.Changed(before, after);

        lines.Should().ContainSingle();
        lines[0].Title.Should().Be("Başlık dns");
        lines[0].Before.Should().Be(CheckStatus.Warning);
        lines[0].After.Should().Be(CheckStatus.Passed);
        lines[0].Improved.Should().BeTrue();
    }

    [Fact]
    public void A_check_that_got_worse_is_listed_and_not_called_improved()
    {
        var lines = RepairComparison.Changed(Report(Check("dns", CheckStatus.Passed)), Report(Check("dns", CheckStatus.Failed)));

        lines.Should().ContainSingle().Which.Improved.Should().BeFalse();
    }

    [Fact]
    public void A_check_that_only_one_of_the_two_runs_has_is_left_out()
    {
        var lines = RepairComparison.Changed(Report(Check("dns", CheckStatus.Warning)), Report(Check("https", CheckStatus.Passed)));

        lines.Should().BeEmpty();
    }

    [Fact]
    public void The_text_says_each_change_or_that_nothing_changed()
    {
        var changed = RepairComparison.Text(
            RepairComparison.Changed(Report(Check("dns", CheckStatus.Warning)), Report(Check("dns", CheckStatus.Passed))));
        var same = RepairComparison.Text([]);

        changed.Should().Be(Strings.Format(Strings.CompareLine, "Başlık dns", RepairComparison.Label(CheckStatus.Warning), RepairComparison.Label(CheckStatus.Passed)));
        same.Should().Be(Strings.Get(Strings.CompareSame));
    }
}
