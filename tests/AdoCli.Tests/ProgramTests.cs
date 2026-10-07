namespace AdoCli.Tests;

using AdoCli.Api;
using AdoCli.Cli;

public class ProgramTests
{
    [Fact]
    public async Task VersionAndHelpNeedNoConfiguration()
    {
        Assert.Equal(0, await Program.RunAsync(Args.Parse(["--version"])));
        Assert.Equal(0, await Program.RunAsync(Args.Parse([])));
    }

    [Theory]
    [InlineData("ado --help", "frobnicate")]
    [InlineData("ado workitem --help", "workitem", "frobnicate")]
    public async Task UnknownCommandIsUsageError(string hint, params string[] argv)
    {
        var e = await Assert.ThrowsAsync<AdoException>(() => Program.RunAsync(Args.Parse(argv)));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Contains(hint, e.Message);
    }

    [Theory]
    [InlineData("ado - a small CLI", "--help")]
    [InlineData("ado - a small CLI", "help")]
    [InlineData("ado - a small CLI", "frobnicate", "--help")]
    [InlineData("ado workitem - work items", "workitem")]
    [InlineData("ado workitem - work items", "workitem", "--help")]
    [InlineData("ado workitem - work items", "workitem", "edit", "-h")]
    [InlineData("ado workitem - work items", "help", "workitem")]
    [InlineData("ado pr - pull requests", "pr", "merge", "142", "--help")]
    public void HelpShowsTheCommandPageOrTheOverview(string heading, params string[] argv) =>
        Assert.StartsWith(heading, Program.HelpPage(Args.Parse(argv)));

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("workitem", "list")]
    [InlineData("pr", "show", "142")]
    public void CommandsRunWithoutHelp(params string[] argv) =>
        Assert.Null(Program.HelpPage(Args.Parse(argv)));

    [Theory]
    [InlineData("auth")]
    [InlineData("config")]
    [InlineData("repo")]
    [InlineData("pr")]
    [InlineData("workitem")]
    [InlineData("build")]
    public void EveryCommandHasAHelpPageListedInTheOverview(string command)
    {
        Assert.StartsWith($"ado {command} - ", Help.For(command));
        Assert.Contains($"\n  {command} ", Help.Overview);
    }

    [Fact]
    public async Task DispatchesToTheSubcommand()
    {
        // workitem edit checks its id before it needs a server, so this reaches WorkItemCommands.EditAsync offline.
        var e = await Assert.ThrowsAsync<AdoException>(() => Program.RunAsync(Args.Parse(["workitem", "edit"])));

        Assert.StartsWith("usage: ado workitem edit <id>", e.Message);
    }
}
