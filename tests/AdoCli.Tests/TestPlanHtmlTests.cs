namespace AdoCli.Tests;

using AdoCli.Api;
using AdoCli.Cli;
using Xunit;

public class TestPlanHtmlTests
{
    [Fact]
    public void EscapesContentAndRendersRepeatedSuiteAssignments()
    {
        var file = new TestPlanFile(TestPlanFile.CurrentFormat, "Project",
            new PlanInfo(10, "Plan <script>alert(1)</script>", "Active", "Sprint", 11),
            [
                new SuiteInfo(11, "Root & <name>", "StaticTestSuite", null, [42]),
                new SuiteInfo(12, "Child", "StaticTestSuite", 11, [42]),
            ],
            [
                new TestCaseEntry(42, 3, "Login & verify",
                    new Dictionary<string, string> { ["System.State"] = "Design" },
                    [new TestStep(1, "<p>Open &amp; check</p>", "<b>Page ready</b>")])
            ]);
        var html = TestPlanHtml.Render(file);
        Assert.Contains("href=\"#suite-12\"", html);
        Assert.Contains("Root &amp; &lt;name&gt;", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("Open &amp; check", html);
        Assert.Contains("Page ready", html);
        Assert.Equal(2, html.Split("Login &amp; verify").Length - 1);
        Assert.Contains("Expected result", html);
    }
    [Fact]
    public void ExpandsNestedSharedStepsAndReportsCyclesAndMissingItems()
    {
        var file = new TestPlanFile(TestPlanFile.CurrentFormat, "Project",
            new PlanInfo(1, "Plan", "Active", null, 2),
            [new SuiteInfo(2, "Suite", "StaticTestSuite", null, [3])],
            [new TestCaseEntry(3, 1, "Case", null, [new TestStep(1, SharedStepsId: 5366)])]);
        Dictionary<int, IReadOnlyList<TestStep>> shared = new()
        {
            [5366] = [new TestStep(2, Action: "Prepare", ExpectedResult: "Ready"),
                       new TestStep(3, SharedStepsId: 6045)],
            [6045] = [new TestStep(4, Action: "Verify", ExpectedResult: "Success"),
                       new TestStep(5, SharedStepsId: 5366)],
        };
        var html = TestPlanHtml.Render(file, shared);
        Assert.Contains("1.1", html);
        Assert.Contains("1.2.1", html);
        Assert.Contains("Verify", html);
        Assert.Contains("Success", html);
        Assert.Contains("Circular shared steps reference", html);

        var errors = new Dictionary<int, string> { [5366] = "Shared steps #5366 not found" };
        Assert.Contains("Shared steps #5366 not found", TestPlanHtml.Render(file, shared, errors));
    }

    [Theory]
    [InlineData("Gemeinsame Schritte")]
    [InlineData("Custom Shared Steps")]
    public async Task ResolvesSharedStepsRegardlessOfLocalizedWorkItemType(string type)
    {
        var xml = TestSteps.Build([new TestStep(1, "Enter username", "Input accepted")], 1);
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            value = new[] { new { id = 5366, fields = new Dictionary<string, string>
            {
                ["System.WorkItemType"] = type,
                [TestSteps.Field] = xml
            }}}
        });
        var stub = new StubHandler(_ => StubHandler.Json(payload));
        var client = new AdoClient("https://tfs", "token", handler: stub);
        var file = new TestPlanFile(TestPlanFile.CurrentFormat, "Project",
            new PlanInfo(10, "Plan", "Active", null, 11),
            [new SuiteInfo(11, "Root", "StaticTestSuite", null, [42])],
            [new TestCaseEntry(42, 1, "Case", null, [new TestStep(2, SharedStepsId: 5366)])]);

        var (shared, errors) = await TestPlanCommands.ResolveSharedStepsAsync(client, file);
        Assert.Empty(errors);
        Assert.Single(shared[5366]);
        Assert.Contains("Input accepted", TestPlanHtml.Render(file, shared));
    }

    [Fact]
    public void SidebarMatchesDocWizzNavigationPatterns()
    {
        var file = new TestPlanFile(TestPlanFile.CurrentFormat, "Project",
            new PlanInfo(1, "Plan", "Active", null, 10),
            [
                new SuiteInfo(10, "Root", "StaticTestSuite", null, [42]),
                new SuiteInfo(11, "Nested", "StaticTestSuite", 10, [42]),
            ],
            [new TestCaseEntry(42, 1, "Case", null, [])]);
        var html = TestPlanHtml.Render(file);
        Assert.Contains("id=\"site-nav\"", html);
        Assert.Contains("id=\"side-filter\"", html);
        Assert.Contains("<details open><summary>", html);
        Assert.Contains("href=\"#suite-11\"", html);
        Assert.Contains("aria-current", html);
        Assert.Contains("id=\"nav-toggle\"", html);
        Assert.Contains("No suites match.", html);
    }

    [Fact]
    public void HtmlReportSupportsSystemAndManualDarkMode()
    {
        var file = new TestPlanFile(TestPlanFile.CurrentFormat, "Project",
            new PlanInfo(1, "Plan", "Active", null, 10),
            [new SuiteInfo(10, "Root", "StaticTestSuite", null, [42])],
            [new TestCaseEntry(42, 1, "Case", null, [])]);
        var html = TestPlanHtml.Render(file);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("data-theme", html);
        Assert.Contains("id=\"theme-toggle\"", html);
        Assert.Contains("ado.testplan.theme", html);
    }

}
