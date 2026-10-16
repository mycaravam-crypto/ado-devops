namespace AdoCli.Tests;

using AdoCli.Api;
using AdoCli.Cli;
using Xunit;

public class TestPlanCoverageTests
{
    [Fact]
    public void CoveragePercentagesHandleZeroAndDistinctDenominators()
    {
        Assert.Equal(0, TestPlanCoverage.Percent(0, 0));
        Assert.Equal(66.7, TestPlanCoverage.Percent(2, 3));
        var report = new TestPlanCoverage.Report(1, "ALPHA", "Product\\ALPHA", 3, 2, 10, 8, 6, []);
        Assert.Equal(66.7, report.RequirementCoverage);
        Assert.Equal(80, report.ExecutionCoverage);
        Assert.Equal(75, report.PassRate);
    }

    [Fact]
    public void LinksAreRestrictedToTestedByAndHtmlIsEscaped()
    {
        Assert.True(TestPlanCoverage.IsTestLink(new WorkItemRelation(
            "Microsoft.VSTS.Common.TestedBy-Forward", "https://example/_apis/wit/workitems/42")));
        Assert.False(TestPlanCoverage.IsTestLink(new WorkItemRelation(
            "System.LinkTypes.Hierarchy-Forward", "https://example/_apis/wit/workitems/42")));
        Assert.Equal(42, TestPlanCoverage.LinkedId("https://example/_apis/wit/workitems/42"));
        Assert.Null(TestPlanCoverage.LinkedId("https://example/not-an-id"));
        var report = new TestPlanCoverage.Report(1, "<script>hello</script>", "Product\\ALPHA",
            1, 0, 0, 0, 0, [new TestPlanCoverage.Requirement(5, "<img src=x>", 0, false)]);
        var html = TestPlanCoverage.Html(report);
        Assert.DoesNotContain("<script>hello</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img src=x&gt;", html);
    }
}
