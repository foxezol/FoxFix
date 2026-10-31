using System.Net;
using System.Text.Json;
using FluentAssertions;
using FoxFix.Core.Dpi;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;
using FoxFix.Core.Settings;
using FoxFix.Core.Updates;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.9.15", 0, 9, 15)]
    [InlineData("0.9.15", 0, 9, 15)]
    [InlineData("V1.2.3-beta.1", 1, 2, 3)]
    [InlineData("v0.10", 0, 10, 0)]
    [InlineData(" v2.0.0+build5 ", 2, 0, 0)]
    public void Tags_that_look_like_versions_are_read(string tag, int major, int minor, int build)
    {
        ReleaseVersion.TryParse(tag, out var version).Should().BeTrue();
        version.Should().Be(new Version(major, minor, build));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("v1.2.3.4")]
    [InlineData("v1.x.3")]
    [InlineData("v-1.2.3")]
    public void Anything_else_is_refused(string? tag)
    {
        ReleaseVersion.TryParse(tag, out _).Should().BeFalse();
    }

    [Fact]
    public void A_missing_build_number_is_the_same_as_zero()
    {
        ReleaseVersion.Normalize(new Version(0, 9)).Should().Be(new Version(0, 9, 0));
    }
}

public sealed class UpdateCheckerTests
{
    private sealed class Source(Func<Task<ReleaseInfo?>> get) : IReleaseSource
    {
        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct) => get();
    }

    private static ReleaseInfo Release(string tag) =>
        new(ReleaseVersion.TryParse(tag, out var v) ? v : new Version(0, 0, 0), tag, "https://example.test/r", "notlar");

    private static UpdateChecker Checker(Func<Task<ReleaseInfo?>> get) => new(new Source(get));

    [Fact]
    public async Task A_newer_release_is_reported_with_its_version()
    {
        var result = await Checker(() => Task.FromResult<ReleaseInfo?>(Release("v0.9.15"))).CheckAsync(new Version(0, 9, 14), CancellationToken.None);

        result.Status.Should().Be(UpdateStatus.Available);
        result.Latest!.Url.Should().Be("https://example.test/r");
        result.Message.Should().Contain("0.9.15");
    }

    [Theory]
    [InlineData("v0.9.14")]
    [InlineData("v0.9.3")]
    [InlineData("v0.8.99")]
    public async Task The_same_or_an_older_release_is_not_an_update(string tag)
    {
        var result = await Checker(() => Task.FromResult<ReleaseInfo?>(Release(tag))).CheckAsync(new Version(0, 9, 14), CancellationToken.None);

        result.Status.Should().Be(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task Versions_compare_as_numbers_not_as_text()
    {
        var result = await Checker(() => Task.FromResult<ReleaseInfo?>(Release("v0.10.0"))).CheckAsync(new Version(0, 9, 14), CancellationToken.None);

        result.Status.Should().Be(UpdateStatus.Available);
    }

    [Fact]
    public async Task A_four_part_assembly_version_is_compared_by_its_first_three_parts()
    {
        var result = await Checker(() => Task.FromResult<ReleaseInfo?>(Release("v0.9.14"))).CheckAsync(new Version(0, 9, 14, 0), CancellationToken.None);

        result.Status.Should().Be(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task Nothing_published_yet_is_up_to_date()
    {
        var result = await Checker(() => Task.FromResult<ReleaseInfo?>(null)).CheckAsync(new Version(0, 9, 14), CancellationToken.None);

        result.Status.Should().Be(UpdateStatus.UpToDate);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_failed_request_is_a_result_with_a_code_not_an_exception(Exception failure)
    {
        var result = await Checker(() => Task.FromException<ReleaseInfo?>(failure)).CheckAsync(new Version(0, 9, 14), CancellationToken.None);

        result.Status.Should().Be(UpdateStatus.Failed);
        result.Message.Should().StartWith("[FX-6003]");
    }

    public static TheoryData<Exception> Failures() =>
    [
        new HttpRequestException("bağlantı yok"),
        new JsonException("bozuk"),
        new TaskCanceledException("zaman aşımı"),
    ];

    [Fact]
    public async Task Cancelling_it_yourself_is_not_a_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var checker = Checker(() => Task.FromException<ReleaseInfo?>(new OperationCanceledException(cts.Token)));

        var act = () => checker.CheckAsync(new Version(0, 9, 14), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

public sealed class GitHubReleaseSourceTests
{
    [Fact]
    public void Github_s_answer_is_read()
    {
        var release = GitHubReleaseSource.Parse("""{"tag_name":"v0.9.15","html_url":"https://github.com/foxezol/FoxFix/releases/tag/v0.9.15","body":"Yenilikler"}""");

        release.Version.Should().Be(new Version(0, 9, 15));
        release.Tag.Should().Be("v0.9.15");
        release.Url.Should().EndWith("v0.9.15");
        release.Notes.Should().Be("Yenilikler");
    }

    [Fact]
    public void A_missing_body_is_just_empty()
    {
        GitHubReleaseSource.Parse("""{"tag_name":"v1.0.0","html_url":"u","body":null}""").Notes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"tag_name":"yeni","html_url":"u"}""")]
    [InlineData("""{"html_url":"u"}""")]
    [InlineData("kesinlikle json değil")]
    public void A_tag_that_is_not_a_version_or_broken_json_is_refused(string json)
    {
        var act = () => GitHubReleaseSource.Parse(json);

        act.Should().Throw<JsonException>();
    }

    private static string Entry(string tag, bool draft = false, bool prerelease = false) =>
        $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/foxezol/FoxFix/releases/tag/{{tag}}","body":"","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":{{prerelease.ToString().ToLowerInvariant()}}}""";

    [Fact]
    public void A_release_with_a_tag_that_is_not_a_version_is_skipped_not_fatal()
    {
        var release = GitHubReleaseSource.ParseNewest($"[{Entry("Pre-release")},{Entry("v0.9.15")}]");

        release!.Tag.Should().Be("v0.9.15");
    }

    [Fact]
    public void The_highest_version_wins_whatever_the_order()
    {
        var release = GitHubReleaseSource.ParseNewest($"[{Entry("v0.9.15")},{Entry("v0.10.0")},{Entry("v0.9.99")}]");

        release!.Tag.Should().Be("v0.10.0");
    }

    [Fact]
    public void Drafts_and_pre_releases_are_never_offered()
    {
        var release = GitHubReleaseSource.ParseNewest($"[{Entry("v2.0.0", draft: true)},{Entry("v1.5.0", prerelease: true)},{Entry("v1.0.0")}]");

        release!.Tag.Should().Be("v1.0.0");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"tag_name":"Pre-release","html_url":"u"}]""")]
    public void No_usable_release_means_nothing_is_offered(string json)
    {
        GitHubReleaseSource.ParseNewest(json).Should().BeNull();
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.0.0"}""")]
    [InlineData("kesinlikle json değil")]
    public void An_answer_that_is_not_a_list_is_refused(string json)
    {
        var act = () => GitHubReleaseSource.ParseNewest(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task One_oddly_named_release_does_not_make_the_check_fail()
    {
        var source = new GitHubReleaseSource(new HttpClient(new Fixed(HttpStatusCode.OK, $"[{Entry("Pre-release")}]")));
        var checker = new UpdateChecker(source);

        var result = await checker.CheckAsync(new Version(0, 9, 15), CancellationToken.None);

        result.Status.Should().Be(UpdateStatus.UpToDate);
    }
    [Fact]
    public async Task A_404_means_nothing_is_published()
    {
        var source = new GitHubReleaseSource(new HttpClient(new Fixed(HttpStatusCode.NotFound, "")));

        (await source.GetLatestAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task A_server_error_is_thrown_for_the_checker_to_turn_into_a_result()
    {
        var source = new GitHubReleaseSource(new HttpClient(new Fixed(HttpStatusCode.Forbidden, "")));

        var act = () => source.GetLatestAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task The_request_goes_to_the_one_documented_address_and_sends_no_data()
    {
        var handler = new Fixed(HttpStatusCode.OK, """[{"tag_name":"v1.0.0","html_url":"u","body":""}]""");
        var source = new GitHubReleaseSource(new HttpClient(handler));

        await source.GetLatestAsync(CancellationToken.None);

        handler.Seen!.RequestUri!.ToString().Should().Be("https://api.github.com/repos/foxezol/FoxFix/releases?per_page=20");
        handler.Seen.Method.Should().Be(HttpMethod.Get);
        handler.Seen.Content.Should().BeNull();
    }

    [Fact]
    public void The_client_names_the_program_but_not_the_person()
    {
        using var client = GitHubReleaseSource.CreateClient();

        client.DefaultRequestHeaders.UserAgent.ToString().Should().Be("FoxFix");
        client.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(15));
    }

    private sealed class Fixed(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }
}

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-settings-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "ayarlar.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Without_a_file_the_defaults_apply_and_updates_are_checked()
    {
        var settings = new AppSettingsStore(FilePath).Load();

        settings.AutoCheckUpdates.Should().BeTrue();
        settings.Theme.Should().Be(ThemeMode.System);
        settings.AccentColor.Should().Be("#9E20E6");
        settings.RecolorLogo.Should().BeTrue();
    }

    [Fact]
    public void What_was_saved_comes_back()
    {
        var store = new AppSettingsStore(FilePath);
        var chosen = new AppSettings { AutoCheckUpdates = false, Theme = ThemeMode.Dark, AccentColor = "#1fa971", RecolorLogo = false };

        store.Save(chosen).Should().BeTrue();

        var loaded = new AppSettingsStore(FilePath).Load();
        loaded.AutoCheckUpdates.Should().BeFalse();
        loaded.Theme.Should().Be(ThemeMode.Dark);
        loaded.AccentColor.Should().Be("#1FA971");
        loaded.RecolorLogo.Should().BeFalse();
    }

    [Fact]
    public void The_theme_is_stored_as_a_word_people_can_read()
    {
        new AppSettingsStore(FilePath).Save(new AppSettings { Theme = ThemeMode.Light });

        File.ReadAllText(FilePath).Should().Contain("\"Light\"");
    }

    [Theory]
    [InlineData("kırmızı")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("9E20E6")]
    [InlineData("#9E20E6; calc.exe")]
    public void A_colour_that_is_not_a_hex_code_falls_back_to_the_brand_purple(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new { AccentColor = text }));

        new AppSettingsStore(FilePath).Load().AccentColor.Should().Be(AppSettings.DefaultAccent);
    }

    [Fact]
    public void A_damaged_file_gives_the_defaults_instead_of_stopping_the_app()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ bozuk");

        var settings = new AppSettingsStore(FilePath).Load();

        settings.Should().Be(new AppSettings());
    }

    [Fact]
    public void An_unknown_theme_word_falls_back_to_following_windows()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"Theme":"Pembe"}""");

        new AppSettingsStore(FilePath).Load().Theme.Should().Be(ThemeMode.System);
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_reported_not_thrown()
    {
        // A folder where the file should be.
        Directory.CreateDirectory(FilePath);

        new AppSettingsStore(FilePath).Save(new AppSettings()).Should().BeFalse();
    }
}

public sealed class DnsCloudflareFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-cf-tests-" + Guid.NewGuid().ToString("N"));

    private readonly DnsRepairFixTests.FakeDnsMachine _machine = new();

    private UndoJournal Journal => new(Path.Combine(_dir, "undo.json"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private DnsCloudflareFix CreateFix() => new(_machine, _machine, _machine);

    [Fact]
    public void It_is_a_choice_not_a_repair()
    {
        CreateFix().IncludedInRepair.Should().BeFalse();
        CreateFix().Id.Should().Be("dns-cloudflare");
    }

    [Fact]
    public async Task Preview_names_the_connection_and_what_it_had_and_changes_nothing()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["8.8.8.8"]);
        _machine.AddAdapter("Wi-Fi");

        var preview = await CreateFix().PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeTrue();
        preview.Changes.Should().Contain(l => l.Contains("Ethernet 3") && l.Contains("8.8.8.8") && l.Contains("1.1.1.1") && l.Contains("1.0.0.1"));
        preview.Changes.Should().Contain(l => l.Contains("Wi-Fi") && l.Contains("otomatik"));
        _machine.Calls.Should().OnlyContain(c => c.StartsWith("read:"));
    }

    [Fact]
    public async Task Already_set_means_nothing_to_do()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["1.1.1.1", "1.0.0.1"]);
        var fix = CreateFix();

        var preview = await fix.PreviewAsync(CancellationToken.None);
        var result = await fix.ApplyAsync(Journal, CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        result.Success.Should().BeTrue();
        _machine.Calls.Should().NotContain(c => c.StartsWith("static:") || c == "flush");
    }

    [Fact]
    public async Task The_same_servers_in_another_order_are_set_again()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["1.0.0.1", "1.1.1.1"]);

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _machine.Calls.Should().Contain("static:Ethernet 3:1.1.1.1,1.0.0.1");
    }

    [Fact]
    public async Task Apply_saves_the_old_servers_first_then_sets_then_flushes()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["8.8.8.8"], v6: ["2001:4860:4860::8888"]);
        var journal = Journal;

        var result = await CreateFix().ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("1 bağlantının DNS'i 1.1.1.1 ve 1.0.0.1 yapıldı");
        _machine.Calls.Should().Equal("read:Ethernet 3", "static:Ethernet 3:1.1.1.1,1.0.0.1", "flush");
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();
        record.FixId.Should().Be(DnsCloudflareFix.FixIdValue);
        record.Data["adapter"].Should().Be("Ethernet 3");
        record.Data["v4"].Should().Be("8.8.8.8");
        record.Data["v6"].Should().Be("2001:4860:4860::8888");
    }

    [Fact]
    public async Task The_old_setting_is_in_the_journal_before_the_first_change()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["8.8.8.8"]);
        var journal = Journal;
        IReadOnlyList<UndoRecord>? seen = null;
        _machine.OnSetAutomatic = _ => Task.CompletedTask;

        // The fake records the call before it changes anything, so what the journal held at that moment is what counts.
        var changed = false;
        var fix = new DnsCloudflareFix(_machine, _machine, new JournalSpy(_machine, async () =>
        {
            if (!changed)
            {
                changed = true;
                seen = await journal.LoadAsync(CancellationToken.None);
            }
        }));

        await fix.ApplyAsync(journal, CancellationToken.None);

        seen.Should().ContainSingle();
    }

    [Fact]
    public async Task Typed_in_ipv6_servers_are_kept()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["8.8.8.8"], v6: ["2001:4860:4860::8888"]);

        await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        var current = await _machine.ReadAsync(_machine.GetAdapters().Single(), CancellationToken.None);
        current!.V4Servers.Should().Equal("1.1.1.1", "1.0.0.1");
        current.V6Servers.Should().Equal("2001:4860:4860::8888");
    }

    [Fact]
    public async Task Running_twice_is_harmless()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["8.8.8.8"]);
        var fix = CreateFix();
        await fix.ApplyAsync(Journal, CancellationToken.None);
        _machine.Calls.Clear();

        var second = await fix.ApplyAsync(Journal, CancellationToken.None);

        second.Success.Should().BeTrue();
        _machine.Calls.Should().NotContain(c => c.StartsWith("static:"));
    }

    [Fact]
    public async Task Virtual_and_disconnected_connections_are_never_touched()
    {
        _machine.AddAdapter("vEthernet (Default Switch)", description: "Hyper-V Virtual Ethernet Adapter");
        _machine.AddAdapter("Wi-Fi", status: System.Net.NetworkInformation.OperationalStatus.Down);

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _machine.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_running_dnscrypt_proxy_keeps_its_loopback_setup()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["127.0.0.1"]);
        _machine.Services.Add(new ServiceEntry("dnscrypt-proxy", @"C:\dnscrypt\dnscrypt-proxy.exe", false, true));

        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _machine.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_connection_that_cannot_be_backed_up_is_reported_and_left_alone()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["8.8.8.8"]);
        _machine.AddAdapter("Wi-Fi", v4: ["9.9.9.9"]);
        _machine.NoSnapshot.Add("Wi-Fi");

        var preview = await CreateFix().PreviewAsync(CancellationToken.None);
        var result = await CreateFix().ApplyAsync(Journal, CancellationToken.None);

        preview.Changes.Should().Contain(l => l.Contains("Wi-Fi"));
        result.Success.Should().BeFalse();
        _machine.Calls.Should().Contain("static:Ethernet 3:1.1.1.1,1.0.0.1").And.NotContain(c => c.StartsWith("static:Wi-Fi"));
    }

    [Fact]
    public async Task A_failed_change_is_reported_and_its_journal_entry_withdrawn()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["8.8.8.8"]);
        _machine.FailSet.Add("Ethernet 3");
        var journal = Journal;

        var result = await CreateFix().ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty();
        _machine.Calls.Should().NotContain("flush");
    }

    [Fact]
    public async Task Undo_puts_the_old_servers_back()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["8.8.8.8"]);
        var journal = Journal;
        var fix = CreateFix();
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();

        await fix.UndoAsync(record, CancellationToken.None);

        _machine.Restored.Should().ContainSingle().Which.V4Servers.Should().Equal("8.8.8.8");
    }

    [Fact]
    public async Task A_failed_undo_says_so()
    {
        _machine.AddAdapter("Ethernet 3", v4: ["8.8.8.8"]);
        var journal = Journal;
        var fix = CreateFix();
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();
        _machine.FailRestore = true;

        var act = () => fix.UndoAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>Passes everything to the fake machine and calls back just before the first change is made.</summary>
    private sealed class JournalSpy(DnsRepairFixTests.FakeDnsMachine inner, Func<Task> beforeChange) : FoxFix.Core.Network.IDnsConfigurator
    {
        public Task<FoxFix.Core.Network.DnsSnapshot?> ReadAsync(FoxFix.Core.Network.AdapterInfo adapter, CancellationToken ct) => inner.ReadAsync(adapter, ct);

        public Task<bool> SetAutomaticAsync(string adapterName, CancellationToken ct) => inner.SetAutomaticAsync(adapterName, ct);

        public Task<bool> RestoreAsync(FoxFix.Core.Network.DnsSnapshot snapshot, CancellationToken ct) => inner.RestoreAsync(snapshot, ct);

        public Task<bool> FlushCacheAsync(CancellationToken ct) => inner.FlushCacheAsync(ct);

        public async Task<bool> SetStaticAsync(string adapterName, IReadOnlyList<string> v4Servers, IReadOnlyList<string> v6Servers, CancellationToken ct)
        {
            await beforeChange();
            return await inner.SetStaticAsync(adapterName, v4Servers, v6Servers, ct);
        }
    }
}
