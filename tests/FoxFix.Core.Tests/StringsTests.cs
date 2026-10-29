using FluentAssertions;
using Xunit;

namespace FoxFix.Core.Tests;

public class StringsTests
{
    [Fact]
    public void Every_declared_key_exists_in_the_resource_file()
    {
        var keys = typeof(Strings)
            .GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

        foreach (var key in keys)
        {
            var act = () => Strings.Get(key);
            act.Should().NotThrow($"'{key}' must be defined in Strings.resx");
            Strings.Get(key).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Turkish_characters_survive_the_resource_file()
    {
        Strings.Get(Strings.AdapterTitle).Should().Be("Ağ bağlantısı");
    }
}
