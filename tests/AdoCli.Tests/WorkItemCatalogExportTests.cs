namespace AdoCli.Tests;

using System.Net;
using System.Text.Json.Nodes;
using AdoCli.Api;
using AdoCli.Cli;

[Collection("Console output tests")]
public class WorkItemCatalogExportTests
{
    static Context Context(StubHandler handler, params string[] options) =>
        new(Args.Parse(["workitem", "export", "--project", "Platform", .. options]), new Config(),
            new AdoClient("https://tfs", "pat", handler: handler));

    [Fact]
    public async Task ExportIncludesAllStatesAndExplicitParentOutsideArea()
    {
        const string parent = """
            {"id":9,"rev":3,"fields":{
              "System.TeamProject":"Platform","System.WorkItemType":"User Story",
              "System.Title":"Password reset","System.State":"Closed",
              "System.AreaPath":"Platform\\Auth","System.IterationPath":"Platform\\Sprint 4",
              "System.Description":"<div>Reset a password</div>",
              "Microsoft.VSTS.Common.AcceptanceCriteria":"<ul><li>Send token</li><li>Expire token</li></ul>"},
             "relations":[{"rel":"System.LinkTypes.Hierarchy-Reverse",
               "url":"https://tfs/_apis/wit/workItems/100"}]}
            """;
        var stub = new StubHandler(n => StubHandler.Json(n == 0
            ? """{"workItems":[{"id":9}]}""" : parent));
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, await WorkItemCatalogExport.RunAsync(Context(stub, "--area", @"Platform\Auth")));
        }
        finally { Console.SetOut(original); }
        var result = JsonNode.Parse(output.ToString())!;
        Assert.Equal(WorkItemCatalogExport.Schema, (string?)result["schema"]);
        Assert.True((bool)result["complete"]!);
        var item = Assert.Single(result["items"]!.AsArray());
        Assert.Equal(9, (int)item!["id"]!);
        Assert.Equal("Closed", (string?)item["state"]);
        Assert.Equal("Reset a password", (string?)item["description"]);
        Assert.Equal(["Send token", "Expire token"], item["criteria"]!.AsArray().Select(x => (string?)x));
        Assert.Equal("parent", (string?)item["relations"]![0]!["kind"]);
        Assert.Equal(100, (int)item["relations"]![0]!["id"]!);
        var query = (string?)JsonNode.Parse(stub.Requests[0].Body!)!["query"];
        Assert.Contains("[System.AreaPath] UNDER", query);
        Assert.DoesNotContain("@Me", query);
        Assert.DoesNotContain("[System.State]", query);
    }

    [Fact]
    public async Task UnavailableWorkItemMarksExportIncomplete()
    {
        var stub = new StubHandler(n => n == 0
            ? StubHandler.Json("""{"workItems":[{"id":8}]}""")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(1, await WorkItemCatalogExport.RunAsync(Context(stub, "--area", "Platform")));
        }
        finally { Console.SetOut(original); }
        var result = JsonNode.Parse(output.ToString())!;
        Assert.False((bool)result["complete"]!);
        Assert.Empty(result["items"]!.AsArray());
        Assert.Contains("work item 8 unavailable", (string?)result["warnings"]![0]);
    }

    [Fact]
    public async Task MissingAreaIsRejectedBeforeNetworkAccess()
    {
        var stub = new StubHandler(HttpStatusCode.OK);
        var error = await Assert.ThrowsAsync<AdoException>(() =>
            WorkItemCatalogExport.RunAsync(Context(stub)));
        Assert.Equal(AdoException.InvalidUsage, error.ExitCode);
        Assert.Empty(stub.Requests);
    }
}
