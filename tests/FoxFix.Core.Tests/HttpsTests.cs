using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using FluentAssertions;
using FoxFix.Core.Commands;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Diagnostics.Checks;
using FoxFix.Core.Network;
using Xunit;
using static FoxFix.Core.Network.HttpsProbeOutcome;

namespace FoxFix.Core.Tests;

public class HttpsDecisionTests
{
    [Theory]
    [InlineData(Ok, new[] { Ok, Ok }, HttpsVerdict.Healthy)]
    [InlineData(TlsFailed, new[] { Ok, TlsFailed }, HttpsVerdict.Healthy)]
    [InlineData(HttpsProbeOutcome.Timeout, new[] { Ok, Ok }, HttpsVerdict.Healthy)]
    [InlineData(Ok, new[] { TlsFailed, TlsFailed }, HttpsVerdict.SiteBlocked)]
    [InlineData(Ok, new[] { HttpsProbeOutcome.Timeout, DnsFailed }, HttpsVerdict.SiteBlocked)]
    [InlineData(HttpsProbeOutcome.Timeout, new[] { HttpsProbeOutcome.Timeout, TlsFailed }, HttpsVerdict.Broken)]
    [InlineData(Other, new[] { Other, Other }, HttpsVerdict.Broken)]
    [InlineData(Ok, new[] { CertificateError, CertificateError }, HttpsVerdict.CertificateProblem)]
    [InlineData(HttpsProbeOutcome.Timeout, new[] { CertificateError, HttpsProbeOutcome.Timeout }, HttpsVerdict.CertificateProblem)]
    [InlineData(CertificateError, new[] { HttpsProbeOutcome.Timeout, HttpsProbeOutcome.Timeout }, HttpsVerdict.CertificateProblem)]
    public void Covers_the_combinations(HttpsProbeOutcome ip, HttpsProbeOutcome[] sites, HttpsVerdict expected)
    {
        HttpsDecision.Decide(ip, sites).Should().Be(expected);
    }
}

public class HttpsErrorClassifierTests
{
    [Fact]
    public void A_rejected_certificate_wins_over_any_exception()
    {
        HttpsErrorClassifier.Classify(new HttpRequestException("x"), certificateRejected: true)
            .Should().Be(CertificateError);
    }

    [Fact]
    public void Cancellation_means_timeout()
    {
        HttpsErrorClassifier.Classify(new TaskCanceledException(), false).Should().Be(HttpsProbeOutcome.Timeout);
    }

    [Fact]
    public void Name_resolution_errors_are_dns_failures()
    {
        var ex = new HttpRequestException(HttpRequestError.NameResolutionError, "dns");

        HttpsErrorClassifier.Classify(ex, false).Should().Be(DnsFailed);
    }

    [Fact]
    public void Secure_connection_errors_are_tls_failures()
    {
        var ex = new HttpRequestException(HttpRequestError.SecureConnectionError, "tls");

        HttpsErrorClassifier.Classify(ex, false).Should().Be(TlsFailed);
    }

    [Fact]
    public void A_nested_authentication_exception_is_a_tls_failure()
    {
        var ex = new HttpRequestException("x", new AuthenticationException("handshake"));

        HttpsErrorClassifier.Classify(ex, false).Should().Be(TlsFailed);
    }

    [Fact]
    public void A_connection_reset_during_the_handshake_is_a_tls_failure()
    {
        var ex = new HttpRequestException("x", new IOException("reset", new SocketException((int)SocketError.ConnectionReset)));

        HttpsErrorClassifier.Classify(ex, false).Should().Be(TlsFailed);
    }

    [Fact]
    public void A_socket_timeout_is_a_timeout()
    {
        var ex = new HttpRequestException("x", new SocketException((int)SocketError.TimedOut));

        HttpsErrorClassifier.Classify(ex, false).Should().Be(HttpsProbeOutcome.Timeout);
    }

    [Fact]
    public void Unknown_failures_are_other()
    {
        HttpsErrorClassifier.Classify(new InvalidOperationException("?"), false).Should().Be(Other);
    }
}

public class HttpsCheckTests
{
    private sealed class FakeHttpsProbe : IHttpsProbe
    {
        private readonly Dictionary<string, HttpsProbeOutcome> _outcomes;

        public FakeHttpsProbe(HttpsProbeOutcome ip, HttpsProbeOutcome site)
        {
            _outcomes = new() { ["1.1.1.1"] = ip, ["www.cloudflare.com"] = site, ["www.microsoft.com"] = site };
        }

        public Task<HttpsProbeOutcome> ProbeAsync(Uri url, TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult(_outcomes[url.Host]);
    }

    private static Task<CheckResult> Run(HttpsProbeOutcome ip, HttpsProbeOutcome site) =>
        new HttpsCheck(new FakeHttpsProbe(ip, site)).RunAsync(CancellationToken.None);

    [Fact]
    public async Task Passes_when_sites_open()
    {
        var result = await Run(Ok, Ok);

        result.Status.Should().Be(CheckStatus.Passed);
        result.Detail.Should().BeNull();
    }

    [Fact]
    public async Task Warns_when_only_the_bare_ip_works()
    {
        var result = await Run(Ok, TlsFailed);

        result.Status.Should().Be(CheckStatus.Warning);
        result.Summary.Should().Be(Strings.Get(Strings.HttpsSiteBlocked));
        result.Detail.Should().Contain("(1.1.1.1): açıldı").And.Contain("güvenli bağlantı kesildi");
    }

    [Fact]
    public async Task Fails_when_nothing_opens()
    {
        var result = await Run(HttpsProbeOutcome.Timeout, HttpsProbeOutcome.Timeout);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Summary.Should().Be(Strings.Get(Strings.HttpsBroken));
    }

    [Fact]
    public async Task Fails_with_a_clock_and_antivirus_hint_on_certificate_errors()
    {
        var result = await Run(Ok, CertificateError);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Summary.Should().Be(Strings.Get(Strings.HttpsCertificate));
    }
}

public class ShutdownArgumentsTests
{
    [Fact]
    public void Restart_has_a_countdown_a_comment_and_is_not_forced()
    {
        var arguments = ShutdownArguments.Restart(20, "FoxFix");

        arguments.Should().Equal("/r", "/t", "20", "/c", "FoxFix");
        arguments.Should().NotContain("/f");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(601)]
    public void Out_of_range_delays_are_rejected(int seconds)
    {
        var act = () => ShutdownArguments.Restart(seconds, "x");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Abort_uses_the_abort_switch()
    {
        ShutdownArguments.Abort().Should().Equal("/a");
    }
}
