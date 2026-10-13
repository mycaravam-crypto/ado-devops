namespace AdoCli.Tests;

using System.Net;
using System.Text.Json.Nodes;
using AdoCli.Api;
using AdoCli.Cli;

[Collection("Console output tests")]
public class WorkItemContextTests
{
    const string Current = """
        {"id":4711,"rev":5,"url":"https://tfs/_apis/wit/workItems/4711","fields":{
          "System.TeamProject":"Platform","System.WorkItemType":"User Story","System.State":"Active",
          "System.Title":"Login times out","System.ChangedDate":"2026-10-01T08:00:00.123Z",
          "System.Description":"<div>Users are logged out &amp; lose work.</div><div><br></div><div>Second paragraph.</div>",
          "Microsoft.VSTS.Common.AcceptanceCriteria":"<ul><li>Session lasts <b>8h</b></li><li>Warning&nbsp;before logout</li></ul>"},
         "relations":[
          {"rel":"System.LinkTypes.Related","url":"https://tfs/_apis/wit/workItems/30"},
          {"rel":"System.LinkTypes.Hierarchy-Forward","url":"https://tfs/_apis/wit/workItems/21"},
          {"rel":"ArtifactLink","url":"vstfs:///Git/Commit/abc"},
          {"rel":"System.LinkTypes.Hierarchy-Reverse","url":"https://tfs/_apis/wit/workItems/100"},
          {"rel":"System.LinkTypes.Hierarchy-Forward","url":"https://tfs/_apis/wit/workItems/20"}]}
        """;

    const string Revision3 = """
        {"id":4711,"rev":3,"url":"https://tfs/_apis/wit/workItems/4711/revisions/3","fields":{
          "System.TeamProject":"Platform","System.WorkItemType":"User Story","System.State":"New",
          "System.Title":"Login is slow","System.Description":"<p>Old text</p>"}}
        """;

    const string Linked = """
        {"count":3,"value":[
          {"id":100,"fields":{"System.Title":"Reliable sessions","System.WorkItemType":"Feature","System.State":"Active"}},
          {"id":20,"fields":{"System.Title":"Extend token","System.WorkItemType":"Task","System.State":"New"}},
          {"id":30,"fields":{"System.Title":"Logout bug","System.WorkItemType":"Bug","System.State":"Closed"}}]}
        """;

    [Fact]
    public async Task CurrentRevisionHasNormalizedTextAndExplicitLinks()
    {
        var (json, stub) = await RunAsync(n => n == 0 ? Current : Linked, "workitem", "context", "4711");

        Assert.Equal("https://tfs/_apis/wit/workitems/4711?$expand=relations&api-version=5.0", stub.Requests[0].Url);
        Assert.Equal("https://tfs/_apis/wit/workitems?ids=100,20,21,30&fields=System.Title,System.WorkItemType,System.State&errorPolicy=omit&api-version=5.0", stub.Requests[1].Url);
        Assert.All(stub.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));

        Assert.Equal(WorkItemCommands.ContextSchema, (string?)json["schema"]);
        Assert.Equal(5, (int)json["revision"]!);
        Assert.Equal(5, (int)json["currentRevision"]!);
        Assert.True((bool)json["isCurrent"]!);
        Assert.Equal("https://tfs/Platform/_workitems/edit/4711", (string?)json["webUrl"]);
        Assert.Equal("User Story", (string?)json["type"]);
        Assert.Equal("Login times out", (string?)json["title"]);
        Assert.Equal("2026-10-01T08:00:00.123Z", (string?)json["changedDate"]);

        Assert.Equal("System.Description", (string?)json["description"]!["sourceField"]);
        Assert.Equal("Users are logged out & lose work.\n\nSecond paragraph.", (string?)json["description"]!["text"]);
        Assert.StartsWith("<div>", (string?)json["description"]!["html"]);

        var criteria = json["acceptanceCriteria"]!;
        Assert.Equal("Microsoft.VSTS.Common.AcceptanceCriteria", (string?)criteria["sourceField"]);
        Assert.Equal("- Session lasts 8h\n- Warning before logout", (string?)criteria["text"]);
        Assert.Equal(["Session lasts 8h", "Warning before logout"], criteria["items"]!.AsArray().Select(i => (string?)i!["text"]));
        Assert.Equal([1, 2], criteria["items"]!.AsArray().Select(i => (int)i!["index"]!));

        Assert.Equal("current", (string?)json["relations"]!["status"]);
        var links = json["relations"]!["items"]!.AsArray();
        Assert.Equal(["parent:100", "child:20", "child:21", "related:30"], links.Select(l => $"{l!["kind"]}:{l["id"]}"));
        Assert.Equal("Reliable sessions", (string?)links[0]!["title"]);
        Assert.Equal("System.LinkTypes.Hierarchy-Reverse", (string?)links[0]!["rel"]);
        // 21 was not returned: deleted or not visible; reported, not guessed.
        Assert.Null(links[2]!["title"]);
        Assert.Contains("linked work item 21 is not visible", (string?)json["warnings"]![0]);
    }

    [Fact]
    public async Task HistoricalRevisionUsesThatRevisionsTextAndNoCurrentLinks()
    {
        var (json, stub) = await RunAsync(n => n == 0 ? Current : Revision3, "workitem", "context", "4711", "--revision", "3");

        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal("https://tfs/_apis/wit/workitems/4711/revisions/3?api-version=5.0", stub.Requests[1].Url);
        Assert.Equal(3, (int)json["revision"]!);
        Assert.Equal(5, (int)json["currentRevision"]!);
        Assert.False((bool)json["isCurrent"]!);
        Assert.Equal("Login is slow", (string?)json["title"]);
        Assert.Equal("New", (string?)json["state"]);
        Assert.Equal("Old text", (string?)json["description"]!["text"]);
        Assert.Equal("https://tfs/_apis/wit/workItems/4711/revisions/3", (string?)json["url"]);
        // Revision 3 had no acceptance criteria: nothing is taken from the current revision.
        Assert.Null(json["acceptanceCriteria"]!["sourceField"]);
        Assert.Null(json["acceptanceCriteria"]!["text"]);
        Assert.Empty(json["acceptanceCriteria"]!["items"]!.AsArray());
        Assert.Equal("unavailable", (string?)json["relations"]!["status"]);
        Assert.Null(json["relations"]!["items"]);
        Assert.Contains("relations unavailable for revision 3", (string?)json["warnings"]![0]);
    }

    [Fact]
    public async Task RequestingTheCurrentRevisionReadsItOnce()
    {
        var (json, stub) = await RunAsync(n => n == 0 ? Current : Linked, "workitem", "context", "4711", "--revision", "5");

        Assert.DoesNotContain(stub.Requests, r => r.Url.Contains("/revisions/"));
        Assert.True((bool)json["isCurrent"]!);
        Assert.Equal("current", (string?)json["relations"]!["status"]);
    }

    [Fact]
    public async Task RevisionAfterTheCurrentOneIsNotFound()
    {
        var stub = new StubHandler(HttpStatusCode.OK, Current);

        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemCommands.ContextAsync(Ctx(stub, "workitem", "context", "4711", "--revision", "9")));

        Assert.Equal(AdoException.NotFound, e.ExitCode);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    public async Task InvalidRevisionIsAUsageError(string revision)
    {
        var stub = new StubHandler(HttpStatusCode.OK, Current);

        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemCommands.ContextAsync(Ctx(stub, "workitem", "context", "4711", "--revision", revision)));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task BugWithoutDescriptionFallsBackToReproStepsAndHasNoCriteria()
    {
        const string bug = """
            {"id":7,"rev":1,"fields":{"System.WorkItemType":"Bug","System.Title":"Crash","System.Description":"<div> </div>",
             "Microsoft.VSTS.TCM.ReproSteps":"<ol><li>Open</li><li>Click</li></ol>"}}
            """;

        var (json, stub) = await RunAsync(_ => bug, "workitem", "context", "7");

        Assert.Single(stub.Requests);
        Assert.Equal("Microsoft.VSTS.TCM.ReproSteps", (string?)json["description"]!["sourceField"]);
        Assert.Equal("- Open\n- Click", (string?)json["description"]!["text"]);
        Assert.Null(json["acceptanceCriteria"]!["sourceField"]);
        Assert.Equal(["Microsoft.VSTS.Common.AcceptanceCriteria"], json["acceptanceCriteria"]!["checkedFields"]!.AsArray().Select(f => (string?)f));
        Assert.Empty(json["acceptanceCriteria"]!["items"]!.AsArray());
        Assert.Equal("https://tfs/_workitems/edit/7", (string?)json["webUrl"]);
        Assert.Equal("current", (string?)json["relations"]!["status"]);
        Assert.Empty(json["relations"]!["items"]!.AsArray());
    }

    [Fact]
    public async Task CustomFieldsReplaceTheDefaults()
    {
        const string custom = """
            {"id":8,"rev":2,"fields":{"System.Description":"<p>Default</p>","Custom.Beschreibung":"<p>Beschreibung</p>",
             "Custom.Akzeptanzkriterien":"<p>Erstens</p><p>Zweitens</p>"}}
            """;

        var (json, _) = await RunAsync(_ => custom, "workitem", "context", "8",
            "--description-field", "Custom.Beschreibung", "--criteria-field", "Custom.Akzeptanzkriterien");

        Assert.Equal("Custom.Beschreibung", (string?)json["description"]!["sourceField"]);
        Assert.Equal("Beschreibung", (string?)json["description"]!["text"]);
        Assert.Equal(["Erstens", "Zweitens"], json["acceptanceCriteria"]!["items"]!.AsArray().Select(i => (string?)i!["text"]));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AdoException.Auth)]
    [InlineData(HttpStatusCode.Forbidden, AdoException.Permission)]
    [InlineData(HttpStatusCode.NotFound, AdoException.NotFound)]
    public async Task HttpErrorsKeepTheirExitCodes(HttpStatusCode status, int exitCode)
    {
        var stub = new StubHandler(status, """{"message":"TF401232: Work item 4711 does not exist, or you do not have permissions to read it."}""");

        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemCommands.ContextAsync(Ctx(stub, "workitem", "context", "4711")));

        Assert.Equal(exitCode, e.ExitCode);
    }

    [Theory]
    [InlineData("<div>a &lt;script&gt;alert(1)&lt;/script&gt; b</div>", "a <script>alert(1)</script> b")]
    [InlineData("<p>x</p><script>alert(1)</script><style>p{}</style><!-- hidden --><p>y</p>", "x\ny")]
    [InlineData("<p>one</p>\n\n\n\n<p>two&nbsp;</p>", "one\n\ntwo")]
    [InlineData("plain text", "plain text")]
    public void PlainTextKeepsTextAndDropsMarkup(string html, string text) =>
        Assert.Equal(text, WorkItemCommands.PlainText(html));

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("<div>Given a user</div><div>When idle</div><div><br></div><div>Then logged out</div>", new[] { "Given a user", "When idle", "Then logged out" })]
    [InlineData("<ol><li>First<br>continued</li><li> </li><li>Second</li></ol>", new[] { "First continued", "Second" })]
    public void CriteriaKeepOrderAndNeverInventItems(string? html, string[] items) =>
        Assert.Equal(items, WorkItemCommands.Criteria(html).Select(c => c.Text));

    [Fact]
    public void BuildContextIgnoresLinksThatAreNotWorkItemsOrNotParentChildRelated()
    {
        var current = new WorkItem(9, []) { Rev = 1, Relations = [new("System.LinkTypes.Dependency-Forward", "https://tfs/_apis/wit/workItems/2"),
            new("System.LinkTypes.Related", "https://tfs/_apis/wit/workItems/notanumber"), new("System.LinkTypes.Related", "https://tfs/_apis/wit/workItems/3")] };

        var context = WorkItemCommands.BuildContext("https://tfs", current, null, new Dictionary<int, WorkItem>(),
            WorkItemCommands.DescriptionFields, WorkItemCommands.CriteriaFields);

        Assert.Equal([3], context.Relations.Items!.Select(r => r.Id));
        Assert.Null(context.ChangedDate);
        Assert.Null(context.Description.SourceField);
        Assert.Contains("linked work item 3 is not visible", Assert.Single(context.Warnings));
    }

    [Fact]
    public async Task OutputIsDeterministic()
    {
        var (first, _) = await RunAsync(n => n == 0 ? Current : Linked, "workitem", "context", "4711");
        var (second, _) = await RunAsync(n => n == 0 ? Current : Linked, "workitem", "context", "4711");

        Assert.Equal(first.ToJsonString(), second.ToJsonString());
    }

    static async Task<(JsonNode Json, StubHandler Stub)> RunAsync(Func<int, string> respond, params string[] argv)
    {
        var stub = new StubHandler(n => StubHandler.Json(respond(n)));
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            Assert.Equal(0, await WorkItemCommands.ContextAsync(Ctx(stub, argv)));
        }
        finally { Console.SetOut(original); }
        return (JsonNode.Parse(writer.ToString())!, stub);
    }

    static Context Ctx(StubHandler stub, params string[] argv) =>
        new(Args.Parse(argv), new Config(), new AdoClient("https://tfs", "pat", handler: stub));
}
