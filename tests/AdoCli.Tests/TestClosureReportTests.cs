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
        Assert.Equal(3, closure.Outstanding);
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
            introductionHtml: "<section id=\"closure\">Summary</section>");
        Assert.Contains("Alpha only", html);
        Assert.DoesNotContain("Beta only", html);
        Assert.Contains("<title>Test closure report</title>", html);
        Assert.Equal(1, html.Split("id=\"closure\"").Length - 1);
    }
    [Fact]
    public void SignatureFieldsAppearForBothPartiesWithoutPretendingToBeSigned()
    {
        var html = TestClosureReport.SignatureHtml();
        Assert.Contains("Kunde / Customer", html);
        Assert.Contains("Lieferant / Supplier", html);
        Assert.Contains("signature-line", html);
        Assert.Contains("not automatic approval", html);
    }
    [Fact]
    public void SummaryFollowsHeadingAndSignaturesFollowSuites()
    {
        var file = new TestPlanFile(TestPlanFile.CurrentFormat, "Project",
            new PlanInfo(12, "Release", "Active", null, 10),
            [new SuiteInfo(10, "Last suite", "StaticTestSuite", null, [1])],
            [new TestCaseEntry(1, 1, "Last case", null, [])]);
        var html = TestPlanHtml.Render(file, reportTitle: "Test closure report",
            introductionHtml: "<section id=\"closure\">Summary</section>",
            closingHtml: TestClosureReport.SignatureHtml());
        var heading = html.IndexOf("<h1>Release</h1>", StringComparison.Ordinal);
        var summary = html.IndexOf("id=\"closure\"", StringComparison.Ordinal);
        var suite = html.IndexOf("Last case", StringComparison.Ordinal);
        var signatures = html.IndexOf("id=\"signatures\"", StringComparison.Ordinal);
        Assert.True(heading >= 0 && heading < summary);
        Assert.True(summary < suite && suite < signatures);
        Assert.True(signatures < html.IndexOf("</main>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Passed", "Passed")]
    [InlineData("Failed", "Failed")]
    [InlineData("Blocked", "Blocked")]
    [InlineData("NotExecuted", "NotRun")]
    public void CaseOutcomeForSinglePoint(string source, string expected) =>
        Assert.Equal(expected, TestClosureReport.CaseOutcome([source]));

    [Fact]
    public void MultipleConfigurationsDoNotHideFailuresOrUnexecutedTests()
    {
        Assert.Equal("Failed", TestClosureReport.CaseOutcome(["Passed", "Failed"]));
        Assert.Equal("NotRun", TestClosureReport.CaseOutcome(["Passed", "NotExecuted"]));
        Assert.Equal("Blocked", TestClosureReport.CaseOutcome(["Passed", "Blocked"]));
        Assert.Equal("NotRun", TestClosureReport.CaseOutcome([]));
        Assert.Equal("Passed", TestClosureReport.CaseOutcome(["Passed", "Passed"]));
    }

    [Theory]
    [InlineData("Passed", "Passed", "#168567")]
    [InlineData("Failed", "Failed", "#c0392b")]
    [InlineData("Blocked", "Blocked", "#bf8616")]
    [InlineData("NotRun", "Not run", "#667587")]
    public void TestCaseTrafficLightAppearsNextToCaseName(string outcome, string label, string color)
    {
        var file = new TestPlanFile(TestPlanFile.CurrentFormat, "Project",
            new PlanInfo(12, "Release", "Active", null, 10),
            [new SuiteInfo(10, "Suite", "StaticTestSuite", null, [1])],
            [new TestCaseEntry(1, 1, "Login case", null, [])]);
        var map = new Dictionary<(int SuiteId, int CaseId), string> { [(10, 1)] = outcome };
        var html = TestPlanHtml.Render(file, caseOutcomes: map);
        Assert.Contains("Login case</h3>", html);
        Assert.Contains(">" + label + "</div>", html);
        Assert.Contains("--status-color:" + color, html);
        Assert.Contains("class=\"case-status\"", html);
        Assert.Contains("background:transparent", html);
        Assert.Contains("aria-hidden", html);
    }

}
