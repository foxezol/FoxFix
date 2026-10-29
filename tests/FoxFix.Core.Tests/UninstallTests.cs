using FluentAssertions;
using FoxFix.Core.Commands;
using FoxFix.Core.Dpi;
using FoxFix.Core.GoodbyeDpi;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class UninstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "foxfix-un-" + Guid.NewGuid().ToString("N"));
    private readonly string _program;

    public UninstallTests()
    {
        _program = Path.Combine(_root, "Program Files", "FoxFix");
        Directory.CreateDirectory(_program);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Data(string name)
    {
        var path = Path.Combine(_root, "Data", name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string Exe() => Path.Combine(_program, "FoxFix.exe");

    // ---------- the plan ----------

    [Fact]
    public void An_installed_copy_waits_removes_its_data_and_hands_the_program_to_its_own_uninstaller()
    {
        File.WriteAllText(Path.Combine(_program, UninstallPlan.UninstallerName), "");
        var local = Data("FoxFix");
        var plan = UninstallPlan.Create(Exe(), local, local);

        plan.Kind.Should().Be(UninstallKind.Installed);
        var command = plan.BuildFinishCommand()!;
        command.Should().StartWith($"ping -n {UninstallPlan.WaitSeconds + 1} 127.0.0.1 >nul");
        command.Should().Contain($"rmdir /s /q \"{local}\"").And.EndWith($"\"{Path.Combine(_program, "unins000.exe")}\" /SILENT /NORESTART");
        command.Should().NotContain("del ", "the uninstaller deletes the program files, not this command");
    }

    [Fact]
    public void A_portable_copy_deletes_only_the_files_and_folders_the_zip_put_there()
    {
        File.WriteAllText(Path.Combine(_program, "FoxFix.portable"), "");
        File.WriteAllText(Path.Combine(_program, "benim-notlarim.txt"), "dokunma");
        var plan = UninstallPlan.Create(Exe(), Path.Combine(_program, "Veri"), Path.Combine(_program, "Veri"));

        plan.Kind.Should().Be(UninstallKind.Portable);
        var command = plan.BuildFinishCommand()!;
        foreach (var file in UninstallPlan.PortableFiles)
        {
            command.Should().Contain($"del /f /q \"{Path.Combine(_program, file)}\"");
        }

        foreach (var folder in UninstallPlan.PortableFolders)
        {
            command.Should().Contain($"rmdir /s /q \"{Path.Combine(_program, folder)}\"");
        }

        command.Should().EndWith($"rmdir \"{_program}\"", "the folder itself goes only if it is empty by then");
        command.Should().NotContain("*").And.NotContain("benim-notlarim");
        command.Should().NotContain($"rmdir /s /q \"{_program}\"", "never the whole folder");
    }

    [Fact]
    public void A_copy_in_an_unknown_place_only_loses_its_data_and_the_program_stays()
    {
        var local = Data("FoxFix");
        var plan = UninstallPlan.Create(Exe(), local, local);

        plan.Kind.Should().Be(UninstallKind.Unknown);
        var command = plan.BuildFinishCommand()!;
        command.Should().Contain($"rmdir /s /q \"{local}\"").And.NotContain("del ").And.NotContain(_program);
        UninstallPlan.Create(Exe(), Path.Combine(_root, "yok", "FoxFix"), Path.Combine(_root, "yok", "FoxFix")).Kind.Should().Be(UninstallKind.Unknown);
    }

    [Fact]
    public void Only_folders_with_FoxFixs_own_names_are_ever_data_folders()
    {
        var plan = UninstallPlan.Create(Exe(), Data("Downloads"), Data("Belgelerim"));

        plan.DataFolders.Should().BeEmpty();
        plan.BuildFinishCommand().Should().BeNull("nothing to remove and no program to finish");
    }

    [Fact]
    public void A_data_folder_that_contains_the_program_is_never_removed()
    {
        // FoxFix run from inside its own data folder: deleting that folder would delete the running program.
        var plan = UninstallPlan.Create(Path.Combine(_root, "Data", "FoxFix", "FoxFix.exe"), Data("FoxFix"), Data("FoxFix"));

        plan.DataFolders.Should().BeEmpty();
    }

    [Theory]
    [InlineData("100%")]
    [InlineData("a^b")]
    [InlineData("hey!")]
    public void A_path_with_a_character_the_command_shell_would_act_on_runs_nothing(string name)
    {
        var program = Path.Combine(_root, name);
        Directory.CreateDirectory(program);
        File.WriteAllText(Path.Combine(program, UninstallPlan.UninstallerName), "");

        UninstallPlan.Create(Path.Combine(program, "FoxFix.exe"), Data("FoxFix"), Data("FoxFix")).BuildFinishCommand().Should().BeNull();
    }

    // ---------- the fix ----------

    private sealed class Runner : ICommandRunner
    {
        public bool TaskExists { get; set; } = true;

        public List<string> Calls { get; } = [];

        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls.Add(string.Join(' ', arguments));
            if (arguments[0] == "/Delete")
            {
                TaskExists = false;
            }

            return Task.FromResult(new CommandResult(arguments[0] == "/Query" && !TaskExists ? 1 : 0, "", ""));
        }
    }

    private sealed class NoServices : IServiceManager
    {
        public Task<ServiceConfig?> ReadConfigAsync(string name, CancellationToken ct) => Task.FromResult<ServiceConfig?>(null);

        public Task<StopOutcome> StopAsync(string name, CancellationToken ct) => Task.FromResult(StopOutcome.Stopped);

        public Task<DeleteOutcome> DeleteAsync(string name, CancellationToken ct) => Task.FromResult(DeleteOutcome.Deleted);

        public Task<bool> CreateAsync(ServiceConfig config, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> StartAsync(string name, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class NoLauncher : IGoodbyeDpiLauncher
    {
        public IGoodbyeDpiProcess Start(string exePath, string arguments) => throw new NotSupportedException();

        public Task<int> StopAllInFolderAsync(string folder, CancellationToken ct) => Task.FromResult(0);

        public bool IsRunningFromFolder(string folder) => false;
    }

    private (UninstallFix Fix, Runner Runner, List<string> Started, int[] Exits, int[] Stops, string Folder) Create(bool finisherWorks = true)
    {
        File.WriteAllText(Path.Combine(_program, UninstallPlan.UninstallerName), "");
        var local = Data("FoxFix");
        var folder = Path.Combine(_root, "gdpi");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "goodbyedpi.exe"), "x");
        var runner = new Runner();
        var started = new List<string>();
        var exits = new int[1];
        var stops = new int[1];
        var fix = new UninstallFix(
            new FoxDpiAutoStart(runner),
            new GoodbyeDpiTeardown(new NoServices(), new NoLauncher(), folder),
            () =>
            {
                stops[0]++;
                return Task.CompletedTask;
            },
            UninstallPlan.Create(Exe(), local, local),
            command =>
            {
                started.Add(command);
                return finisherWorks;
            },
            () => exits[0]++);
        return (fix, runner, started, exits, stops, folder);
    }

    [Fact]
    public async Task Previewing_changes_nothing_and_says_it_cannot_be_undone()
    {
        var (fix, runner, started, exits, _, folder) = Create();

        var preview = await fix.PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeTrue();
        preview.Changes.Should().Contain(Strings.Get(Strings.UninstallIrreversible));
        started.Should().BeEmpty();
        exits[0].Should().Be(0);
        runner.Calls.Should().BeEmpty();
        Directory.Exists(folder).Should().BeTrue();
    }

    [Fact]
    public async Task Applying_stops_FoxDPI_removes_the_task_and_the_files_then_hands_over_and_closes()
    {
        var (fix, runner, started, exits, stops, folder) = Create();

        var result = await fix.ApplyAsync(new UndoJournal(Path.Combine(_root, "undo.json")), CancellationToken.None);

        result.Success.Should().BeTrue();
        stops[0].Should().Be(1);
        runner.Calls.Should().Contain(c => c.StartsWith("/Delete") && c.Contains(FoxDpiAutoStart.TaskName));
        Directory.Exists(folder).Should().BeFalse("FoxDPI's own files are gone");
        started.Should().ContainSingle().Which.Should().Contain("unins000.exe");
        exits[0].Should().Be(1, "FoxFix closes so the program files can be deleted");
        result.Summary.Should().Be(Strings.Get(Strings.UninstallResultInstalled));
        File.Exists(Path.Combine(_root, "undo.json")).Should().BeFalse("nothing is journaled: it cannot be undone");
    }

    [Fact]
    public async Task When_the_finishing_command_cannot_start_FoxFix_stays_open_and_says_what_is_left()
    {
        var (fix, _, started, exits, _, _) = Create(finisherWorks: false);

        var result = await fix.ApplyAsync(new UndoJournal(Path.Combine(_root, "undo.json")), CancellationToken.None);

        started.Should().HaveCount(1);
        exits[0].Should().Be(0, "closing without the command would leave the program behind with no way to finish");
        result.Summary.Should().Be(Strings.Get(Strings.UninstallResultManual));
    }
}