namespace AdoCli.Tests;

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AdoCli.Api;
using AdoCli.Cli;

public class TestPlanTests
{
    // Steps as Azure DevOps stores them: HTML in formatted strings, a plain-text step, a shared steps reference.
    const string StepsOf101 = """<steps id="0" last="4"><step id="2" type="ActionStep"><parameterizedString isformatted="true">&lt;DIV&gt;&lt;P&gt;Open the login page&lt;/P&gt;&lt;/DIV&gt;</parameterizedString><parameterizedString isformatted="true">&lt;DIV&gt;&lt;P&gt;&lt;BR/&gt;&lt;/P&gt;&lt;/DIV&gt;</parameterizedString><description/></step><step id="3" type="ValidateStep"><parameterizedString isformatted="true">Log in as &lt;B&gt;jane&lt;/B&gt;</parameterizedString><parameterizedString isformatted="true">Start page shows „Willkommen“</parameterizedString><description/></step><step id="4" type="ActionStep"><parameterizedString isformatted="true">Log out</parameterizedString><parameterizedString isformatted="true"></parameterizedString><description/></step></steps>""";
    const string StepsOf102 = """<steps id="0" last="3"><step id="2" type="ValidateStep"><parameterizedString isformatted="false">Enter 1 &lt; 2""" + "\n" +
        """and submit</parameterizedString><parameterizedString isformatted="false">Accepted</parameterizedString><description>note</description></step><compref id="3" ref="555"></compref></steps>""";

    [Fact]
    public void ParsesStepsXmlIncludingPlainTextAndSharedSteps()
    {
        var (steps, last) = TestSteps.Parse(StepsOf101);
        Assert.Equal(4, last);
        Assert.Equal([2, 3, 4], steps.Select(s => s.Id!.Value));
        Assert.Equal("<DIV><P>Open the login page</P></DIV>", steps[0].Action);
        Assert.Equal("Start page shows „Willkommen“", steps[1].ExpectedResult);
        Assert.Equal("", steps[2].ExpectedResult);

        (steps, last) = TestSteps.Parse(StepsOf102);
        Assert.Equal(3, last);
        Assert.Equal("Enter 1 &lt; 2<br>and submit", steps[0].Action);
        Assert.Equal("note", steps[0].Description);
        Assert.Equal(new TestStep(3, SharedStepsId: 555), steps[1]);
        Assert.Empty(TestSteps.Parse(null).Steps);
    }

    [Fact]
    public void BuildKeepsIdsAndNumbersNewStepsAfterLast()
    {
        List<TestStep> steps = [new(2, "<P>a & b</P>", "ok"), new(Action: "new", ExpectedResult: ""), new(3, SharedStepsId: 555), new(Action: "another")];

        var xml = TestSteps.Build(steps, last: 7);

        Assert.Equal("""<steps id="0" last="9"><step id="2" type="ValidateStep"><parameterizedString isformatted="true">&lt;P&gt;a &amp; b&lt;/P&gt;</parameterizedString><parameterizedString isformatted="true">ok</parameterizedString><description></description></step>""" +
            """<step id="8" type="ActionStep"><parameterizedString isformatted="true">new</parameterizedString><parameterizedString isformatted="true"></parameterizedString><description></description></step>""" +
            """<compref id="3" ref="555" /><step id="9" type="ActionStep"><parameterizedString isformatted="true">another</parameterizedString><parameterizedString isformatted="true"></parameterizedString><description></description></step></steps>""", xml);
        var (parsed, last) = TestSteps.Parse(xml);
        Assert.Equal(9, last);
        Assert.Equal([2, 8, 3, 9], parsed.Select(s => s.Id!.Value));
    }

    [Theory]
    [InlineData(StepsOf101)]
    [InlineData(StepsOf102)]
    public void StepsSurviveParseAndBuild(string xml)
    {
        var (steps, last) = TestSteps.Parse(xml);
        var (again, lastAgain) = TestSteps.Parse(TestSteps.Build(steps, last));
        Assert.True(TestSteps.Same(steps, again));
        Assert.Equal(last, lastAgain);
    }

    [Theory]
    [InlineData("<steps><step>")]
    [InlineData("""<steps id="0" last="1"><parameter id="1"/></steps>""")]
    public void RejectsStepsItCannotRewrite(string xml) =>
        Assert.Throws<FormatException>(() => TestSteps.Parse(xml));

    [Fact]
    public async Task ExportsHierarchyTestCasesOnceWithRevisionsFieldsAndSteps()
    {
        var server = new FakeServer();

        var file = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);

        Assert.Equal(new PlanInfo(12, "Release 1", "Active", "Platform\\Sprint 1", 13), file.Plan);
        Assert.Equal(["13:-:101", "14:13:101,102", "15:13:103"], file.Suites!.Select(s => $"{s.Id}:{s.ParentId?.ToString() ?? "-"}:{string.Join(',', s.TestCaseIds)}"));
        Assert.Equal([101, 102, 103], file.TestCases!.Select(c => c.Id));
        var login = file.TestCases![0];
        Assert.Equal(5, login.Rev);
        Assert.Equal("Login works", login.Title);
        Assert.Equal("Jane Doe <jane@company.local>", login.Fields!["System.AssignedTo"]);
        Assert.Equal("2", login.Fields["Microsoft.VSTS.Common.Priority"]);
        Assert.Equal("", login.Fields["System.Tags"]);
        Assert.Equal(3, login.Steps!.Count);
        Assert.Empty(file.TestCases[2].Steps!);
        Assert.Contains("https://tfs/Platform/_apis/test/plans/12/suites/14/testcases?api-version=5.0", server.Urls);
    }

    [Fact]
    public async Task ExportImportExportIsLossless()
    {
        var server = new FakeServer();
        var path = Temp();

        Assert.Equal(0, await TestPlanCommands.ExportAsync(Ctx(server, "testplan", "export", "12", "--output", path)));
        var first = File.ReadAllText(path);
        Assert.Equal(0, await TestPlanCommands.ImportAsync(Ctx(server, "testplan", "import", path, "--yes")));
        Assert.Equal(0, await TestPlanCommands.ExportAsync(Ctx(server, "testplan", "export", "12", "--output", path)));

        Assert.Empty(server.Patches);
        Assert.Equal(first, File.ReadAllText(path));
        Assert.Contains("„Willkommen“", first);
        Assert.Contains("<DIV><P>Open the login page</P></DIV>", first);
        Assert.Equal(first, TestPlanFile.Read(first, "plan.json").Write());
    }

    [Fact]
    public async Task ImportsEditsOfSeveralTestCasesAndOnlyThem()
    {
        var server = new FakeServer();
        var file = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);
        var login = file.TestCases![0];
        var form = file.TestCases[1];
        var path = Save(file with
        {
            TestCases =
            [
                // Title, a field, one step's expected result, a new step at the end; step 4 removed.
                login with
                {
                    Title = "Login and logout work",
                    Fields = new(login.Fields!) { ["System.State"] = "Ready" },
                    Steps = [login.Steps![0], login.Steps[1] with { ExpectedResult = "Dashboard opens" }, new(Action: "Close the browser", ExpectedResult: "")],
                },
                // Steps reordered only.
                form with { Steps = [form.Steps![1], form.Steps[0]] },
                file.TestCases[2],
            ],
        });

        Assert.Equal(0, await TestPlanCommands.ImportAsync(Ctx(server, "testplan", "import", path, "--yes")));

        Assert.Equal([101, 102], server.Patches.Select(p => p.Id));
        var ops = server.Patches[0].Ops;
        Assert.Equal(("test", "/rev", "5"), Op(ops[0]));
        Assert.Equal(["/rev", "/fields/System.Title", "/fields/System.State", "/fields/Microsoft.VSTS.TCM.Steps"], ops.Select(o => (string)o!["path"]!));
        Assert.Equal(["/rev", "/fields/Microsoft.VSTS.TCM.Steps"], server.Patches[1].Ops.Select(o => (string)o!["path"]!));

        var again = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);
        var updated = again.TestCases![0];
        Assert.Equal(6, updated.Rev);
        Assert.Equal("Login and logout work", updated.Title);
        Assert.Equal("Ready", updated.Fields!["System.State"]);
        Assert.Equal("Jane Doe <jane@company.local>", updated.Fields["System.AssignedTo"]);
        // Existing step ids are kept, the new step gets the next id after "last" (4), the removed id is not reused.
        Assert.Equal([2, 3, 5], updated.Steps!.Select(s => s.Id!.Value));
        Assert.Equal("Dashboard opens", updated.Steps![1].ExpectedResult);
        Assert.Equal([3, 2], again.TestCases[1].Steps!.Select(s => s.Id!.Value));
        Assert.Equal(1, again.TestCases[2].Rev);
    }

    [Fact]
    public async Task ChangesListEachEditedStep()
    {
        var server = new FakeServer();
        var file = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);
        var login = file.TestCases![0];

        var import = await TestPlanCommands.PlanImportAsync(server.Client, file with
        {
            TestCases = [login with { Steps = [login.Steps![1] with { Action = "Log in" }, login.Steps[0], new(Action: "x")] }],
        });

        Assert.Equal(["step 1: action changed (was step 2)", "step 2: moved (was step 1)", "step 3: added", "removed (was step 3): Log out"], import.Updates.Single().Changes);
    }

    [Fact]
    public async Task DryRunWritesNothing()
    {
        var server = new FakeServer();
        var file = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);
        var path = Save(file with { TestCases = [file.TestCases![0] with { Title = "Renamed" }] });

        Assert.Equal(0, await TestPlanCommands.ImportAsync(Ctx(server, "testplan", "import", path, "--dry-run")));

        Assert.Empty(server.Patches);
    }

    [Fact]
    public async Task EditOfATestCaseChangedOnTheServerIsAConflictAndNothingIsWritten()
    {
        var server = new FakeServer();
        var file = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);
        server.Change(101, "System.State", "Closed");
        var path = Save(file with
        {
            TestCases = [file.TestCases![0] with { Title = "Mine" }, file.TestCases[1] with { Title = "Also mine" }],
        });

        Assert.Equal(AdoException.Conflict, await TestPlanCommands.ImportAsync(Ctx(server, "testplan", "import", path, "--yes")));
        Assert.Equal(AdoException.Conflict, await TestPlanCommands.ImportAsync(Ctx(server, "testplan", "import", path, "--dry-run")));

        Assert.Empty(server.Patches);
        var import = await TestPlanCommands.PlanImportAsync(server.Client, TestPlanFile.Read(File.ReadAllText(path), path));
        Assert.Contains("rev 5 -> 6", import.Conflicts.Single());
        Assert.Equal([102], import.Updates.Select(u => u.Id));
    }

    [Fact]
    public async Task UneditedTestCaseChangedOnTheServerIsLeftAlone()
    {
        var server = new FakeServer();
        var file = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);
        server.Change(101, "System.Title", "Renamed by a colleague");
        var path = Save(file);

        Assert.Equal(0, await TestPlanCommands.ImportAsync(Ctx(server, "testplan", "import", path, "--yes")));

        Assert.Empty(server.Patches);
        Assert.Contains("https://tfs/_apis/wit/workitems/101/revisions/5?api-version=5.0", server.Urls);
    }

    [Fact]
    public async Task ServerRefusingTheRevisionTestIsAConflict()
    {
        var server = new FakeServer { RefuseRevTest = true };

        var e = await Assert.ThrowsAsync<AdoException>(() => server.Client.UpdateWorkItemAsync(101, new Dictionary<string, string> { ["System.Title"] = "x" }, 5));

        Assert.Equal(AdoException.Conflict, e.ExitCode);
        Assert.Contains("TF26071", e.Message);
    }

    [Fact]
    public async Task ARejectedUpdateDoesNotStopTheOthers()
    {
        var server = new FakeServer();
        var file = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);
        var path = Save(file with
        {
            TestCases =
            [
                file.TestCases![0] with { Fields = new(file.TestCases[0].Fields!) { ["System.State"] = "Nonsense" } },
                file.TestCases[1] with { Title = "Fine" },
            ],
        });

        Assert.Equal(AdoException.General, await TestPlanCommands.ImportAsync(Ctx(server, "testplan", "import", path, "--yes")));

        Assert.Equal([102], server.Patches.Select(p => p.Id));
    }

    [Fact]
    public async Task MissingPlanIsNotFound()
    {
        var server = new FakeServer();

        var e = await Assert.ThrowsAsync<AdoException>(() => TestPlanCommands.ExportAsync(server.Client, "Platform", 99));

        Assert.Equal(AdoException.NotFound, e.ExitCode);
        Assert.Contains("Test plan 99 not found", e.Message);
    }

    [Fact]
    public async Task MissingOrWrongWorkItemsAreErrorsAndNothingIsWritten()
    {
        var server = new FakeServer();
        var file = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);
        var path = Save(file with
        {
            TestCases = [file.TestCases![0] with { Title = "x" }, file.TestCases[1] with { Id = 404 }, file.TestCases[2] with { Id = 900 }],
        });

        Assert.Equal(AdoException.General, await TestPlanCommands.ImportAsync(Ctx(server, "testplan", "import", path, "--yes")));

        Assert.Empty(server.Patches);
        var import = await TestPlanCommands.PlanImportAsync(server.Client, TestPlanFile.Read(File.ReadAllText(path), path));
        Assert.Equal(["#404: not found (deleted, or you may not see it)", "#900: is a Bug, not a Test Case"], import.Errors);
    }

    [Fact]
    public async Task UnknownStepIdIsAnError()
    {
        var server = new FakeServer();
        var file = await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);

        var import = await TestPlanCommands.PlanImportAsync(server.Client, file with
        {
            TestCases = [file.TestCases![2] with { Steps = [new(42, "copied from elsewhere", "")] }],
        });

        Assert.Contains("step id 42 does not exist", import.Errors.Single());
    }

    [Fact]
    public void ValidationListsEveryProblem()
    {
        var json = """
            {
              "format": "ado-testplan/1",
              "testCases": [
                {"id": 1, "rev": 0, "title": "", "steps": [{"id": 2, "action": "a"}, {"id": 2, "action": "b"}, {"expectedResult": "c"}]},
                {"id": 1, "rev": 3, "title": "t", "fields": {"System.Title": "x"}, "steps": [{"sharedStepsId": 9, "action": "a"}]},
                {"id": 2, "rev": 3, "title": "t"}
              ]
            }
            """;

        var e = Assert.Throws<AdoException>(() => TestPlanFile.Read(json, "plan.json"));

        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Equal("""
            plan.json is not a valid test plan file:
              testCases[0] (#1): rev must be the revision the test case was exported at
              testCases[0] (#1): title must not be empty
              testCases[0] (#1): steps[1]: id 2 is used by another step; leave the id out for a new step
              testCases[0] (#1): steps[2]: action is missing
              testCases[1] (#1): test case #1 is listed more than once
              testCases[1] (#1): fields must not contain System.Title; use title
              testCases[1] (#1): steps[0]: a shared steps reference has no action, expectedResult or description; edit the Shared Steps work item #9 instead
              testCases[2] (#2): steps is missing (use [] for a test case without steps)
            """.ReplaceLineEndings("\n"), e.Message);
    }

    [Theory]
    [InlineData("""{"format":"ado-testplan/1","testCases":[{"id":1,"rev":1,"title":"t","steps":[{"action":"a","expectedResults":"typo"}]}]}""", "$.testCases[0].steps[0].expectedResults): unknown property")]
    [InlineData("""{"format":"ado-testplan/1","testCases":[""", "line 1")]
    [InlineData("""{"format":"other","testCases":[]}""", "format must be")]
    public void RejectsInvalidFiles(string json, string message)
    {
        var e = Assert.Throws<AdoException>(() => TestPlanFile.Read(json, "plan.json"));
        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void NumbersAreReadAsFieldText()
    {
        var file = TestPlanFile.Read("""{"format":"ado-testplan/1","testCases":[{"id":1,"rev":1,"title":"t","fields":{"Microsoft.VSTS.Common.Priority":1},"steps":[]}]}""", "f");
        Assert.Equal("1", file.TestCases![0].Fields!["Microsoft.VSTS.Common.Priority"]);
    }

    [Fact]
    public async Task ListShowsPlans()
    {
        var server = new FakeServer();

        var plans = await server.Client.GetTestPlansAsync("Platform");

        Assert.Equal("Release 1", plans.Single().Name);
        Assert.Equal("https://tfs/Platform/_apis/test/plans?$top=100&$skip=0&api-version=5.0", server.Urls[0]);
    }

    [Fact]
    public async Task LegacyTestEndpointsUseFiveEvenWhenGlobalApiVersionIsSix()
    {
        var server = new FakeServer("6.0");
        var plans = await server.Client.GetTestPlansAsync("Platform");
        Assert.Single(plans);
        await TestPlanCommands.ExportAsync(server.Client, "Platform", 12);

        Assert.Contains("https://tfs/Platform/_apis/test/plans?$top=100&$skip=0&api-version=5.0", server.Urls);
        Assert.Contains("https://tfs/Platform/_apis/test/plans/12?api-version=5.0", server.Urls);
        Assert.Contains("https://tfs/Platform/_apis/test/plans/12/suites?$top=100&$skip=0&api-version=5.0", server.Urls);
        Assert.Contains("https://tfs/Platform/_apis/test/plans/12/suites/14/testcases?api-version=5.0", server.Urls);
        Assert.Contains(server.Urls, url => url.Contains("/_apis/wit/workitems?") && url.EndsWith("api-version=6.0"));
    }

    [Fact]
    public async Task ShowSummarizesSuitesAndCountsSharedCasesOnlyOnce()
    {
        var server = new FakeServer("6.0");
        var summary = await TestPlanCommands.GetOverviewAsync(server.Client, "Platform", 12);
        Assert.Equal(12, summary.Id);
        Assert.Equal(13, summary.RootSuiteId);
        Assert.Equal(3, summary.Suites.Count);
        Assert.Equal(3, summary.UniqueTestCaseCount);
        Assert.Equal([13, 14, 15], summary.Suites.Select(s => s.Id));
        Assert.Equal(13, summary.Suites[1].ParentId);
        Assert.Contains(server.Urls, u => u.Contains("/_apis/test/plans/12/suites/14/testcases") && u.EndsWith("api-version=5.0"));
        Assert.DoesNotContain(server.Urls, u => u.Contains("/_apis/wit/"));
        Assert.Empty(server.Patches);
    }

    [Fact]
    public async Task ShowMissingPlanReturnsNotFound()
    {
        var server = new FakeServer();
        var error = await Assert.ThrowsAsync<AdoException>(() =>
            TestPlanCommands.GetOverviewAsync(server.Client, "Platform", 99));
        Assert.Equal(AdoException.NotFound, error.ExitCode);
    }

    [Fact]
    public async Task ShowJsonIsValidWithoutAdditionalStdout()
    {
        var server = new FakeServer();
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            Assert.Equal(0, await TestPlanCommands.ShowAsync(Ctx(server, "testplan", "show", "12", "--json")));
        }
        finally { Console.SetOut(original); }
        var obj = JsonNode.Parse(writer.ToString())!;
        Assert.Equal(12, (int)obj["id"]!);
        Assert.Equal(3, (int)obj["uniqueTestCaseCount"]!);
        Assert.Equal(3, obj["suites"]!.AsArray().Count);
    }

    [Fact]
    public async Task DetailsFetchCasesOnceAndKeepTheirSteps()
    {
        var server = new FakeServer("6.0");
        var view = await TestPlanCommands.GetDetailedAsync(server.Client, "Platform", 12, "details");
        Assert.Equal(3, view.Plan.UniqueTestCaseCount);
        Assert.Equal(3, view.TestCases!.Count);
        Assert.Equal(3, view.TestCases.Single(c => c.Id == 101).Steps.Count);
        Assert.Null(view.Results);
        Assert.Single(server.Urls.Where(u => u.Contains("/_apis/wit/workitems?")));
        Assert.Empty(server.Patches);
    }

    [Fact]
    public async Task ResultsAreActualRunOutcomesNotExpectedStepResults()
    {
        var server = new FakeServer("6.0");
        var view = await TestPlanCommands.GetDetailedAsync(server.Client, "Platform", 12, "results");
        Assert.Null(view.TestCases);
        Assert.Single(view.Results!);
        Assert.Equal("Failed", view.Results[0].Outcome);
        Assert.Equal(101, view.Results[0].TestCaseId);
        Assert.Equal(22, view.Results[0].RunId);
        Assert.DoesNotContain(server.Urls, u => u.Contains("/_apis/wit/workitems?"));
    }

    [Fact]
    public async Task AllIncludesDefinitionsAndRunOutcomes()
    {
        var server = new FakeServer();
        var view = await TestPlanCommands.GetDetailedAsync(server.Client, "Platform", 12, "all");
        Assert.NotNull(view.TestCases);
        Assert.NotNull(view.Results);
        Assert.Equal(3, view.TestCases!.Count);
        Assert.Single(view.Results!);
    }

    [Fact]
    public async Task ShowRejectsUnsupportedWithModeBeforeNetwork()
    {
        var server = new FakeServer();
        var error = await Assert.ThrowsAsync<AdoException>(() =>
            TestPlanCommands.ShowAsync(Ctx(server, "testplan", "show", "12", "--with", "everything")));
        Assert.Equal(AdoException.InvalidUsage, error.ExitCode);
        Assert.Contains("details, results or all", error.Message);
        Assert.Empty(server.Urls);
    }

    static (string, string, string) Op(JsonNode? op) => ((string)op!["op"]!, (string)op["path"]!, op["value"]!.ToJsonString());

    static Context Ctx(FakeServer server, params string[] argv) =>
        new(Args.Parse([.. argv, "--project", "Platform"]), new Config(), server.Client);

    static string Temp() => Path.Combine(Path.GetTempPath(), $"ado-testplan-{Guid.NewGuid():N}.json");

    static string Save(TestPlanFile file)
    {
        var path = Temp();
        File.WriteAllText(path, file.Write());
        return path;
    }

    /// <summary>An Azure DevOps Server with one test plan, kept in memory: work item updates create new revisions.</summary>
    sealed class FakeServer : HttpMessageHandler
    {
        readonly Dictionary<int, List<JsonObject>> _revisions = [];

        public FakeServer(string apiVersion = AdoClient.DefaultApiVersion)
        {
            Client = new AdoClient("https://tfs", "pat", handler: this, apiVersion: apiVersion);
            Add(101, 5, "Login works", StepsOf101, new JsonObject { ["displayName"] = "Jane Doe", ["uniqueName"] = "jane@company.local" });
            Add(102, 2, "Form validation", StepsOf102, null);
            Add(103, 1, "Checkout", null, null);
            Add(900, 1, "A bug", null, null, "Bug");
        }

        public AdoClient Client { get; }
        public bool RefuseRevTest { get; init; }
        public List<string> Urls { get; } = [];
        public List<(int Id, JsonArray Ops)> Patches { get; } = [];

        void Add(int id, int rev, string title, string? steps, JsonObject? assignedTo, string type = "Test Case")
        {
            var fields = new JsonObject
            {
                ["System.Id"] = id, ["System.Rev"] = rev, ["System.WorkItemType"] = type, ["System.Title"] = title,
                ["System.State"] = "Design", ["Microsoft.VSTS.Common.Priority"] = 2, ["System.AreaPath"] = "Platform",
                ["System.IterationPath"] = "Platform\\Sprint 1",
            };
            if (steps is not null)
                fields[TestSteps.Field] = steps;
            if (assignedTo is not null)
                fields["System.AssignedTo"] = assignedTo;
            // Earlier revisions only differ in their number here.
            _revisions[id] = Enumerable.Range(1, rev).Select(r => { var f = (JsonObject)fields.DeepClone(); f["System.Rev"] = r; return f; }).ToList();
        }

        /// <summary>Someone else edits a field: a new revision.</summary>
        public void Change(int id, string field, string value)
        {
            var next = (JsonObject)_revisions[id][^1].DeepClone();
            next[field] = value;
            next["System.Rev"] = _revisions[id].Count + 1;
            _revisions[id].Add(next);
        }

        static string Item(int id, JsonObject fields) => new JsonObject { ["id"] = id, ["rev"] = fields["System.Rev"]!.DeepClone(), ["fields"] = fields.DeepClone() }.ToJsonString();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!;
            Urls.Add(url.ToString());
            var path = url.AbsolutePath.ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            const string plan = """{"id":12,"name":"Release 1","state":"Active","iteration":"Platform\\Sprint 1","rootSuite":{"id":"13"}}""";
            return path switch
            {
                ["platform", "_apis", "test", "plans"] => Json($"{{\"value\":[{plan}],\"count\":1}}"),
                ["platform", "_apis", "test", "plans", "12"] => Json(plan),
                ["platform", "_apis", "test", "plans", _] => Error(HttpStatusCode.NotFound, $"Test plan {path[4]} not found."),
                ["platform", "_apis", "test", "runs"] =>
                    Json("""{"value":[{"id":22,"completedDate":"2026-10-01T10:00:00Z"}]}"""),
                ["platform", "_apis", "test", "runs", "22", "results"] =>
                    Json("""{"value":[{"id":1,"testCase":{"id":"101"},"outcome":"Failed","state":"Completed","errorMessage":"Assertion failed"}]}"""),
                ["platform", "_apis", "test", "plans", "12", "suites"] => Json("""
                    {"value":[{"id":13,"name":"Release 1","suiteType":"StaticTestSuite"},
                      {"id":14,"name":"Login","suiteType":"StaticTestSuite","parent":{"id":"13"}},
                      {"id":15,"name":"Checkout","suiteType":"RequirementTestSuite","parent":{"id":"13"}}]}
                    """),
                ["platform", "_apis", "test", "plans", "12", "suites", var suite, "testcases"] =>
                    Json($"{{\"value\":[{string.Join(',', (suite switch { "13" => [101], "14" => [101, 102], _ => new[] { 103 } }).Select(i => $"{{\"testCase\":{{\"id\":\"{i}\"}}}}"))}]}}"),
                ["_apis", "wit", "workitems"] => Json($"{{\"value\":[{string.Join(',', query["ids"]!.Split(',').Select(int.Parse)
                    .Where(_revisions.ContainsKey).Select(i => Item(i, _revisions[i][^1])))}]}}"),
                ["_apis", "wit", "workitems", var id, "revisions", var rev] => Json(Item(int.Parse(id), _revisions[int.Parse(id)][int.Parse(rev) - 1])),
                ["_apis", "wit", "workitems", var id] when request.Method == HttpMethod.Patch =>
                    Patch(int.Parse(id), JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsArray()),
                _ => Error(HttpStatusCode.NotFound, $"no route for {url}"),
            };
        }

        HttpResponseMessage Patch(int id, JsonArray ops)
        {
            var current = _revisions[id][^1];
            var next = (JsonObject)current.DeepClone();
            foreach (var op in ops)
            {
                var path = (string)op!["path"]!;
                if ((string)op["op"]! == "test")
                {
                    if (RefuseRevTest || path != "/rev" || (int)op["value"]! != (int)current["System.Rev"]!)
                        return Error(HttpStatusCode.PreconditionFailed, "TF26071: This work item has been changed by someone else since you opened it.");
                }
                else if ((string)op["value"]! == "Nonsense")
                    return Error(HttpStatusCode.BadRequest, "TF401320: Rule Error for field State.");
                else
                    next[path["/fields/".Length..]] = op["value"]!.DeepClone();
            }
            next["System.Rev"] = _revisions[id].Count + 1;
            _revisions[id].Add(next);
            Patches.Add((id, ops));
            return Json(Item(id, next));
        }

        static HttpResponseMessage Json(string body) => StubHandler.Json(body);

        static HttpResponseMessage Error(HttpStatusCode status, string message) =>
            new(status) { Content = new StringContent(new JsonObject { ["message"] = message }.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
