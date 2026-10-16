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

}
