namespace AdoCli.Tests;

using System.Net;
using System.Text.Json.Nodes;
using AdoCli.Api;

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
            $"https://tfs/Platform/_apis/git/repositories/connector/pullrequests?searchCriteria.status=active&$top=50&searchCriteria.creatorId={me}&api-version=5.0",
            stub.Requests[0].Url);
        Assert.Equal(142, prs.Single().PullRequestId);
    }

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

    static void AssertJson(string expected, string? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual!)), actual);
}
