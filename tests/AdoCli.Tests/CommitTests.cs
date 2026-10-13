namespace AdoCli.Tests;

using System.Net;
using System.Text.Json.Nodes;
using AdoCli.Api;
using AdoCli.Cli;

[Collection("Console output tests")]
public class CommitTests
{
    const string Sha = "0123456789abcdef0123456789abcdef01234567";

    const string Repo = """
        {"id":"22222222-2222-2222-2222-222222222222","name":"backend","project":{"name":"Platform"}}
        """;

    const string Commit = $$"""
        {"commitId":"{{Sha}}","parents":["fedcba9876543210fedcba9876543210fedcba98"],
         "author":{"name":"Jane","email":"jane@company.local","date":"2026-10-01T08:00:00Z"},
         "committer":{"name":"Jane","email":"jane@company.local","date":"2026-10-01T08:05:00Z"},
         "comment":"Fix login timeout\n\nLonger explanation."}
        """;

    static string Linked(string workItems, string sha = Sha) =>
        $$"""{"count":1,"value":[{"commitId":"{{sha}}","comment":"Fix login timeout"{{workItems}}}]}""";

    [Theory]
    [InlineData("5.0")]
    [InlineData("6.0")]
    public async Task QueriesCommitAndItsWorkItemsWithGetOnly(string apiVersion)
    {
        var stub = new StubHandler(n => StubHandler.Json(n == 0 ? Commit : Linked(""","workItems":[]""")));
        var client = new AdoClient("https://tfs/tfs/DefaultCollection", "pat", handler: stub, apiVersion: apiVersion);

        await client.GetCommitAsync("Platform", "backend", Sha);
        await client.GetCommitWorkItemsAsync("Platform", "backend", Sha);

        Assert.Equal($"https://tfs/tfs/DefaultCollection/Platform/_apis/git/repositories/backend/commits/{Sha}?changeCount=0&api-version={apiVersion}", stub.Requests[0].Url);
        Assert.Equal("https://tfs/tfs/DefaultCollection/Platform/_apis/git/repositories/backend/commits" +
            $"?searchCriteria.itemVersion.version={Sha}&searchCriteria.itemVersion.versionType=commit" +
            $"&searchCriteria.$top=1&searchCriteria.includeWorkItems=true&api-version={apiVersion}", stub.Requests[1].Url);
        Assert.All(stub.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task ReportsLinkedWorkItemsSortedAndOnce()
    {
        var (exit, json, stub) = await RunAsync(Linked("""
            ,"workItems":[{"id":"4711","url":"https://tfs/_apis/wit/workItems/4711"},{"id":"12","url":"https://tfs/_apis/wit/workItems/12"},{"id":"4711","url":"https://tfs/_apis/wit/workItems/4711"}]
            """), "commit", "context", Sha, "--project", "Platform", "--repo", "backend");

        Assert.Equal(0, exit);
        Assert.Equal(CommitCommands.Schema, (string?)json["schema"]);
        Assert.Equal("https://tfs", (string?)json["server"]);
        Assert.Equal("Platform", (string?)json["project"]);
        Assert.Equal("backend", (string?)json["repository"]!["name"]);
        Assert.Equal(Sha, (string?)json["commit"]!["commitId"]);
        Assert.Equal("Fix login timeout\n\nLonger explanation.", (string?)json["commit"]!["comment"]);
        Assert.Equal("jane@company.local", (string?)json["commit"]!["author"]!["email"]);
        Assert.Equal("verified", (string?)json["workItems"]!["status"]);
        var items = json["workItems"]!["items"]!.AsArray();
        Assert.Equal([12, 4711], items.Select(i => (int)i!["id"]!));
        Assert.Equal("commit-workitem", (string?)items[0]!["source"]);
        Assert.Equal("https://tfs/_apis/wit/workItems/12", (string?)items[0]!["url"]);
        Assert.Equal("https://tfs/_workitems/edit/12", (string?)items[0]!["webUrl"]);
        Assert.Empty(json["warnings"]!.AsArray());
        Assert.All(stub.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task CommitWithoutLinksIsVerifiedEmpty()
    {
        var (exit, json, _) = await RunAsync(Linked(""","workItems":[]"""), "commit", "context", Sha, "--project", "Platform", "--repo", "backend");

        Assert.Equal(0, exit);
        Assert.Equal("verified", (string?)json["workItems"]!["status"]);
        Assert.Empty(json["workItems"]!["items"]!.AsArray());
    }

    [Theory]
    [InlineData("", "did not report work items")]
    [InlineData(null, "instead of")]
    public async Task UnreportedLinksAreUnavailableNotEmpty(string? workItems, string warning)
    {
        var body = workItems is null ? Linked(""","workItems":[]""", sha: "ffffffffffffffffffffffffffffffffffffffff") : Linked(workItems);

        var (exit, json, _) = await RunAsync(body, "commit", "context", Sha, "--project", "Platform", "--repo", "backend");

        Assert.Equal(0, exit);
        Assert.Equal("unavailable", (string?)json["workItems"]!["status"]);
        Assert.Null(json["workItems"]!["items"]);
        Assert.Contains(warning, (string?)json["warnings"]![0]);
    }

    [Fact]
    public async Task NoCommitFromQueryIsUnavailable()
    {
        var (_, json, _) = await RunAsync("""{"count":0,"value":[]}""", "commit", "context", Sha, "--project", "Platform", "--repo", "backend");

        Assert.Equal("unavailable", (string?)json["workItems"]!["status"]);
        Assert.Contains("no commit", (string?)json["warnings"]![0]);
    }

    [Fact]
    public async Task OutputIsDeterministic()
    {
        string[] argv = ["commit", "context", Sha.ToUpperInvariant(), "--project", "Platform", "--repo", "backend"];
        var links = Linked(""","workItems":[{"id":"2"},{"id":"1"}]""");

        var (_, first, stub) = await RunAsync(links, argv);
        var (_, second, _) = await RunAsync(links, argv);

        Assert.Equal(first.ToJsonString(), second.ToJsonString());
        Assert.Contains($"/commits/{Sha}?", stub.Requests[1].Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AdoException.Auth)]
    [InlineData(HttpStatusCode.Forbidden, AdoException.Permission)]
    [InlineData(HttpStatusCode.NotFound, AdoException.NotFound)]
    public async Task HttpErrorsKeepTheirExitCodes(HttpStatusCode status, int exitCode)
    {
        var stub = new StubHandler(n => n == 0 ? StubHandler.Json(Repo) : new HttpResponseMessage(status) { Content = new StringContent("""{"message":"TF401175: no access"}""") });

        var e = await Assert.ThrowsAsync<AdoException>(() => CommitCommands.ContextAsync(Ctx(stub, "commit", "context", Sha, "--project", "Platform", "--repo", "backend")));

        Assert.Equal(exitCode, e.ExitCode);
        Assert.DoesNotContain("pat", e.Message);
    }

    [Fact]
    public async Task MalformedResponseIsAClearError()
    {
        var stub = new StubHandler(n => StubHandler.Json(n == 0 ? Repo : "<html>not json</html>"));

        var e = await Assert.ThrowsAsync<AdoException>(() => CommitCommands.ContextAsync(Ctx(stub, "commit", "context", Sha, "--project", "Platform", "--repo", "backend")));

        Assert.Equal(AdoException.General, e.ExitCode);
        Assert.Contains("unexpected response from Azure DevOps Server (GET /_apis/", e.Message.Replace("/Platform", ""));
    }

    [Theory]
    [InlineData("abc1234")]
    [InlineData("HEAD")]
    public async Task ShortRevisionOutsideACloneOfTheRepoIsAUsageError(string commit)
    {
        var stub = new StubHandler(HttpStatusCode.OK, Repo);

        var e = await Assert.ThrowsAsync<AdoException>(() => CommitCommands.ContextAsync(Ctx(stub, "commit", "context", commit, "--project", "Platform", "--repo", "backend")));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Contains("full 40-character commit SHA", e.Message);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task MissingCommitIsAUsageError()
    {
        var stub = new StubHandler(HttpStatusCode.OK, Repo);

        var e = await Assert.ThrowsAsync<AdoException>(() => CommitCommands.ContextAsync(Ctx(stub, "commit", "context", "--project", "Platform", "--repo", "backend")));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public void HelpListsCommitContext()
    {
        Assert.Contains("ado commit context <commit>", Help.For("commit"));
        Assert.Contains("commit    context", Help.Overview);
    }

    static async Task<(int Exit, JsonNode Json, StubHandler Stub)> RunAsync(string linked, params string[] argv)
    {
        var stub = new StubHandler(n => StubHandler.Json(n switch { 0 => Repo, 1 => Commit, _ => linked }));
        var original = Console.Out;
        using var writer = new StringWriter();
        int exit;
        try
        {
            Console.SetOut(writer);
            exit = await CommitCommands.ContextAsync(Ctx(stub, argv));
        }
        finally { Console.SetOut(original); }
        Assert.DoesNotContain("Basic", writer.ToString());
        return (exit, JsonNode.Parse(writer.ToString())!, stub);
    }

    static Context Ctx(StubHandler stub, params string[] argv) =>
        new(Args.Parse(argv), new Config(), new AdoClient("https://tfs", "pat", handler: stub));
}
