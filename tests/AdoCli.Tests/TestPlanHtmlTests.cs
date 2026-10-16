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
}
