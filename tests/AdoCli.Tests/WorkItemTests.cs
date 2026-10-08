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

        var items = await new AdoClient("https://tfs", "pat", handler: stub).QueryWorkItemsAsync("Platform");

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

        var items = await new AdoClient("https://tfs", "pat", handler: stub).QueryWorkItemsAsync("Platform");

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

        await new AdoClient("https://tfs", "pat", handler: stub).QueryWorkItemsAsync("Platform", limit: 10);

        Assert.Equal("https://tfs/Platform/_apis/wit/wiql?$top=10&api-version=5.0", stub.Requests[0].Url);
    }

    [Fact]
    public async Task DefaultQueryListsOpenItemsWithListFields()
    {
        var stub = new StubHandler(n => n == 0
            ? StubHandler.Json("""{"workItems":[{"id":7}]}""")
            : StubHandler.Json("""{"value":[{"id":7,"fields":{}}]}"""));

        await new AdoClient("https://tfs", "pat", handler: stub).QueryWorkItemsAsync("Platform");

        var query = (string)JsonNode.Parse(stub.Requests[0].Body!)!["query"]!;
        Assert.Equal("SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = @Me AND [System.State] NOT IN ('Closed', 'Done', 'Removed')" +
            " AND [System.TeamProject] = @project ORDER BY [System.ChangedDate] DESC", query);
        Assert.EndsWith("ids=7&fields=System.Title,System.WorkItemType,System.State,System.AssignedTo,Microsoft.VSTS.Common.Priority," +
            "System.IterationPath,System.ChangedDate&errorPolicy=omit&api-version=5.0", stub.Requests[1].Url);
    }

    [Fact]
    public async Task TypeAndStateFiltersGoIntoTheQuery()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"workItems":[]}""");

        await new AdoClient("https://tfs", "pat", handler: stub).QueryWorkItemsAsync(null, new() { Types = ["Bug", "User Story"], States = ["Closed", "Won't Fix"] });

        var query = (string)JsonNode.Parse(stub.Requests.Single().Body!)!["query"]!;
        Assert.Equal("SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = @Me AND [System.State] IN ('Closed', 'Won''t Fix')" +
            " AND [System.WorkItemType] IN ('Bug', 'User Story') ORDER BY [System.ChangedDate] DESC", query);
    }

    [Fact]
    public async Task ListCommandPassesRepeatedAndCommaSeparatedFilters()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"workItems":[]}""");

        var code = await WorkItemCommands.ListAsync(Ctx(stub, "workitem", "list", "--project", "Platform",
            "--type", "Bug, User Story", "--type", "bug", "--state", "Active"));

        Assert.Equal(0, code);
        var query = (string)JsonNode.Parse(stub.Requests.Single().Body!)!["query"]!;
        Assert.Contains("[System.State] IN ('Active')", query);
        Assert.Contains("[System.WorkItemType] IN ('Bug', 'User Story')", query);
    }

    [Theory]
    [InlineData(0, "0 work items")]
    [InlineData(1, "1 work item")]
    [InlineData(2, "2 work items")]
    public async Task ListHasFinalResultCount(int count, string footer)
    {
        var refs = string.Join(",", Enumerable.Range(1, count).Select(id => $"{{\"id\":{id}}}"));
        var items = string.Join(",", Enumerable.Range(1, count).Select(id =>
            $"{{\"id\":{id},\"fields\":{{\"System.Title\":\"Example {id}\"}}}}"));
        var stub = new StubHandler(n => n == 0
            ? StubHandler.Json($"{{\"workItems\":[{refs}]}}")
            : StubHandler.Json($"{{\"value\":[{items}]}}"));
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            Assert.Equal(0, await WorkItemCommands.ListAsync(Ctx(stub, "workitem", "list")));
        }
        finally { Console.SetOut(original); }
        Assert.EndsWith(footer + Environment.NewLine, writer.ToString());
        Assert.Contains("ID", writer.ToString());
    }

    [Fact]
    public async Task ListJsonAndIdsDoNotHaveHumanReadableFooter()
    {
        var jsonStub = new StubHandler(n => n == 0
            ? StubHandler.Json("""{"workItems":[{"id":7}]}""")
            : StubHandler.Json("""{"value":[{"id":7,"fields":{"System.Title":"Example"}}]}"""));
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            Assert.Equal(0, await WorkItemCommands.ListAsync(Ctx(jsonStub, "workitem", "list", "--json")));
        }
        finally { Console.SetOut(original); }
        var data = JsonNode.Parse(writer.ToString())!.AsArray();
        Assert.Single(data);
        Assert.Equal(7, (int)data[0]!["id"]!);

        var idsStub = new StubHandler(HttpStatusCode.OK, """{"workItems":[{"id":7}]}""");
        using var idsWriter = new StringWriter();
        try
        {
            Console.SetOut(idsWriter);
            Assert.Equal(0, await WorkItemCommands.ListAsync(Ctx(idsStub, "workitem", "list", "--ids")));
        }
        finally { Console.SetOut(original); }
        Assert.Equal("7" + Environment.NewLine, idsWriter.ToString());
    }

    [Fact]
    public void SummaryAggregatesReturnedItemsAndMarksLimitWithoutClaimingExactTotal()
    {
        var items = new List<WorkItem>
        {
            new(1, new() { ["System.WorkItemType"] = Str("Bug"), ["System.State"] = Str("Active"),
                ["Microsoft.VSTS.Common.Priority"] = Str("1") }),
            new(2, new() { ["System.WorkItemType"] = Str("Bug"), ["System.State"] = Str("Resolved"),
                ["Microsoft.VSTS.Common.Priority"] = Str("2") }),
            new(3, new() { ["System.WorkItemType"] = Str("Task"), ["System.State"] = Str("Active"),
                ["Microsoft.VSTS.Common.Priority"] = Str("2") }),
        };
        using var writer = new StringWriter();
        WorkItemCommands.PrintSummary(items, 3, writer);
        var output = writer.ToString();
        Assert.Contains("limit reached; more may exist", output);
        Assert.Contains("By type:", output);
        Assert.Contains("Bug                2", output);
        Assert.Contains("By state:", output);
        Assert.Contains("Active             2", output);
        Assert.Contains("By priority:", output);
        Assert.Contains("P2                 2", output);
    }

    [Fact]
    public void SummaryHandlesZeroItemsAndMissingFields()
    {
        using var empty = new StringWriter();
        WorkItemCommands.PrintSummary([], null, empty);
        Assert.Contains("Showing 0 items.", empty.ToString());
        Assert.Contains("(none)", empty.ToString());
        using var missing = new StringWriter();
        WorkItemCommands.PrintSummary([new WorkItem(1, [])], null, missing);
        Assert.Contains("Unknown", missing.ToString());
        Assert.DoesNotContain("limit reached", missing.ToString());
    }

    [Fact]
    public async Task SummaryFlagAddsBreakdownOnlyToTextOutput()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"workItems":[]}""");
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            Assert.Equal(0, await WorkItemCommands.ListAsync(Ctx(stub, "workitem", "list", "--summary")));
        }
        finally { Console.SetOut(original); }
        Assert.Contains("By priority:", writer.ToString());
    }

    [Fact]
    public void ListRowIncludesAssigneeDisplayName()
    {
        var item = System.Text.Json.JsonSerializer.Deserialize<WorkItem>(
            """{"id":10,"fields":{"System.Title":"Task","System.AssignedTo":{"displayName":"Jane Doe"}}}""",
            Json.Options)!;
        Assert.Equal("Jane Doe", WorkItemCommands.ListRow(item)[3]);
    }

    [Theory]
    [InlineData(false, "@Me (default)", "Open (default")]
    [InlineData(true, "Everyone", "Open (default")]
    public void AppliedFiltersExplainDefaultsAndAll(bool all, string assignee, string state)
    {
        var a = Args.Parse(all ? ["workitem", "list", "--all"] : ["workitem", "list"]);
        using var writer = new StringWriter();
        WorkItemCommands.PrintAppliedFilters(a, "Platform", 200, 200, writer);
        Assert.Contains(assignee, writer.ToString());
        Assert.Contains(state, writer.ToString());
        Assert.Contains("reached; more items may exist", writer.ToString());
        Assert.Contains("Project:     Platform", writer.ToString());
    }

    [Fact]
    public void AppliedFiltersExplainExplicitAssigneeAndAllStates()
    {
        var a = Args.Parse(["workitem", "list", "--all", "--assigned-to", "jane@example.com",
            "--state", "any", "--type", "Bug"]);
        using var writer = new StringWriter();
        WorkItemCommands.PrintAppliedFilters(a, null, null, 0, writer);
        Assert.Contains("Assigned to: jane@example.com", writer.ToString());
        Assert.Contains("State:       any", writer.ToString());
        Assert.Contains("Type:        Bug", writer.ToString());
        Assert.Contains("Limit:       None", writer.ToString());
    }

    [Fact]
    public void ListRowShowsDetails()
    {
        var w = System.Text.Json.JsonSerializer.Deserialize<WorkItem>("""
            {"id":4711,"fields":{"System.Title":"Improve","System.WorkItemType":"Bug","System.State":"Active",
              "Microsoft.VSTS.Common.Priority":2,"System.IterationPath":"Platform\\Sprint 42","System.ChangedDate":"2026-10-05T12:00:00Z"}}
            """, Json.Options)!;

        var expectedDate = DateTimeOffset.Parse("2026-10-05T12:00:00Z").ToLocalTime().ToString("yyyy-MM-dd");
        Assert.Equal(["4711", "Bug", "Active", "", "2", "Platform\\Sprint 42", expectedDate, "Improve"], WorkItemCommands.ListRow(w));
    }

    [Fact]
    public void ListRowLeavesMissingFieldsEmpty()
    {
        var w = new WorkItem(1, []);

        Assert.Equal(["1", "", "", "", "", "", "", ""], WorkItemCommands.ListRow(w));
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

    static Context Ctx(StubHandler stub, params string[] argv) =>
        new(Args.Parse(argv), new Config(), new AdoClient("https://tfs", "pat", handler: stub));

    [Fact]
    public async Task CreateCommandSendsTitleDescriptionAndFields()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"id":4712,"fields":{"System.Title":"Crash","System.WorkItemType":"Bug"}}""");

        var code = await WorkItemCommands.CreateAsync(Ctx(stub, "workitem", "create", "--project", "Platform", "--type", "Bug",
            "--title", "Crash", "--description", "a < b\nnext", "--field", "Microsoft.VSTS.Common.Priority=1"));

        Assert.Equal(0, code);
        var request = stub.Requests.Single();
        Assert.Equal("https://tfs/Platform/_apis/wit/workitems/$Bug?api-version=5.0", request.Url);
        var patch = JsonNode.Parse(request.Body!)!.AsArray().ToDictionary(op => (string)op!["path"]!, op => (string)op!["value"]!);
        Assert.Equal("Crash", patch["/fields/System.Title"]);
        Assert.Equal("a &lt; b<br>next", patch["/fields/System.Description"]);
        Assert.Equal("1", patch["/fields/Microsoft.VSTS.Common.Priority"]);
    }

    [Fact]
    public async Task CreateCommandRequiresTypeWithTitle()
    {
        var stub = new StubHandler(HttpStatusCode.OK);

        var e = await Assert.ThrowsAsync<AdoException>(() =>
            WorkItemCommands.CreateAsync(Ctx(stub, "workitem", "create", "--project", "Platform", "--title", "Crash")));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task EditCommandPatchesOnlyGivenFields()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"id":4711,"fields":{"System.Title":"Improve","System.State":"Active"}}""");

        var code = await WorkItemCommands.EditAsync(Ctx(stub, "workitem", "edit", "4711", "--state", "Active"));

        Assert.Equal(0, code);
        var request = stub.Requests.Single();
        Assert.Equal("https://tfs/_apis/wit/workitems/4711?api-version=5.0", request.Url);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""[{"op":"add","path":"/fields/System.State","value":"Active"}]"""),
            JsonNode.Parse(request.Body!)), request.Body);
    }

    [Fact]
    public async Task EditCommandWithoutChangesIsUsageError()
    {
        var stub = new StubHandler(HttpStatusCode.OK);

        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemCommands.EditAsync(Ctx(stub, "workitem", "edit", "4711")));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public void FilterOptionsGoIntoTheQuery()
    {
        var a = Args.Parse(["workitem", "list", "--all", "--state", "any", "--area", "Platform\\Import", "--iteration", "Platform\\Sprint 42",
            "--tag", "release,ui", "--title-contains", "abc", "--contains", "don't", "--wiql", "[Microsoft.VSTS.Common.Priority] = 1"]);

        var wiql = AdoClient.Wiql(WorkItemCommands.Filter(a), inProject: true);

        Assert.Equal("SELECT [System.Id] FROM WorkItems WHERE [System.AreaPath] UNDER 'Platform\\Import'" +
            " AND [System.IterationPath] UNDER 'Platform\\Sprint 42' AND [System.Tags] CONTAINS 'release' AND [System.Tags] CONTAINS 'ui'" +
            " AND [System.Title] CONTAINS 'abc' AND ([System.Title] CONTAINS 'don''t' OR [System.Description] CONTAINS 'don''t')" +
            " AND ([Microsoft.VSTS.Common.Priority] = 1) AND [System.TeamProject] = @project ORDER BY [System.ChangedDate] DESC", wiql);
    }

    [Theory]
    [InlineData("jane@company.local", "[System.AssignedTo] = 'jane@company.local'")]
    [InlineData("@me", "[System.AssignedTo] = @Me")]
    public void AssignedToReplacesTheDefaultAssignee(string who, string condition)
    {
        var wiql = AdoClient.Wiql(new WorkItemFilter { AssignedTo = who }, inProject: false);

        Assert.Equal($"SELECT [System.Id] FROM WorkItems WHERE {condition} AND [System.State] NOT IN ('Closed', 'Done', 'Removed')" +
            " ORDER BY [System.ChangedDate] DESC", wiql);
    }

    [Fact]
    public void AllWithAnyStateHasNoConditions()
    {
        Assert.Equal("SELECT [System.Id] FROM WorkItems ORDER BY [System.ChangedDate] DESC",
            AdoClient.Wiql(new WorkItemFilter { Everyone = true, States = ["Any"] }, inProject: false));
    }

    [Fact]
    public async Task ListIdsSkipsFetchingFields()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"workItems":[{"id":7},{"id":9}]}""");

        var code = await WorkItemCommands.ListAsync(Ctx(stub, "workitem", "list", "--all", "--ids"));

        Assert.Equal(0, code);
        Assert.EndsWith("/wit/wiql?api-version=5.0", stub.Requests.Single().Url);
    }

    [Fact]
    public void EditIdsReadsArgumentsAndStdin()
    {
        var a = Args.Parse(["workitem", "edit", "3", "-", "#5"]);

        Assert.Equal([3, 1, 2, 5], WorkItemCommands.EditIds(a, new StringReader("1\n2 3\n")));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    public void EditIdsRejectsNonIds(string token)
    {
        var e = Assert.Throws<AdoException>(() => WorkItemCommands.EditIds(Args.Parse(["workitem", "edit", "-"]), new StringReader(token)));
        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
    }

    [Fact]
    public void ReplaceTitleIgnoresCaseAndSkipsUnmatchedTitles()
    {
        WorkItem[] items =
        [
            new(1, new() { ["System.Title"] = Str("Fix ABC import") }),
            new(2, new() { ["System.Title"] = Str("Unrelated") }),
        ];

        var changes = WorkItemCommands.Changes(items, new Dictionary<string, string> { ["System.History"] = "renamed" }, "abc", "xyz");

        var (item, fields) = Assert.Single(changes);
        Assert.Equal(1, item.Id);
        Assert.Equal("Fix xyz import", fields["System.Title"]);
        Assert.Equal("renamed", fields["System.History"]);
    }

    static System.Text.Json.JsonElement Str(string s) => System.Text.Json.JsonSerializer.SerializeToElement(s);

    [Fact]
    public async Task BulkEditWithYesUpdatesEachAndReportsFailures()
    {
        var stub = new StubHandler(n => n switch
        {
            0 => StubHandler.Json("""{"value":[{"id":1,"fields":{"System.Title":"a"}},{"id":2,"fields":{"System.Title":"b"}}]}"""),
            1 => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"message":"invalid state"}""") },
            _ => StubHandler.Json("""{"id":2,"fields":{"System.Title":"b"}}"""),
        });

        var code = await WorkItemCommands.EditAsync(Ctx(stub, "workitem", "edit", "1", "2", "--state", "Closed", "--yes"));

        Assert.Equal(AdoException.General, code);
        Assert.Equal(3, stub.Requests.Count);
        Assert.Contains("ids=1,2&", stub.Requests[0].Url);
        Assert.Equal("https://tfs/_apis/wit/workitems/1?api-version=5.0", stub.Requests[1].Url);
        Assert.Equal("https://tfs/_apis/wit/workitems/2?api-version=5.0", stub.Requests[2].Url);
        Assert.Contains("\"Closed\"", stub.Requests[2].Body);
    }

    [Fact]
    public async Task BulkEditDryRunChangesNothing()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"value":[{"id":1,"fields":{"System.Title":"abc"}},{"id":2,"fields":{"System.Title":"abc"}}]}""");

        var code = await WorkItemCommands.EditAsync(Ctx(stub, "workitem", "edit", "1", "2", "--replace-title", "abc", "--with", "xyz", "--dry-run"));

        Assert.Equal(0, code);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData("--replace-title", "abc")]
    [InlineData("--replace-title", "abc", "--with", "x", "--title", "t")]
    public async Task ReplaceTitleNeedsWithAndNoTitle(params string[] options)
    {
        var stub = new StubHandler(HttpStatusCode.OK);

        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemCommands.EditAsync(Ctx(stub, ["workitem", "edit", "1", .. options])));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Empty(stub.Requests);
    }
}
