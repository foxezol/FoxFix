using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FoxFix.Core.Tests;

public class StringsTests
{
    [Fact]
    public void Every_declared_key_exists_in_the_resource_file()
    {
        foreach (var key in Strings.AllKeys())
        {
            var act = () => Strings.Get(key);
            act.Should().NotThrow($"'{key}' must be defined in Strings.resx");
            Strings.Get(key).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Every_key_has_an_English_text_with_the_same_placeholders()
    {
        var placeholder = new Regex(@"\{(\d+)");

        foreach (var key in Strings.AllKeys())
        {
            var turkish = Strings.GetIn(key, Strings.Turkish);
            var english = Strings.GetIn(key, Strings.English);

            english.Should().NotBeNullOrWhiteSpace(key);
            placeholder.Matches(english).Select(m => m.Groups[1].Value).Order()
                .Should().Equal(placeholder.Matches(turkish).Select(m => m.Groups[1].Value).Order(), $"{key} must fill the same values in both languages");
        }
    }

    [Fact]
    public void English_is_really_a_second_file_and_not_the_Turkish_one_again()
    {
        Strings.GetIn(Strings.AdapterTitle, Strings.English).Should().Be("Network connection");
        Strings.GetIn(Strings.AdapterTitle, Strings.Turkish).Should().Be("Ağ bağlantısı");
    }

    [Fact]
    public void Error_codes_keep_their_tag_in_both_languages()
    {
        var tag = new Regex(@"\[FX-\d{4}\]");

        foreach (var key in Strings.AllKeys())
        {
            var turkish = tag.Match(Strings.GetIn(key, Strings.Turkish));
            if (turkish.Success)
            {
                Strings.GetIn(key, Strings.English).Should().Contain(turkish.Value, key);
            }
        }
    }

    [Fact]
    public void Turkish_characters_survive_the_resource_file()
    {
        Strings.Get(Strings.AdapterTitle).Should().Be("Ağ bağlantısı");
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("EN", true)]
    [InlineData("tr", true)]
    [InlineData("", false)]
    [InlineData("de", false)]
    [InlineData(null, false)]
    public void Only_Turkish_and_English_are_known(string? code, bool known)
    {
        Strings.IsKnownLanguage(code).Should().Be(known);
    }
}
