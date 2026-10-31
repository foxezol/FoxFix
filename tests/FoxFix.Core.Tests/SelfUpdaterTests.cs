using System.IO.Compression;
using System.Security.Cryptography;
using FluentAssertions;
using FoxFix.Core.GoodbyeDpi;
using FoxFix.Core.Updates;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class ReleaseAssetParsingTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void The_files_of_a_release_are_read_with_the_hash_github_published()
    {
        var json = $$"""
            {"tag_name":"v0.9.16","html_url":"u","body":"",
             "assets":[
               {"name":"FoxFixSetup.exe","browser_download_url":"https://github.com/foxezol/FoxFix/releases/download/v0.9.16/FoxFixSetup.exe","size":71000000,"digest":"sha256:{{Hash}}"},
               {"name":"notlar.txt","browser_download_url":"https://github.com/x","size":12,"digest":null}
             ]}
            """;

        var release = GitHubReleaseSource.Parse(json);

        release.Assets.Should().HaveCount(2);
        release.Assets[0].Should().Be(new ReleaseAsset("FoxFixSetup.exe", "https://github.com/foxezol/FoxFix/releases/download/v0.9.16/FoxFixSetup.exe", 71000000, Hash));
        release.Assets[1].Sha256.Should().BeNull();
    }

    [Theory]
    [InlineData("md5:abcd")]
    [InlineData("")]
    public void Only_a_sha256_digest_counts(string digest)
    {
        var json = $$"""{"tag_name":"v1.0.0","html_url":"u","assets":[{"name":"a","browser_download_url":"b","size":1,"digest":"{{digest}}"}]}""";

        GitHubReleaseSource.Parse(json).Assets.Single().Sha256.Should().BeNull();
    }

    [Fact]
    public void A_release_without_files_has_an_empty_list()
    {
        GitHubReleaseSource.Parse("""{"tag_name":"v1.0.0","html_url":"u"}""").Assets.Should().BeEmpty();
        new ReleaseInfo(new Version(1, 0, 0), "v1.0.0", "u", "").Assets.Should().BeEmpty();
    }
}

public sealed class SelfUpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "foxfix-selfupdate-" + Guid.NewGuid().ToString("N"));

    private string Work => Path.Combine(_root, "FoxFix", "guncelleme");

    private string Program => Path.Combine(_root, "program");

    public void Dispose()
    {
        foreach (var folder in new[] { _root, _root + "%PATH%" }.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class Downloader(Func<byte[]> content) : IPackageDownloader
    {
        public List<string> Urls { get; } = [];

        public Exception? Failure { get; set; }

        public Task DownloadAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct)
        {
            Urls.Add(url);
            if (Failure is not null)
            {
                throw Failure;
            }

            File.WriteAllBytes(destination, content());
            progress?.Report(0.5);
            progress?.Report(1);
            return Task.CompletedTask;
        }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static ReleaseAsset Asset(string name, byte[] bytes, string? url = null, string? sha = null, long? size = null) =>
        new(name, url ?? $"https://github.com/foxezol/FoxFix/releases/download/v0.9.16/{name}", size ?? bytes.Length, sha ?? Sha(bytes));

    private static ReleaseInfo Release(params ReleaseAsset[] assets) =>
        new(new Version(0, 9, 16), "v0.9.16", "https://github.com/foxezol/FoxFix/releases/tag/v0.9.16", "", assets);

    private SelfUpdater Updater(UninstallKind kind, Downloader downloader) => new(downloader, kind, Program, Work);

    private static byte[] Zip(params (string Path, string Text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(text);
            }
        }

        return memory.ToArray();
    }

    [Fact]
    public void An_installed_copy_gets_the_setup_and_a_zip_copy_gets_the_zip()
    {
        var setup = Asset("FoxFixSetup.exe", [1, 2, 3]);
        var zip = Asset("FoxFix-0.9.16-tasinabilir.zip", [4, 5, 6]);
        var release = Release(zip, setup);

        Updater(UninstallKind.Installed, new Downloader(() => [])).PickAsset(release).Should().Be(setup);
        Updater(UninstallKind.Portable, new Downloader(() => [])).PickAsset(release).Should().Be(zip);
        Updater(UninstallKind.Unknown, new Downloader(() => [])).PickAsset(release).Should().BeNull();
    }

    [Fact]
    public void A_copy_of_unknown_kind_is_never_updated_here()
    {
        var release = Release(Asset("FoxFixSetup.exe", [1]));

        Updater(UninstallKind.Unknown, new Downloader(() => [])).CanUpdate(release).Should().BeFalse();
        new SelfUpdater(new Downloader(() => []), UninstallKind.Installed, null, Work).CanUpdate(release).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://github.com/foxezol/FoxFix/releases/download/v1/FoxFixSetup.exe", true)]
    [InlineData("http://github.com/foxezol/FoxFix/releases/download/v1/FoxFixSetup.exe", false)]
    [InlineData("https://github.com.evil.test/foxezol/FoxFix/releases/download/v1/FoxFixSetup.exe", false)]
    [InlineData("https://evil.test/foxezol/FoxFix/releases/download/v1/FoxFixSetup.exe", false)]
    [InlineData("https://github.com/someone/FoxFix/releases/download/v1/FoxFixSetup.exe", false)]
    [InlineData("https://github.com/foxezol/FoxFix/archive/main.zip", false)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", false)]
    [InlineData("", false)]
    public void Only_files_from_this_projects_releases_are_trusted(string url, bool trusted)
    {
        SelfUpdater.IsVerifiable(new ReleaseAsset("FoxFixSetup.exe", url, 100, new string('a', 64))).Should().Be(trusted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void A_file_without_a_proper_sha256_cannot_be_checked(string? sha)
    {
        SelfUpdater.IsVerifiable(new ReleaseAsset("FoxFixSetup.exe", "https://github.com/foxezol/FoxFix/releases/download/v1/FoxFixSetup.exe", 100, sha)).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(SelfUpdater.MaxPackageBytes + 1)]
    public void An_absurd_size_is_not_trusted(long size)
    {
        SelfUpdater.IsVerifiable(new ReleaseAsset("FoxFixSetup.exe", "https://github.com/foxezol/FoxFix/releases/download/v1/FoxFixSetup.exe", size, new string('a', 64))).Should().BeFalse();
    }

    [Fact]
    public async Task A_verified_setup_gives_a_command_that_waits_runs_it_silently_and_reopens_FoxFix()
    {
        byte[] bytes = [1, 2, 3, 4];
        var downloader = new Downloader(() => bytes);
        var progress = new List<double>();

        var result = await Updater(UninstallKind.Installed, downloader)
            .PrepareAsync(Release(Asset("FoxFixSetup.exe", bytes)), new Inline(progress.Add), CancellationToken.None);

        result.Success.Should().BeTrue();
        downloader.Urls.Should().ContainSingle().Which.Should().EndWith("/FoxFixSetup.exe");
        var setupPath = Path.Combine(Work, "0.9.16", "FoxFixSetup.exe");
        File.Exists(setupPath).Should().BeTrue();
        result.Command.Should().StartWith($"ping -n {SelfUpdater.WaitSeconds + 1} 127.0.0.1 >nul & ")
            .And.Contain($"\"{setupPath}\"")
            .And.Contain("/VERYSILENT").And.Contain("/NORESTART").And.Contain("/YENIDEN=1");
        progress.Should().Equal(0.5, 1);
    }

    private sealed class Inline(Action<double> handler) : IProgress<double>
    {
        public void Report(double value) => handler(value);
    }

    [Fact]
    public async Task A_file_that_does_not_match_its_hash_is_deleted_and_never_run()
    {
        byte[] real = [1, 2, 3, 4];
        var downloader = new Downloader(() => [9, 9, 9, 9]);

        var result = await Updater(UninstallKind.Installed, downloader)
            .PrepareAsync(Release(Asset("FoxFixSetup.exe", real)), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Command.Should().BeNull();
        result.Message.Should().StartWith("[FX-6004]").And.Contain("SHA-256");
        Directory.Exists(Work).Should().BeFalse("the downloaded file is removed");
    }

    [Fact]
    public async Task The_hash_comparison_ignores_letter_case()
    {
        byte[] bytes = [7, 7, 7];

        var result = await Updater(UninstallKind.Installed, new Downloader(() => bytes))
            .PrepareAsync(Release(Asset("FoxFixSetup.exe", bytes, sha: Sha(bytes).ToUpperInvariant())), null, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Nothing_is_downloaded_from_an_untrusted_address()
    {
        byte[] bytes = [1];
        var downloader = new Downloader(() => bytes);

        var result = await Updater(UninstallKind.Installed, downloader)
            .PrepareAsync(Release(Asset("FoxFixSetup.exe", bytes, url: "https://evil.test/FoxFixSetup.exe")), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        downloader.Urls.Should().BeEmpty();
        result.Message.Should().Contain("SHA-256");
    }

    [Fact]
    public async Task Nothing_is_downloaded_without_a_hash_to_check_it_against()
    {
        var downloader = new Downloader(() => [1]);
        var asset = new ReleaseAsset("FoxFixSetup.exe", "https://github.com/foxezol/FoxFix/releases/download/v0.9.16/FoxFixSetup.exe", 1, null);

        var result = await Updater(UninstallKind.Installed, downloader).PrepareAsync(Release(asset), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        downloader.Urls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_copy_of_unknown_kind_is_pointed_to_the_release_page_not_updated()
    {
        var downloader = new Downloader(() => [1]);

        var result = await Updater(UninstallKind.Unknown, downloader).PrepareAsync(Release(Asset("FoxFixSetup.exe", [1])), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("sürüm sayfasından");
        result.Message.Should().NotStartWith("[FX-");
        downloader.Urls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_release_without_the_right_file_is_not_updated()
    {
        var result = await Updater(UninstallKind.Installed, new Downloader(() => [1]))
            .PrepareAsync(Release(Asset("baska.exe", [1])), null, CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_failed_download_is_a_result_with_a_code(Exception failure)
    {
        var downloader = new Downloader(() => [1]) { Failure = failure };

        var result = await Updater(UninstallKind.Installed, downloader).PrepareAsync(Release(Asset("FoxFixSetup.exe", [1])), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("[FX-6004]");
    }

    public static TheoryData<Exception> Failures() =>
    [
        new HttpRequestException("çevrimdışı"),
        new IOException("disk dolu"),
        new UnauthorizedAccessException("izin yok"),
        new InvalidDataException("büyük"),
    ];

    [Fact]
    public async Task Cancelling_the_download_is_not_turned_into_a_failure()
    {
        var downloader = new Downloader(() => [1]) { Failure = new OperationCanceledException() };

        var act = () => Updater(UninstallKind.Installed, downloader).PrepareAsync(Release(Asset("FoxFixSetup.exe", [1])), null, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Leftovers_of_an_earlier_attempt_are_cleared_first()
    {
        Directory.CreateDirectory(Path.Combine(Work, "0.9.10"));
        File.WriteAllText(Path.Combine(Work, "0.9.10", "eski.exe"), "x");
        byte[] bytes = [1, 2];

        await Updater(UninstallKind.Installed, new Downloader(() => bytes)).PrepareAsync(Release(Asset("FoxFixSetup.exe", bytes)), null, CancellationToken.None);

        Directory.Exists(Path.Combine(Work, "0.9.10")).Should().BeFalse();
    }

    [Theory]
    [InlineData("guncelleme", "Belgeler")]
    [InlineData("Windows", "FoxFix")]
    [InlineData("", "FoxFix")]
    public async Task Only_the_update_folder_inside_FoxFixs_own_data_folder_is_ever_emptied(string name, string parent)
    {
        var work = Path.Combine(_root, parent, name);
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "onemli.txt"), "dokunma");
        byte[] bytes = [1];
        var updater = new SelfUpdater(new Downloader(() => bytes), UninstallKind.Installed, Program, work);

        var act = () => updater.PrepareAsync(Release(Asset("FoxFixSetup.exe", bytes)), null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(Path.Combine(work, "onemli.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task A_path_with_a_character_cmd_would_misread_is_not_used()
    {
        byte[] bytes = [1];
        var work = Path.Combine(_root + "%PATH%", "FoxFix", "guncelleme");
        var updater = new SelfUpdater(new Downloader(() => bytes), UninstallKind.Installed, Program, work);

        var result = await updater.PrepareAsync(Release(Asset("FoxFixSetup.exe", bytes)), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("özel bir karakter");
        result.Command.Should().BeNull();
    }

    [Fact]
    public async Task A_zip_copy_gets_only_its_two_exe_files_replaced_and_is_started_again()
    {
        var zipBytes = Zip(("FoxFix/FoxFix.exe", "yeni"), ("FoxFix/FoxDPI.exe", "yeni"), ("FoxFix/FoxFix.portable", ""), ("FoxFix/README.md", "r"));
        Directory.CreateDirectory(Path.Combine(Program, "Veri"));

        var result = await Updater(UninstallKind.Portable, new Downloader(() => zipBytes))
            .PrepareAsync(Release(Asset("FoxFix-0.9.16-tasinabilir.zip", zipBytes)), null, CancellationToken.None);

        result.Success.Should().BeTrue();
        var newFolder = Path.Combine(Work, "0.9.16", "paket", "FoxFix");
        result.Command.Should().StartWith("ping -n ")
            .And.Contain($"copy /y \"{Path.Combine(newFolder, "FoxFix.exe")}\" \"{Path.Combine(Program, "FoxFix.exe")}\"")
            .And.Contain($"copy /y \"{Path.Combine(newFolder, "FoxDPI.exe")}\" \"{Path.Combine(Program, "FoxDPI.exe")}\"")
            .And.Contain($"start \"\" \"{Path.Combine(Program, "FoxFix.exe")}\"");
        result.Command.Should().NotContain("Veri").And.NotContain("del ").And.NotContain("rmdir");
        Directory.Exists(Path.Combine(Program, "Veri")).Should().BeTrue();
    }

    [Fact]
    public async Task A_zip_without_the_exe_files_is_refused()
    {
        var zipBytes = Zip(("FoxFix/README.md", "r"));

        var result = await Updater(UninstallKind.Portable, new Downloader(() => zipBytes))
            .PrepareAsync(Release(Asset("FoxFix-0.9.16-tasinabilir.zip", zipBytes)), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("[FX-6004]");
        result.Command.Should().BeNull();
    }

    [Fact]
    public async Task A_file_that_is_not_a_zip_is_refused_even_with_a_matching_hash()
    {
        byte[] bytes = [1, 2, 3, 4, 5];

        var result = await Updater(UninstallKind.Portable, new Downloader(() => bytes))
            .PrepareAsync(Release(Asset("FoxFix-0.9.16-tasinabilir.zip", bytes)), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("[FX-6004]");
    }
}
