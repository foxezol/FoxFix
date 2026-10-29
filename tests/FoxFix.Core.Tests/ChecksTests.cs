using System.Net.NetworkInformation;
using FluentAssertions;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Diagnostics.Checks;
using Xunit;

namespace FoxFix.Core.Tests;

public class AdapterCheckTests
{
    [Fact]
    public async Task Fails_when_no_physical_adapter_exists()
    {
        var virtualOnly = Adapters.Wifi(description: "Hyper-V Virtual Ethernet Adapter");
        var check = new AdapterCheck(new FakeNetworkInfo(virtualOnly));

        var result = await check.RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
    }

    [Fact]
    public async Task Fails_when_all_physical_adapters_are_down()
    {
        var check = new AdapterCheck(new FakeNetworkInfo(Adapters.Wifi(status: OperationalStatus.Down)));

        var result = await check.RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
    }

    [Fact]
    public async Task Passes_when_a_physical_adapter_is_up()
    {
        var check = new AdapterCheck(new FakeNetworkInfo(Adapters.Healthy()));

        var result = await check.RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Ignores_virtual_adapters_that_are_up()
    {
        var virtualUp = Adapters.Wifi(description: "VMware Virtual Ethernet Adapter");
        var check = new AdapterCheck(new FakeNetworkInfo(virtualUp, Adapters.Wifi(status: OperationalStatus.Down)));

        var result = await check.RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
    }
}

public class IpConfigCheckTests
{
    [Fact]
    public async Task Skipped_when_no_adapter_is_up()
    {
        var check = new IpConfigCheck(new FakeNetworkInfo(Adapters.Wifi(status: OperationalStatus.Down)));

        var result = await check.RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Skipped);
    }

    [Fact]
    public async Task Fails_on_apipa_address()
    {
        var adapter = Adapters.Wifi(addresses: ["169.254.12.7"]);
        var check = new IpConfigCheck(new FakeNetworkInfo(adapter));

        var result = await check.RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Summary.Should().Contain("169.254");
    }

    [Fact]
    public async Task Fails_when_there_is_no_ipv4_address()
    {
        var check = new IpConfigCheck(new FakeNetworkInfo(Adapters.Wifi()));

        var result = await check.RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
    }

    [Fact]
    public async Task Warns_when_address_exists_but_gateway_is_missing()
    {
        var adapter = Adapters.Wifi(addresses: ["192.168.1.20"]);
        var check = new IpConfigCheck(new FakeNetworkInfo(adapter));

        var result = await check.RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Warning);
    }

    [Theory]
    [InlineData("192.168.1.20", "192.168.1.1")]
    [InlineData("192.168.0.5", "192.168.0.1")]
    [InlineData("10.0.0.8", "10.0.0.1")]
    public async Task Passes_with_any_private_range_gateway(string address, string gateway)
    {
        var adapter = Adapters.Wifi(addresses: [address], gateways: [gateway]);
        var check = new IpConfigCheck(new FakeNetworkInfo(adapter));

        var result = await check.RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Passed);
        result.Summary.Should().Contain(gateway);
    }
}

public class PingCheckTests
{
    private static PingCheck Create(params string[] reachable) =>
        new(new FakeNetworkInfo(Adapters.Healthy()), new FakePinger(reachable));

    [Fact]
    public async Task Passes_when_internet_answers()
    {
        var result = await Create("192.168.1.1", "8.8.8.8").RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Passed);
        result.Detail.Should().BeNull();
    }

    [Fact]
    public async Task Passes_with_a_note_when_only_the_gateway_is_silent()
    {
        var result = await Create("1.1.1.1").RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Passed);
        result.Detail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Fails_when_only_the_gateway_answers()
    {
        var result = await Create("192.168.1.1").RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
    }

    [Fact]
    public async Task Fails_when_nothing_answers()
    {
        var result = await Create().RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
    }
}
