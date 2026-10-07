namespace AdoCli.Tests;

using System.Net;
using System.Text.Json.Nodes;
using AdoCli.Api;

public class BuildTests
{
    const string Build = """
        {"id":100,"buildNumber":"20261006.1","status":"notStarted","definition":{"id":7,"name":"ci"},
         "sourceBranch":"refs/heads/main","_links":{"web":{"href":"https://tfs/b/100"}}}
        """;

    [Fact]
    public async Task QueuesDefinitionWithoutBranchByDefault()
    {
        var stub = new StubHandler(HttpStatusCode.OK, Build);

        var b = await new AdoClient("https://tfs", "pat", handler: stub).QueueBuildAsync("Platform", 7, null);

        Assert.Equal("https://tfs/Platform/_apis/build/builds?api-version=5.0", stub.Requests[0].Url);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"definition":{"id":7}}"""), JsonNode.Parse(stub.Requests[0].Body!)));
        Assert.Equal("https://tfs/b/100", b.Links!.Web!.Href);
    }

    [Fact]
    public async Task ListsAllBuildsByContinuationToken()
    {
        var stub = new StubHandler(n => StubHandler.Json($$"""{"value":[{{Build}}]}""", n < 2 ? $"t{n}" : null));

        var builds = await new AdoClient("https://tfs", "pat", handler: stub).GetBuildsAsync("Platform");

        Assert.Equal(3, builds.Count);
        Assert.Equal("https://tfs/Platform/_apis/build/builds?queryOrder=queueTimeDescending&$top=1000&api-version=5.0", stub.Requests[0].Url);
        Assert.Equal("https://tfs/Platform/_apis/build/builds?queryOrder=queueTimeDescending&$top=1000&continuationToken=t1&api-version=5.0", stub.Requests[2].Url);
    }

    [Fact]
    public async Task ListsBuildsUpToLimit()
    {
        var stub = new StubHandler(n => StubHandler.Json($$"""{"value":[{{Build}}]}""", "more"));

        var builds = await new AdoClient("https://tfs", "pat", handler: stub).GetBuildsAsync("Platform", limit: 2);

        Assert.Equal(2, builds.Count);
        Assert.Contains("$top=2&", stub.Requests[0].Url);
        Assert.Contains("$top=1&continuationToken=more&", stub.Requests[1].Url);
    }
}
