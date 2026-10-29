namespace AdoCli.Tests;

using AdoCli.Cli;
using Xunit;

public sealed class TestClosureReportTests
{
    [Fact]
    public void SnapshotPreservesPointMetricsWithoutTreatingBlockedAsExecuted()
    {
        var progress = new TestPlanProgress.Report(12, "Release", "Area", 14, 5, 2, 1, 1, 1,
            [new TestPlanProgress.SuiteProgress(3, "Suite", 5, 2, 1, 1, 1)], []);
        var timestamp = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        var closure = TestClosureReport.FromProgress("Project", progress, timestamp);
        Assert.Equal("ado.test-closure/v1", closure.Format);
        Assert.Equal(3, closure.Executed);
        Assert.Equal(3, closure.Open);
        Assert.Equal(60, closure.ExecutionPercent);
        Assert.Equal(66.7, closure.PassPercent);
        Assert.Equal(timestamp, closure.GeneratedAt);
    }

    [Fact]
    public void SummaryEscapesUntrustedProjectAndArea()
    {
        var progress = new TestPlanProgress.Report(12, "Release", "<img src=x>", 14, 0, 0, 0, 0, 0, [], []);
        var report = TestClosureReport.FromProgress("<script>", progress, DateTimeOffset.UtcNow);
        var html = TestClosureReport.SummaryHtml(report);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img src=x&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img src=x>", html);
        Assert.Contains("not a release approval", html);
        Assert.Contains("Test points", html);
    }

    [Fact]
    public void AreaScopedHtmlExcludesUnrelatedCases()
    {
        var file = new TestPlanFile(TestPlanFile.CurrentFormat, "Project",
            new PlanInfo(12, "Release", "Active", null, 10),
            [new SuiteInfo(10, "Mixed suite", "StaticTestSuite", null, [1, 2])],
            [
                new TestCaseEntry(1, 1, "Alpha only",
                    new Dictionary<string, string> { ["System.AreaPath"] = "Product\\ALPHA\\Web" }, []),
                new TestCaseEntry(2, 1, "Beta only",
                    new Dictionary<string, string> { ["System.AreaPath"] = "Product\\BETA" }, [])
            ]);
        var scoped = TestClosureReport.FilterByArea(file, "Product\\ALPHA");
        var html = TestPlanHtml.Render(scoped, reportTitle: "Test closure report",
            introductionHtml: "<section id=\\\"closure\\\">Summary</section>");
        Assert.Contains("Alpha only", html);
        Assert.DoesNotContain("Beta only", html);
        Assert.Contains("<title>Test closure report</title>", html);
        Assert.Equal(1, html.Split("id=\\\"closure\\\"").Length - 1);
    }
}
