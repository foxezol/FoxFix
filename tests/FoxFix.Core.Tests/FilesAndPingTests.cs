using System.Security.Cryptography;
using FluentAssertions;
using FoxFix.Core.GoodbyeDpi;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class TrialPingTests
{
    private sealed class Latency : Network.ILatencyProbe
    {
        public List<string> Hosts { get; } = [];

        public Task<double?> AverageMillisecondsAsync(string host, int count, TimeSpan timeout, CancellationToken ct)
        {
            Hosts.Add(host);
            return Task.FromResult<double?>(34.4);
        }
    }

    [Fact]
    public void The_trial_pings_google_and_the_step_carries_the_average()
    {
        GoodbyeDpiTrial.PingHost.Should().Be("google.com");
        new TrialStep("zapret-kablonet", TrialStatus.AllOpened, 1, 1, 34.4).PingMs.Should().Be(34.4);
        new TrialStep("zapret-kablonet", TrialStatus.Testing, 0, 1).PingMs.Should().BeNull();
        new Latency().Hosts.Should().BeEmpty();
    }
}

public sealed class PrepareFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "foxfix-prep-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class LockedPayload : IGoodbyeDpiPayload
    {
        public void WriteTo(string folder) => throw new IOException("The process cannot access the file because it is being used by another process.");
    }

    private static readonly IReadOnlyDictionary<string, string> Hashes =
        new Dictionary<string, string> { ["a.sys"] = Convert.ToHexString(SHA256.HashData("x"u8.ToArray())) };

    [Fact]
    public void A_file_that_is_locked_but_already_correct_is_not_a_problem()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "a.sys"), "x");

        GoodbyeDpiFiles.Prepare(new LockedPayload(), _folder, Hashes).Should().BeEmpty();
    }

    [Fact]
    public void A_file_that_cannot_be_written_and_is_not_there_is_reported_instead_of_crashing()
    {
        GoodbyeDpiFiles.Prepare(new LockedPayload(), _folder, Hashes).Should().Equal("a.sys");
    }
}