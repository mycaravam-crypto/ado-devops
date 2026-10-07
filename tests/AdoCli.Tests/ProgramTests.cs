namespace AdoCli.Tests;

using AdoCli.Api;

public class ProgramTests
{
    [Fact]
    public async Task VersionAndHelpNeedNoConfiguration()
    {
        Assert.Equal(0, await Program.RunAsync(Args.Parse(["--version"])));
        Assert.Equal(0, await Program.RunAsync(Args.Parse([])));
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("workitem", "frobnicate")]
    public async Task UnknownCommandIsUsageError(params string[] argv)
    {
        var e = await Assert.ThrowsAsync<AdoException>(() => Program.RunAsync(Args.Parse(argv)));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Contains("ado --help", e.Message);
    }

    [Fact]
    public async Task DispatchesToTheSubcommand()
    {
        // workitem edit checks its id before it needs a server, so this reaches WorkItemCommands.EditAsync offline.
        var e = await Assert.ThrowsAsync<AdoException>(() => Program.RunAsync(Args.Parse(["workitem", "edit"])));

        Assert.StartsWith("usage: ado workitem edit <id>", e.Message);
    }
}
