using FluentAssertions;
using FoxFix.App;
using Xunit;

namespace FoxFix.App.Tests;

public class ElevationGateTests
{
    [Fact]
    public void With_administrator_rights_nothing_stops_the_program()
    {
        ElevationGate.BlockingMessage(isElevated: true).Should().BeNull();
    }

    [Fact]
    public void Without_them_the_user_is_told_in_plain_words_what_to_do_and_the_code_is_5()
    {
        var message = ElevationGate.BlockingMessage(isElevated: false);

        message.Should().Be("Bu uygulama yönetici yetkileri gerektiriyor. Lütfen sağ tıklayıp 'Yönetici Olarak Çalıştır' seçeneğini kullanın. (Hata Kodu: 5)");
        ElevationGate.ExitCode.Should().Be(5);
    }
}