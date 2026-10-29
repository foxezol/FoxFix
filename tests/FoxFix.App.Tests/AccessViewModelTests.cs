using FluentAssertions;
using FoxFix.Core.GoodbyeDpi;
using FoxFix.Core.Network;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class AccessViewModelTests
{
    private sealed class Probe : IHttpsProbe
    {
        public HashSet<string> Closed { get; } = [];

        public Task<HttpsProbeOutcome> ProbeAsync(Uri url, TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult(Closed.Contains(url.Host) ? HttpsProbeOutcome.TlsFailed : HttpsProbeOutcome.Ok);
    }

    private static AccessViewModel Create(Probe probe) => new(new AccessTester(probe));

    public AccessViewModelTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
    }

    [Fact]
    public async Task It_tries_the_control_site_and_every_site_and_says_how_many_open()
    {
        var probe = new Probe();
        probe.Closed.Add("discord.com");
        var vm = Create(probe);
        vm.Hosts = "discord.com, example.org";

        await vm.RunCommand.ExecuteAsync(null);

        vm.Rows.Select(r => r.Host).Should().Equal("www.microsoft.com", "discord.com", "example.org");
        vm.Rows[0].IsControl.Should().BeTrue();
        vm.Rows.Select(r => r.Kind).Should().Equal("Ok", "Bad", "Ok");
        vm.Rows[1].StatusText.Should().Be("güvenli bağlantı kesildi");
        vm.SummaryText.Should().Be("2 siteden 1 tanesi açılıyor.");
        vm.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task When_even_the_control_site_fails_it_points_at_the_connection_not_the_sites()
    {
        var probe = new Probe();
        probe.Closed.UnionWith(["www.microsoft.com", "discord.com"]);
        var vm = Create(probe);

        await vm.RunCommand.ExecuteAsync(null);

        vm.SummaryText.Should().Contain("Normal bir site bile açılmadı");
    }

    [Fact]
    public async Task An_empty_list_asks_for_a_site_and_tries_nothing()
    {
        var vm = Create(new Probe());
        vm.Hosts = "  ";

        await vm.RunCommand.ExecuteAsync(null);

        vm.Rows.Should().BeEmpty();
        vm.SummaryText.Should().Contain("site adı yaz");
    }

    [Fact]
    public async Task One_site_that_opens_gets_a_short_sentence()
    {
        var vm = Create(new Probe());

        await vm.RunCommand.ExecuteAsync(null);

        vm.SummaryText.Should().Be("Site açılıyor.");
    }
}
