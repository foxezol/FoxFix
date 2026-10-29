using System.Net;
using FluentAssertions;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Diagnostics.Checks;
using FoxFix.Core.Network;
using Xunit;

namespace FoxFix.Core.Tests;

public class DnsPacketTests
{
    [Fact]
    public void Query_has_the_expected_wire_layout()
    {
        var query = DnsPacket.BuildQuery("google.com", 0x1234);

        query[0..2].Should().Equal(0x12, 0x34);                // id
        query[2..4].Should().Equal(0x01, 0x00);                // recursion desired
        query[4..6].Should().Equal(0x00, 0x01);                // one question
        query[12].Should().Be(6);                              // "google"
        query[13..19].Should().Equal("google"u8.ToArray());
        query[19].Should().Be(3);                              // "com"
        query[^5].Should().Be(0);                              // root label
        query[^4..].Should().Equal(0x00, 0x01, 0x00, 0x01);    // type A, class IN
    }

    [Fact]
    public void Trailing_dot_is_ignored()
    {
        DnsPacket.BuildQuery("google.com.", 1).Should().Equal(DnsPacket.BuildQuery("google.com", 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad..name")]
    public void Invalid_hosts_are_rejected(string host)
    {
        var act = () => DnsPacket.BuildQuery(host, 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_label_over_63_characters_is_rejected()
    {
        var act = () => DnsPacket.BuildQuery(new string('a', 64) + ".com", 1);

        act.Should().Throw<ArgumentException>();
    }

    private static byte[] Response(ushort id, ushort flags, ushort answers)
    {
        var bytes = new byte[12];
        bytes[0] = (byte)(id >> 8);
        bytes[1] = (byte)id;
        bytes[2] = (byte)(flags >> 8);
        bytes[3] = (byte)flags;
        bytes[6] = (byte)(answers >> 8);
        bytes[7] = (byte)answers;
        return bytes;
    }

    [Fact]
    public void A_successful_answer_with_records_is_valid()
    {
        DnsPacket.IsValidAnswer(Response(0xBEEF, 0x8180, 1), 0xBEEF).Should().BeTrue();
    }

    [Fact]
    public void An_answer_for_another_query_is_rejected()
    {
        DnsPacket.IsValidAnswer(Response(0xBEEF, 0x8180, 1), 0x0001).Should().BeFalse();
    }

    [Fact]
    public void A_query_echoed_back_is_not_an_answer()
    {
        DnsPacket.IsValidAnswer(Response(0xBEEF, 0x0100, 1), 0xBEEF).Should().BeFalse();
    }

    [Theory]
    [InlineData(0x8183)] // NXDOMAIN
    [InlineData(0x8182)] // SERVFAIL
    [InlineData(0x8185)] // REFUSED
    public void Error_response_codes_are_rejected(ushort flags)
    {
        DnsPacket.IsValidAnswer(Response(0xBEEF, flags, 1), 0xBEEF).Should().BeFalse();
    }

    [Fact]
    public void An_answer_without_records_is_rejected()
    {
        DnsPacket.IsValidAnswer(Response(0xBEEF, 0x8180, 0), 0xBEEF).Should().BeFalse();
    }

    [Fact]
    public void Truncated_data_is_rejected()
    {
        DnsPacket.IsValidAnswer(new byte[5], 0).Should().BeFalse();
    }
}

public class DnsDecisionTreeTests
{
    [Theory]
    [InlineData(true, true, true, DnsVerdict.Healthy)]
    [InlineData(true, false, false, DnsVerdict.Healthy)]
    [InlineData(false, true, true, DnsVerdict.LocalConfigBroken)]
    [InlineData(false, true, false, DnsVerdict.LocalConfigBroken)]
    [InlineData(false, false, true, DnsVerdict.UdpIntercepted)]
    [InlineData(false, false, false, DnsVerdict.ServerUnreachable)]
    public void Covers_every_combination(bool system, bool udp, bool tcp, DnsVerdict expected)
    {
        DnsDecisionTree.Decide(system, udp, tcp).Should().Be(expected);
    }
}

public class DnsCheckTests
{
    private sealed class FakeDnsProbe : IDnsProbe
    {
        public bool System { get; init; }

        public HashSet<string> UdpServers { get; init; } = [];

        public HashSet<string> TcpServers { get; init; } = [];

        public int DirectCalls { get; private set; }

        public Task<bool> ResolveWithSystemAsync(string host, TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult(System);

        public Task<IReadOnlyList<IPAddress>> ResolveAddressesAsync(string host, TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([]);

        public Task<bool> QueryUdpAsync(IPAddress server, string host, TimeSpan timeout, CancellationToken ct)
        {
            DirectCalls++;
            return Task.FromResult(UdpServers.Contains(server.ToString()));
        }

        public Task<bool> QueryTcpAsync(IPAddress server, string host, TimeSpan timeout, CancellationToken ct)
        {
            DirectCalls++;
            return Task.FromResult(TcpServers.Contains(server.ToString()));
        }
    }

    [Fact]
    public async Task Passes_with_only_the_canary_probe_when_the_system_resolves()
    {
        var probe = new FakeDnsProbe { System = true };

        var result = await new DnsCheck(probe).RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Passed);
        probe.DirectCalls.Should().Be(1);
    }

    [Fact]
    public async Task Reports_a_broken_local_config_when_direct_udp_works()
    {
        var probe = new FakeDnsProbe { UdpServers = ["8.8.8.8"] };

        var result = await new DnsCheck(probe).RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Summary.Should().Be(Strings.Get(Strings.DnsLocalConfig));
    }

    [Fact]
    public async Task Reports_udp_interception_when_only_tcp_works()
    {
        var probe = new FakeDnsProbe { TcpServers = ["8.8.8.8", "1.1.1.1"] };

        var result = await new DnsCheck(probe).RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Summary.Should().Be(Strings.Get(Strings.DnsUdpIntercepted));
        result.Detail.Should().Contain("çalışmıyor");
    }

    [Fact]
    public async Task Reports_unreachable_servers_when_nothing_works()
    {
        var result = await new DnsCheck(new FakeDnsProbe()).RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Summary.Should().Be(Strings.Get(Strings.DnsUnreachable));
    }

    [Fact]
    public async Task Warns_when_a_nonexistent_server_answers_over_udp()
    {
        var probe = new FakeDnsProbe { System = true, UdpServers = ["192.0.2.1"] };

        var result = await new DnsCheck(probe).RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Warning);
        result.Summary.Should().Be(Strings.Get(Strings.DnsRedirected));
    }

    [Fact]
    public async Task A_broken_dns_with_a_redirect_mentions_the_redirect_in_the_detail()
    {
        var probe = new FakeDnsProbe { UdpServers = ["192.0.2.1"], TcpServers = ["8.8.8.8"] };

        var result = await new DnsCheck(probe).RunAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Detail.Should().Contain(Strings.Get(Strings.DnsRedirected));
    }

    [Fact]
    public async Task One_working_server_is_enough_for_the_direct_tests()
    {
        var probe = new FakeDnsProbe { UdpServers = ["1.1.1.1"] };

        var result = await new DnsCheck(probe).RunAsync(CancellationToken.None);

        result.Summary.Should().Be(Strings.Get(Strings.DnsLocalConfig));
    }
}

public class EngineRunsAfterFailureTests
{
    private sealed class Stub : ICheck
    {
        private readonly CheckStatus _status;

        public Stub(string id, CheckStatus status, bool runsAfterFailure = false)
        {
            Id = id;
            _status = status;
            RunsAfterFailure = runsAfterFailure;
        }

        public string Id { get; }

        public string Title => Id;

        public bool RunsAfterFailure { get; }

        public Task<CheckResult> RunAsync(CancellationToken ct) => Task.FromResult(new CheckResult(_status, Id));
    }

    [Fact]
    public async Task Explanatory_checks_still_run_after_a_failure_but_ordinary_ones_do_not()
    {
        var engine = new DiagnosticsEngine(
        [
            new Stub("dns", CheckStatus.Failed),
            new Stub("https", CheckStatus.Passed),
            new Stub("residue", CheckStatus.Warning, runsAfterFailure: true),
        ]);

        var report = await engine.RunAsync(null, CancellationToken.None);

        report.Reports.Select(r => r.CheckId).Should().Equal("dns", "residue");
        report.FirstFailure!.CheckId.Should().Be("dns");
    }
}
