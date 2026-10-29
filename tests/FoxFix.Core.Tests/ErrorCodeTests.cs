using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class ErrorCodeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FoxFix.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("FoxFix.sln not found above the test folder.");
    }

    [Fact]
    public void Every_code_is_unique_well_formed_and_explained()
    {
        ErrorCodes.All.Select(e => e.Code).Should().OnlyHaveUniqueItems();
        ErrorCodes.All.Should().OnlyContain(e => Regex.IsMatch(e.Code, @"^FX-[1-9]\d{3}$") && e.Title.Length > 5 && e.Remedy.Length > 10);
        ErrorCodes.Find("FX-2001")!.Title.Should().Contain("bütünlüğü");
        ErrorCodes.Find("FX-0000").Should().BeNull();
    }

    [Fact]
    public void Every_code_that_a_message_shows_is_in_the_catalog()
    {
        var resx = File.ReadAllText(Path.Combine(RepoRoot(), "src", "FoxFix.Core", "Resources", "Strings.resx"));
        var used = Regex.Matches(resx, @"\[(FX-\d{4})\]").Select(m => m.Groups[1].Value).Distinct().ToList();

        used.Should().NotBeEmpty();
        used.Should().OnlyContain(code => ErrorCodes.Find(code) != null, "a code in a message must be explained");
    }

    [Fact]
    public void Every_code_is_in_the_table_people_can_read()
    {
        var table = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "HATA-KODLARI.md"));

        foreach (var error in ErrorCodes.All)
        {
            table.Should().Contain($"`{error.Code}`").And.Contain(error.Title, $"{error.Code} is in the code but missing from docs/HATA-KODLARI.md");
        }
    }

    [Fact]
    public void A_message_is_tagged_with_its_code_in_square_brackets()
    {
        ErrorCodes.Tag(ErrorCodes.DpiFilesBroken).Should().Be("[FX-2001] ");
    }
}