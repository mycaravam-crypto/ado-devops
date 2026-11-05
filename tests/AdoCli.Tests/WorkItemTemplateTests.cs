namespace AdoCli.Tests;

using System.Net;
using System.Text.Json.Nodes;
using AdoCli.Api;
using AdoCli.Cli;

[Collection("Console output tests")]
public class WorkItemTemplateTests
{
    const string StoryId = "11111111-1111-1111-1111-111111111111";
    const string CaseId = "22222222-2222-2222-2222-222222222222";

    const string TemplateList = $$"""
        {"value":[
          {"id":"{{StoryId}}","name":"Standard story","description":"","workItemTypeName":"User Story"},
          {"id":"{{CaseId}}","name":"Smoke test","description":"","workItemTypeName":"Test Case"}]}
        """;

    const string StoryTemplate = $$"""
        {"id":"{{StoryId}}","name":"Standard story","description":"Default story","workItemTypeName":"User Story",
         "fields":{"System.Title":"As a <role>","Microsoft.VSTS.Scheduling.StoryPoints":"3","System.Tags":"refine"} }
        """;

    static Context Ctx(StubHandler stub, params string[] argv) =>
        new(Args.Parse(argv), new Config(), new AdoClient("https://tfs", "pat", handler: stub));

    static async Task<(int Code, string Out)> Capture(Func<Task<int>> run)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            return (await run(), writer.ToString());
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public void TeamUrlPutsTheTeamBetweenProjectAndApis()
    {
        var client = new AdoClient("https://tfs/DefaultCollection", "pat");

        Assert.Equal("https://tfs/DefaultCollection/My%20Project/My%20Team/_apis/wit/templates?workitemtypename=Bug&api-version=5.0-preview.1",
            client.TeamUrl("My Project", "My Team", "wit/templates", "workitemtypename=Bug"));
    }

    [Fact]
    public void TeamUrlKeepsAConfiguredPreviewVersion()
    {
        var client = new AdoClient("https://tfs", "pat", apiVersion: "7.1-preview.1");

        Assert.EndsWith("api-version=7.1-preview.1", client.TeamUrl("P", "T", "wit/templates"));
    }

    [Fact]
    public async Task ListUsesTheProjectsDefaultTeamAndFiltersByType()
    {
        var stub = new StubHandler(n => n == 0
            ? StubHandler.Json("""{"id":"33333333-3333-3333-3333-333333333333","name":"Platform","defaultTeam":{"id":"44444444-4444-4444-4444-444444444444","name":"Platform Team"}}""")
            : StubHandler.Json(TemplateList));

        var (code, output) = await Capture(() => WorkItemTemplateCommands.RunAsync(
            Ctx(stub, "workitem", "template", "list", "--project", "Platform", "--type", "User Story")));

        Assert.Equal(0, code);
        Assert.Equal("https://tfs/_apis/projects/Platform?api-version=5.0", stub.Requests[0].Url);
        // The stub records Uri.ToString(), which shows %20 as a space.
        Assert.Equal("https://tfs/Platform/Platform Team/_apis/wit/templates?workitemtypename=User Story&api-version=5.0-preview.1", stub.Requests[1].Url);
        Assert.Contains("Standard story", output);
        Assert.Contains("2 templates of team Platform Team", output);
    }

    [Fact]
    public async Task ShowResolvesANameAndReadsTheFields()
    {
        var stub = new StubHandler(n => n == 0 ? StubHandler.Json(TemplateList) : StubHandler.Json(StoryTemplate));

        var (code, output) = await Capture(() => WorkItemTemplateCommands.RunAsync(
            Ctx(stub, "workitem", "template", "show", "standard STORY", "--project", "Platform", "--team", "Web")));

        Assert.Equal(0, code);
        Assert.Equal($"https://tfs/Platform/Web/_apis/wit/templates/{StoryId}?api-version=5.0-preview.1", stub.Requests[1].Url);
        Assert.Contains("Microsoft.VSTS.Scheduling.StoryPoints", output);
        Assert.Contains("Default story", output);
    }

    [Fact]
    public void FieldDisplayShowsNullValuesAsEmpty()
    {
        var template = System.Text.Json.JsonSerializer.Deserialize<WorkItemTemplate>(
            """{"id":"11111111-1111-1111-1111-111111111111","name":"T","workItemTypeName":"Bug","fields":{"System.Tags":null,"System.Title":"<b>x</b>"}}""",
            Json.Options)!;

        var (rows, blocks) = WorkItemTemplateCommands.FieldDisplay(template.Fields!);

        Assert.Equal(new[] { "System.Tags", "" }, Assert.Single(rows));
        Assert.Equal(("System.Title", "x"), Assert.Single(blocks));
    }

    [Fact]
    public async Task UnknownNameIsNotFound()
    {
        var stub = new StubHandler(HttpStatusCode.OK, TemplateList);

        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemTemplateCommands.RunAsync(
            Ctx(stub, "workitem", "template", "show", "Nope", "--project", "Platform", "--team", "Web")));

        Assert.Equal(AdoException.NotFound, e.ExitCode);
    }

    [Fact]
    public async Task AmbiguousNameAsksForTheId()
    {
        var stub = new StubHandler(HttpStatusCode.OK, $$"""
            {"value":[
              {"id":"{{StoryId}}","name":"Default","workItemTypeName":"User Story"},
              {"id":"{{CaseId}}","name":"Default","workItemTypeName":"Test Case"}]}
            """);

        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemTemplateCommands.RunAsync(
            Ctx(stub, "workitem", "template", "show", "Default", "--project", "Platform", "--team", "Web")));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Contains(CaseId, e.Message);
    }

    [Fact]
    public async Task CreatePostsNameTypeAndFields()
    {
        var stub = new StubHandler(HttpStatusCode.OK, $$"""{"id":"{{CaseId}}","name":"Smoke test","workItemTypeName":"Test Case"}""");

        var (code, _) = await Capture(() => WorkItemTemplateCommands.RunAsync(Ctx(stub, "workitem", "template", "create",
            "--project", "Platform", "--team", "Web", "--name", "Smoke test", "--type", "Test Case",
            "--template-description", "Quick checks", "--description", "Check a < b", "--field", "Microsoft.VSTS.Common.Priority=1")));

        Assert.Equal(0, code);
        var request = stub.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://tfs/Platform/Web/_apis/wit/templates?api-version=5.0-preview.1", request.Url);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""
            {"name":"Smoke test","description":"Quick checks","workItemTypeName":"Test Case",
             "fields":{"System.Description":"Check a &lt; b","Microsoft.VSTS.Common.Priority":"1"}}
            """), JsonNode.Parse(request.Body!)), request.Body);
    }

    [Fact]
    public async Task CreateNeedsNameTypeAndAField()
    {
        var stub = new StubHandler(HttpStatusCode.OK);

        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemTemplateCommands.RunAsync(Ctx(stub,
            "workitem", "template", "create", "--project", "Platform", "--name", "Empty", "--type", "Bug")));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task EditKeepsOtherFieldsAndReplacesTheTemplate()
    {
        var stub = new StubHandler(n => n == 0 ? StubHandler.Json(StoryTemplate) : StubHandler.Json(StoryTemplate));

        var (code, _) = await Capture(() => WorkItemTemplateCommands.RunAsync(Ctx(stub, "workitem", "template", "edit", StoryId,
            "--project", "Platform", "--team", "Web", "--name", "Story v2", "--field", "Microsoft.VSTS.Scheduling.StoryPoints=5",
            "--remove-field", "system.tags")));

        Assert.Equal(0, code);
        Assert.Equal(2, stub.Requests.Count);
        var put = stub.Requests[1];
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal($"https://tfs/Platform/Web/_apis/wit/templates/{StoryId}?api-version=5.0-preview.1", put.Url);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""
            {"name":"Story v2","description":"Default story","workItemTypeName":"User Story",
             "fields":{"System.Title":"As a <role>","Microsoft.VSTS.Scheduling.StoryPoints":"5"}}
            """), JsonNode.Parse(put.Body!)), put.Body);
    }

    [Fact]
    public void EditRejectsRemovingAFieldTheTemplateDoesNotSet()
    {
        var template = new WorkItemTemplate(Guid.Empty, "T", null, "Bug", new() { ["System.Title"] = "x" });

        var e = Assert.Throws<AdoException>(() => WorkItemTemplateCommands.Edited(template, null, null, new Dictionary<string, string>(), ["System.Tags"]));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
    }

    [Fact]
    public async Task DeleteWithYesSendsDeleteAndAcceptsAnEmptyAnswer()
    {
        var stub = new StubHandler(n => n == 0 ? StubHandler.Json(TemplateList) : new HttpResponseMessage(HttpStatusCode.NoContent));

        var code = await WorkItemTemplateCommands.RunAsync(Ctx(stub, "workitem", "template", "delete", "Smoke test",
            "--project", "Platform", "--team", "Web", "--yes"));

        Assert.Equal(0, code);
        Assert.Equal(HttpMethod.Delete, stub.Requests[1].Method);
        Assert.Equal($"https://tfs/Platform/Web/_apis/wit/templates/{CaseId}?api-version=5.0-preview.1", stub.Requests[1].Url);
    }

    [Fact]
    public async Task UnknownSubcommandIsUsageError()
    {
        var e = await Assert.ThrowsAsync<AdoException>(() => Program.RunAsync(Args.Parse(["workitem", "template", "frobnicate"])));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Contains("ado workitem --help", e.Message);
    }

    [Fact]
    public async Task CreateWorkItemFromTemplateOverlaysOptions()
    {
        var stub = new StubHandler(n => n switch
        {
            0 => StubHandler.Json(TemplateList),
            1 => StubHandler.Json(StoryTemplate),
            _ => StubHandler.Json("""{"id":4712,"fields":{"System.Title":"As a tester","System.WorkItemType":"User Story"}}"""),
        });

        var (code, _) = await Capture(() => WorkItemCommands.CreateAsync(Ctx(stub, "workitem", "create", "--project", "Platform",
            "--team", "Web", "--template", "Standard story", "--title", "As a tester", "--field", "Microsoft.VSTS.Scheduling.StoryPoints=8")));

        Assert.Equal(0, code);
        var request = stub.Requests[2];
        Assert.Equal("https://tfs/Platform/_apis/wit/workitems/$User Story?api-version=5.0", request.Url);
        var patch = JsonNode.Parse(request.Body!)!.AsArray().ToDictionary(op => (string)op!["path"]!, op => (string)op!["value"]!);
        Assert.Equal("As a tester", patch["/fields/System.Title"]);
        Assert.Equal("8", patch["/fields/Microsoft.VSTS.Scheduling.StoryPoints"]);
        Assert.Equal("refine", patch["/fields/System.Tags"]);
    }

    [Fact]
    public async Task CreateWorkItemFromTemplateRejectsAnotherType()
    {
        var stub = new StubHandler(n => n == 0 ? StubHandler.Json(TemplateList) : StubHandler.Json(StoryTemplate));

        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemCommands.CreateAsync(Ctx(stub, "workitem", "create",
            "--project", "Platform", "--team", "Web", "--template", "Standard story", "--type", "Bug")));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Equal(2, stub.Requests.Count);
    }
}
