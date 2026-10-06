namespace AdoCli.Tests;

using System.Net;
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
}
