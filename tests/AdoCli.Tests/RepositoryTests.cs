namespace AdoCli.Tests;

using System.Net;
using System.Text.Json;
using AdoCli.Api;
using AdoCli.Cli;

public class RepositoryTests
{
    const string Body = """
        {"count":1,"value":[{"id":"22222222-2222-2222-2222-222222222222","name":"connector",
          "defaultBranch":"refs/heads/main","remoteUrl":"https://tfs/_git/connector","webUrl":"https://tfs/w",
          "project":{"id":"p","name":"Platform"},"size":123}]}
        """;

    [Fact]
    public async Task ListsRepositoriesOfProject()
    {
        var stub = new StubHandler(HttpStatusCode.OK, Body);
        var repos = await new AdoClient("https://tfs/tfs/DefaultCollection", "pat", handler: stub).GetRepositoriesAsync("Platform");

        Assert.Equal("https://tfs/tfs/DefaultCollection/Platform/_apis/git/repositories?api-version=5.0", stub.Requests[0].Url);
        Assert.Equal("connector", repos.Single().Name);
        Assert.Equal("main", Output.Branch(repos[0].DefaultBranch));
    }

    [Fact]
    public async Task SerializesAsCamelCaseJson()
    {
        var repos = await new AdoClient("https://tfs", "pat", handler: new StubHandler(HttpStatusCode.OK, Body)).GetRepositoriesAsync(null);

        var json = JsonDocument.Parse(JsonSerializer.Serialize(repos, Json.Options));
        Assert.Equal("Platform", json.RootElement[0].GetProperty("project").GetProperty("name").GetString());
    }
}
