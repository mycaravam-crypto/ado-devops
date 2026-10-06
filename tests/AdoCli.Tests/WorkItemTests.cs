namespace AdoCli.Tests;

using System.Net;
using AdoCli.Api;
using AdoCli.Cli;

public class WorkItemTests
{
    [Fact]
    public async Task ReadsFieldsIncludingIdentities()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
            {"id":4711,"fields":{"System.Title":"Improve","System.AssignedTo":{"displayName":"hannovb"},"Custom.Points":3}}
            """);

        var w = await new AdoClient("https://tfs", "pat", handler: stub).GetWorkItemAsync(4711);

        Assert.Equal("https://tfs/_apis/wit/workitems/4711?api-version=5.0", stub.Requests[0].Url);
        Assert.Equal("Improve", w.Field("System.Title"));
        Assert.Equal("hannovb", w.Field("System.AssignedTo"));
        Assert.Equal("3", w.Field("Custom.Points"));
        Assert.Equal("", w.Field("System.Description"));
    }

    [Fact]
    public async Task EmptyQuerySkipsSecondRequest()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"workItems":[]}""");

        var items = await new AdoClient("https://tfs", "pat", handler: stub).GetMyWorkItemsAsync("Platform");

        Assert.Empty(items);
        Assert.Contains("@Me", stub.Requests.Single().Body);
    }

    [Fact]
    public void ConvertsHtmlDescriptionToText()
    {
        Assert.Equal("Do <it>\nnow", WorkItemCommands.PlainText("<div>Do &lt;it&gt;</div><div><b>now</b></div>"));
    }
}
