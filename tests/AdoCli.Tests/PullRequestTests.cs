namespace AdoCli.Tests;

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdoCli.Api;
using AdoCli.Cli;

public class PullRequestTests
{
    const string Pr = """
        {"pullRequestId":142,"title":"Fix import validation","status":"active",
         "createdBy":{"id":"11111111-1111-1111-1111-111111111111","displayName":"hannovb"},
         "repository":{"id":"22222222-2222-2222-2222-222222222222","name":"connector","project":{"name":"Platform"}},
         "sourceRefName":"refs/heads/feature/x","targetRefName":"refs/heads/main",
         "lastMergeSourceCommit":{"commitId":"abc"},"lastMergeTargetCommit":{"commitId":"def"},
         "reviewers":[{"id":"33333333-3333-3333-3333-333333333333","displayName":"Alice","vote":10}]}
        """;

    [Fact]
    public async Task ListsRepositoryPullRequestsByCreator()
    {
        var stub = new StubHandler(HttpStatusCode.OK, $$"""{"count":1,"value":[{{Pr}}]}""");
        var me = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var prs = await new AdoClient("https://tfs", "pat", handler: stub).GetPullRequestsAsync("Platform", "connector", "active", me);

        Assert.Equal(
            $"https://tfs/Platform/_apis/git/repositories/connector/pullrequests?searchCriteria.status=active&searchCriteria.creatorId={me}&$top=100&$skip=0&api-version=5.0",
            stub.Requests[0].Url);
        Assert.Equal(142, prs.Single().PullRequestId);
    }

    [Fact]
    public void ListRowShowsTargetBranch()
    {
        var pr = JsonSerializer.Deserialize<PullRequest>(Pr, Json.Options)!;

        Assert.Equal(["142", "Fix import validation", "hannovb", "main", "Active"], PrCommands.ListRow(pr));
    }

    [Fact]
    public async Task ListsAllPullRequestsPageByPage()
    {
        var stub = new StubHandler(n => StubHandler.Json(Page(n < 2 ? 100 : 7)));

        var prs = await new AdoClient("https://tfs", "pat", handler: stub).GetPullRequestsAsync("Platform", null, "active", null);

        Assert.Equal(207, prs.Count);
        Assert.Equal(["$top=100&$skip=0", "$top=100&$skip=100", "$top=100&$skip=200"],
            stub.Requests.Select(r => r.Url.Split("active&")[1].Split("&api-version")[0]));
    }

    [Fact]
    public async Task StopsAtLimit()
    {
        var stub = new StubHandler(n => StubHandler.Json(Page(n == 0 ? 100 : 30)));

        var prs = await new AdoClient("https://tfs", "pat", handler: stub).GetPullRequestsAsync("Platform", null, "all", null, limit: 130);

        Assert.Equal(130, prs.Count);
        Assert.Contains("$top=30&$skip=100", stub.Requests[1].Url);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task FollowsCommitContinuationToken()
    {
        var stub = new StubHandler(n => StubHandler.Json($$"""{"value":[{"commitId":"c{{n}}"}]}""", n == 0 ? "next" : null));
        var client = new AdoClient("https://tfs", "pat", handler: stub);

        var commits = await client.GetPullRequestCommitsAsync(JsonSerializer.Deserialize<PullRequest>(Pr, Json.Options)!);

        Assert.Equal(["c0", "c1"], commits.Select(c => c.CommitId));
        Assert.EndsWith("/pullrequests/142/commits?continuationToken=next&api-version=5.0", stub.Requests[1].Url);
    }

    static string Page(int count) =>
        $$"""{"count":{{count}},"value":[{{string.Join(',', Enumerable.Repeat(Pr, count))}}]}""";

    [Fact]
    public async Task GetsPullRequestByIdAcrossProjects()
    {
        var stub = new StubHandler(HttpStatusCode.OK, Pr);

        var pr = await new AdoClient("https://tfs", "pat", handler: stub).GetPullRequestAsync(142);

        Assert.Equal("https://tfs/_apis/git/pullrequests/142?api-version=5.0", stub.Requests[0].Url);
        Assert.Equal("Platform", pr.Repository.Project.Name);
        Assert.Equal(10, pr.Reviewers!.Single().Vote);
    }

    [Fact]
    public async Task CreatesPullRequest()
    {
        var stub = new StubHandler(HttpStatusCode.Created, Pr);

        await new AdoClient("https://tfs", "pat", handler: stub)
            .CreatePullRequestAsync("Platform", "connector", "refs/heads/feature/x", "refs/heads/main", "Fix", "");

        Assert.Equal(HttpMethod.Post, stub.Requests[0].Method);
        Assert.Equal("https://tfs/Platform/_apis/git/repositories/connector/pullrequests?api-version=5.0", stub.Requests[0].Url);
        AssertJson("""{"sourceRefName":"refs/heads/feature/x","targetRefName":"refs/heads/main","title":"Fix","description":""}""", stub.Requests[0].Body);
    }

    [Fact]
    public async Task ApprovesAsReviewer()
    {
        var stub = new StubHandler(HttpStatusCode.OK, Pr);
        var client = new AdoClient("https://tfs", "pat", handler: stub);
        var me = Guid.NewGuid();

        await client.VoteAsync(await client.GetPullRequestAsync(142), me, 10);

        Assert.Equal(HttpMethod.Put, stub.Requests[1].Method);
        Assert.Equal($"https://tfs/Platform/_apis/git/repositories/22222222-2222-2222-2222-222222222222/pullrequests/142/reviewers/{me}?api-version=5.0",
            stub.Requests[1].Url);
        AssertJson("""{"vote":10}""", stub.Requests[1].Body);
    }

    [Fact]
    public async Task CompletesAtLastSeenSourceCommit()
    {
        var stub = new StubHandler(HttpStatusCode.OK, Pr);
        var client = new AdoClient("https://tfs", "pat", handler: stub);

        await client.CompletePullRequestAsync(await client.GetPullRequestAsync(142), squash: true);

        Assert.Equal(HttpMethod.Patch, stub.Requests[1].Method);
        AssertJson("""{"status":"completed","lastMergeSourceCommit":{"commitId":"abc"},"completionOptions":{"squashMerge":true}}""", stub.Requests[1].Body);
    }

    [Fact]
    public async Task ListsChangedFilesBetweenMergeCommits()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
            {"changes":[{"item":{"path":"/src/b.cs"},"changeType":"edit"},{"item":{"path":"/src","isFolder":true},"changeType":"edit"},
                        {"item":{"path":"/a.cs"},"changeType":"rename","originalPath":"/old.cs"}]}
            """);
        var pr = JsonSerializer.Deserialize<PullRequest>(Pr, Json.Options)!;

        var changes = await new AdoClient("https://tfs", "pat", handler: stub).GetPullRequestChangesAsync(pr);

        Assert.Equal("https://tfs/Platform/_apis/git/repositories/22222222-2222-2222-2222-222222222222/diffs/commits" +
            "?baseVersion=def&baseVersionType=commit&targetVersion=abc&targetVersionType=commit&$top=2000&$skip=0&api-version=5.0", stub.Requests[0].Url);
        Assert.Equal([new Change("/a.cs", "rename", "/old.cs"), new Change("/src/b.cs", "edit", null)], changes);
    }

    [Fact]
    public async Task SkipsWorkItemsThatAreNotVisible()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"value":[{"id":1,"fields":{}},null]}""");

        var items = await new AdoClient("https://tfs", "pat", handler: stub).GetWorkItemsAsync([1, 2]);

        Assert.EndsWith("ids=1,2&fields=System.Title,System.WorkItemType,System.State&errorPolicy=omit&api-version=5.0", stub.Requests[0].Url);
        Assert.Equal(1, items.Single().Id);
    }

    static void AssertJson(string expected, string? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual!)), actual);

    [Fact]
    public void SummaryDescribesTheQuery()
    {
        Assert.Equal(["active", "all repositories"], PrCommands.Describe("active", mine: false, repo: null));
        Assert.Equal(["any status", "created by you", "repository api"], PrCommands.Describe("all", mine: true, repo: "api"));
    }
}
