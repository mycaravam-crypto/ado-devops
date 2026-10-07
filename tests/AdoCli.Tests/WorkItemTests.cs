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
    public async Task FetchesAllQueriedWorkItemsInBatchesOf200()
    {
        var refs = string.Join(',', Enumerable.Range(1, 450).Select(i => $"{{\"id\":{i}}}"));
        var stub = new StubHandler(n => n == 0
            ? StubHandler.Json($$"""{"workItems":[{{refs}}]}""")
            : StubHandler.Json("""{"value":[{"id":1,"fields":{}}]}"""));

        var items = await new AdoClient("https://tfs", "pat", handler: stub).GetMyWorkItemsAsync("Platform");

        Assert.Equal("https://tfs/Platform/_apis/wit/wiql?api-version=5.0", stub.Requests[0].Url);
        Assert.Equal(4, stub.Requests.Count);
        Assert.Contains("ids=1,2,", stub.Requests[1].Url);
        Assert.Contains("ids=201,", stub.Requests[2].Url);
        Assert.Contains("ids=401,", stub.Requests[3].Url);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task LimitCapsTheQuery()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"workItems":[]}""");

        await new AdoClient("https://tfs", "pat", handler: stub).GetMyWorkItemsAsync("Platform", limit: 10);

        Assert.Equal("https://tfs/Platform/_apis/wit/wiql?$top=10&api-version=5.0", stub.Requests[0].Url);
    }

    [Fact]
    public void ListsEveryFieldNotInTheHeader()
    {
        var w = System.Text.Json.JsonSerializer.Deserialize<WorkItem>("""
            {"id":4711,"fields":{
              "System.Title":"Improve","System.State":"Active","System.AssignedTo":{"displayName":"hannovb"},
              "System.AreaPath":"Platform\\Import","Custom.Points":3,"System.Tags":"",
              "System.CreatedBy":{"displayName":"anna"},
              "System.Description":"<div>Do it</div><div>now</div>","Custom.Notes":"line 1\nline 2"}}
            """, Json.Options)!;

        var (rows, blocks) = WorkItemCommands.OtherFields(w);

        Assert.Equal(
            [["Custom.Points", "3"], ["System.AreaPath", "Platform\\Import"], ["System.CreatedBy", "anna"]],
            rows);
        Assert.Equal([("Custom.Notes", "line 1\nline 2"), ("System.Description", "Do it\nnow")], blocks);
    }

    [Fact]
    public void ConvertsHtmlDescriptionToText()
    {
        Assert.Equal("Do <it>\nnow", WorkItemCommands.PlainText("<div>Do &lt;it&gt;</div><div><b>now</b></div>"));
    }
}
