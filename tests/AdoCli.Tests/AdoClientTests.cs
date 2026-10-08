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
    public void UsesConfiguredApiVersion()
    {
        Assert.Equal("https://tfs/_apis/x?api-version=6.0", new AdoClient("https://tfs", "pat", apiVersion: "6.0").Url(null, "x"));
    }

    [Fact]
    public void PerRequestApiVersionDoesNotChangeGlobalVersion()
    {
        var client = new AdoClient("https://tfs", "pat", apiVersion: "6.0");
        Assert.Equal("https://tfs/My%20Project/_apis/test/plans?$top=10&api-version=5.0",
            client.Url("My Project", "test/plans", "$top=10", apiVersion: "5.0"));
        Assert.Equal("https://tfs/_apis/git/repositories?api-version=6.0", client.Url(null, "git/repositories"));
    }

    [Fact]
    public async Task ExplainsUnsupportedApiVersion()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest,
            """{"message":"The requested REST API version of 7.0 is out of range for this server.","typeKey":"VssVersionOutOfRangeException"}""");
        var client = new AdoClient("https://tfs", "pat", handler: stub, apiVersion: "7.0");

        var e = await Assert.ThrowsAsync<AdoException>(() => client.SendAsync<object>(HttpMethod.Get, client.Url(null, "x")));

        Assert.Equal(AdoException.General, e.ExitCode);
        Assert.Contains("REST API version 7.0", e.Message);
        Assert.Contains("ADO_API_VERSION", e.Message);
    }

    [Fact]
    public void RejectsCredentialsInServerUrl()
    {
        var e = Assert.Throws<AdoException>(() => new AdoClient("https://me:secret-pat@tfs/DefaultCollection", "pat"));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
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
