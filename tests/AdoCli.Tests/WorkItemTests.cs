namespace AdoCli.Tests;

using System.Net;
using System.Text.Json.Nodes;
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

    [Fact]
    public async Task CreatesWithJsonPatchOfFields()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"id":4712,"fields":{"System.Title":"Crash","System.WorkItemType":"User Story"}}""");
        var fields = new Dictionary<string, string> { ["System.Title"] = "Crash", ["Microsoft.VSTS.Common.Priority"] = "1" };

        var w = await new AdoClient("https://tfs", "pat", handler: stub).CreateWorkItemAsync("Platform", "User Story", fields);

        var request = stub.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        // Uri.ToString() shows the escaped space (%20) unescaped.
        Assert.Equal("https://tfs/Platform/_apis/wit/workitems/$User Story?api-version=5.0", request.Url);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""
            [{"op":"add","path":"/fields/System.Title","value":"Crash"},{"op":"add","path":"/fields/Microsoft.VSTS.Common.Priority","value":"1"}]
            """), JsonNode.Parse(request.Body!)), request.Body);
        Assert.Equal("application/json-patch+json", request.ContentType);
        Assert.Equal(4712, w.Id);
    }

    [Fact]
    public async Task UpdatesByIdWithoutProject()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"id":4711,"fields":{"System.State":"Active"}}""");

        await new AdoClient("https://tfs", "pat", handler: stub).UpdateWorkItemAsync(4711, new Dictionary<string, string> { ["System.State"] = "Active" });

        var request = stub.Requests.Single();
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("https://tfs/_apis/wit/workitems/4711?api-version=5.0", request.Url);
        Assert.Equal("application/json-patch+json", request.ContentType);
    }

    [Fact]
    public void MapsOptionsToFields()
    {
        var a = Args.Parse(["workitem", "edit", "4711", "--state", "Active", "--assigned-to", "jane@company.local",
            "--description", "a < b\nnext", "--comment", "done", "--field", "Microsoft.VSTS.Common.Priority=1", "--field=Custom.Url=https://x?a=b"]);

        var fields = WorkItemCommands.Fields(a);

        Assert.Equal("Active", fields["System.State"]);
        Assert.Equal("jane@company.local", fields["System.AssignedTo"]);
        Assert.Equal("a &lt; b<br>next", fields["System.Description"]);
        Assert.Equal("done", fields["System.History"]);
        Assert.Equal("1", fields["Microsoft.VSTS.Common.Priority"]);
        Assert.Equal("https://x?a=b", fields["Custom.Url"]);
        Assert.False(fields.ContainsKey("System.Title"));
    }

    [Theory]
    [InlineData("Priority")]
    [InlineData("=1")]
    public void RejectsFieldWithoutName(string field)
    {
        var e = Assert.Throws<AdoException>(() => WorkItemCommands.Fields(Args.Parse(["workitem", "edit", "1", "--field", field])));
        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
    }
}
