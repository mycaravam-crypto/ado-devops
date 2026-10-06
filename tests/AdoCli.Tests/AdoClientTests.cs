namespace AdoCli.Tests;

using System.Net;
using AdoCli.Api;

public class AdoClientTests
{
    [Theory]
    [InlineData("https://tfs/tfs/DefaultCollection/", null, "https://tfs/tfs/DefaultCollection/_apis/projects?api-version=5.0")]
    [InlineData("https://tfs/DefaultCollection", "My Project", "https://tfs/DefaultCollection/My%20Project/_apis/projects?api-version=5.0")]
    public void BuildsUrlsUnderAnyServerLayout(string server, string? project, string expected)
    {
        Assert.Equal(expected, new AdoClient(server, "pat").Url(project, "projects"));
    }

    [Fact]
    public void AppendsQueryBeforeApiVersion()
    {
        Assert.Equal("https://tfs/_apis/git/pullrequests?$top=5&api-version=5.0",
            new AdoClient("https://tfs", "pat").Url(null, "git/pullrequests", "$top=5"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AdoException.Auth)]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation, AdoException.Auth)]
    [InlineData(HttpStatusCode.Forbidden, AdoException.Permission)]
    [InlineData(HttpStatusCode.NotFound, AdoException.NotFound)]
    [InlineData(HttpStatusCode.Conflict, AdoException.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, AdoException.General)]
    [InlineData(HttpStatusCode.InternalServerError, AdoException.General)]
    public async Task MapsHttpErrorsToExitCodes(HttpStatusCode status, int exitCode)
    {
        var client = new AdoClient("https://tfs", "secret-pat", handler: new StubHandler(status, """{"message":"boom"}"""));

        var e = await Assert.ThrowsAsync<AdoException>(() => client.SendAsync<object>(HttpMethod.Get, client.Url(null, "x")));

        Assert.Equal(exitCode, e.ExitCode);
        Assert.DoesNotContain("secret-pat", e.Message);
    }

    [Fact]
    public async Task SendsPatAsBasicAuth()
    {
        var stub = new StubHandler(HttpStatusCode.OK);
        var client = new AdoClient("https://tfs", "pat", handler: stub);

        await client.SendAsync<object>(HttpMethod.Get, client.Url(null, "x"));

        Assert.Equal("Basic OnBhdA==", stub.Requests[0].Auth); // base64(":pat")
    }
}
