namespace AdoCli.Tests;

using AdoCli.Git;

public class GitClientTests
{
    [Theory]
    [InlineData("https://tfs.local/tfs/DefaultCollection/Platform/_git/connector", null,
        "https://tfs.local/tfs/DefaultCollection", "Platform", "connector")]
    [InlineData("https://me@tfs.local:8080/DefaultCollection/My%20Project/_git/my%20repo", null,
        "https://tfs.local:8080/DefaultCollection", "My Project", "my repo")]
    [InlineData("ssh://tfs.local:22/tfs/DefaultCollection/Platform/_git/connector", null,
        null, "Platform", "connector")]
    [InlineData("https://tfs.local/tfs/DefaultCollection/_git/connector", "https://tfs.local/tfs/DefaultCollection/",
        "https://tfs.local/tfs/DefaultCollection", "connector", "connector")]
    public void ParsesAzureDevOpsServerRemotes(string url, string? server, string? expectedServer, string project, string repo)
    {
        Assert.Equal(new Remote(expectedServer, project, repo), GitClient.ParseRemote(url, server));
    }

    [Fact]
    public void IgnoresNonAzureDevOpsRemotes()
    {
        Assert.Null(GitClient.ParseRemote("git@github.com:me/repo.git", null));
    }
}

public class CheckoutCommandTests
{
    [Fact]
    public void CreatesBranchFromFetchedSource()
    {
        Assert.Equal(
            [["fetch", "--quiet", "origin", "refs/heads/feature/x"], ["checkout", "--quiet", "-b", "pr/142", "FETCH_HEAD"]],
            AdoCli.Cli.PrCommands.CheckoutCommands("pr/142", "refs/heads/feature/x", branchExists: false));
    }

    [Fact]
    public void OnlyFastForwardsExistingBranch()
    {
        var commands = AdoCli.Cli.PrCommands.CheckoutCommands("pr/142", "refs/heads/feature/x", branchExists: true);
        Assert.Equal(["merge", "--quiet", "--ff-only", "FETCH_HEAD"], commands[^1]);
    }
}
