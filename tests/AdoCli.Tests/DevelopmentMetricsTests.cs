namespace AdoCli.Tests;

using AdoCli.Api;
using AdoCli.Cli;
using Xunit;

public sealed class DevelopmentMetricsTests
{
    private static PullRequest Pr(int id, DateTimeOffset? opened, DateTimeOffset? closed) =>
        new(id, "Title", null, closed is null ? "active" : "completed",
            new IdentityRef("Dev"), new Repository(Guid.Empty, "repo", null, null, null, new ProjectRef("Project")),
            "refs/heads/feature", "refs/heads/main", null, null, null, opened, closed);

    [Fact]
    public void CountsOpenedAndClosedSeparatelyByCalendarMonth()
    {
        var now = new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);
        var prs = new[]
        {
            Pr(1, new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero)),
            Pr(2, new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), null),
            Pr(3, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero))
        };
        var report = DevelopmentMetrics.Aggregate("Project", "Product\\ALPHA", 2, now, prs, 5);
        Assert.Equal(2, report.Opened);
        Assert.Equal(2, report.Closed);
        Assert.Equal(1, report.Timeline[0].Opened);
        Assert.Equal(1, report.Timeline[0].Closed);
        Assert.Equal(1, report.Timeline[1].Opened);
        Assert.Equal(1, report.Timeline[1].Closed);
        Assert.Equal(5, report.UnlinkedPullRequests);
    }

    [Fact]
    public void AverageExcludesMissingAndInvalidDurations()
    {
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, DevelopmentMetrics.AverageDays([
            Pr(1, start, start.AddDays(2)),
            Pr(2, start, start.AddDays(-1)),
            Pr(3, start, null)
        ]));
        Assert.Null(DevelopmentMetrics.AverageDays([Pr(4, start, null)]));
    }

    [Fact]
    public void HtmlEscapesAreaName()
    {
        var report = DevelopmentMetrics.Aggregate("Project", "<script>", 1,
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), [], 0);
        var html = DevelopmentMetrics.Html(report);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("Area: <script>", html);
        Assert.Contains("Monthly pull requests", html);
    }
    [Fact]
    public void LifecycleMetricsDistinguishCompletedAbandonedAndOpen()
    {
        var now = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        var start = now.AddDays(-12);
        var merged = Pr(1, start, start.AddDays(2));
        var abandoned = Pr(2, start, start.AddDays(6)) with { Status = "abandoned" };
        var open = Pr(3, start, null);
        var result = DevelopmentMetrics.Aggregate("Project", "Area", 1, now,
            [merged, abandoned, open], 0);
        Assert.Equal(1, result.Merged);
        Assert.Equal(1, result.Abandoned);
        Assert.Equal(1, result.OpenAtEnd);
        Assert.Equal(4, result.MedianDays);
        var html = DevelopmentMetrics.Html(result);
        Assert.Contains("Interactive monthly trend", html);
        Assert.Contains("data-metric=\"opened\"", html);
        Assert.Contains("data-metric=\"duration\"", html);
    }

    [Fact]
    public void OlderActivePullRequestsRemainCandidates()
    {
        var cutoff = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var old = cutoff.AddMonths(-2);
        Assert.True(DevelopmentMetrics.IsCandidate(Pr(1, old, null), cutoff));
        Assert.False(DevelopmentMetrics.IsCandidate(
            Pr(2, old, cutoff.AddDays(-1)), cutoff));
        Assert.True(DevelopmentMetrics.IsCandidate(
            Pr(3, old, cutoff.AddDays(1)), cutoff));
    }

    [Fact]
    public void MedianHandlesOddEvenAndMissingDates()
    {
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, DevelopmentMetrics.MedianDays([
            Pr(1, start, start.AddDays(1)),
            Pr(2, start, start.AddDays(2)),
            Pr(3, start, start.AddDays(9))
        ]));
        Assert.Equal(3, DevelopmentMetrics.MedianDays([
            Pr(1, start, start.AddDays(2)),
            Pr(2, start, start.AddDays(4))
        ]));
        Assert.Null(DevelopmentMetrics.MedianDays([Pr(3, start, null)]));
    }

    [Fact]
    public void NoAreaMatchesExplainsUnlinkedPullRequests()
    {
        var message = DevelopmentMetrics.NoMatchesMessage("LogDB-Web", 17);
        Assert.Contains("17 candidate PR(s)", message);
        Assert.Contains("no Area Path", message);
        Assert.Contains("--scope repository", message);
        Assert.Contains("not Area Path metrics", message);
    }

    [Fact]
    public void ProgressWritesBoundedUpdatesToStderrStream()
    {
        using var writer = new StringWriter();
        var update = DevelopmentMetrics.Progress(writer, interactive: false);
        for (var done = 1; done <= 100; done++)
            update(done, 100, done / 2, 0);
        var output = writer.ToString();
        Assert.Contains("100% (100/100)", output);
        Assert.Contains("matched: 50", output);
        Assert.True(output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 22);
    }

    [Fact]
    public void EmptyCandidateSetReportsCompletion()
    {
        using var writer = new StringWriter();
        DevelopmentMetrics.Progress(writer, interactive: false)(0, 0, 0, 0);
        Assert.Contains("100% (0/0)", writer.ToString());
    }

    [Theory]
    [InlineData("LogDB-Web/XXX", "LogDB-Web\\XXX")]
    [InlineData(" LogDB-Web/XXX/ ", "LogDB-Web\\XXX")]
    [InlineData("LogDB-Web\\XXX", "LogDB-Web\\XXX")]
    public void NormalizesAreaPathSeparators(string input, string expected)
    {
        Assert.Equal(expected, DevelopmentMetrics.NormalizeAreaPath(input));
        Assert.True(TestPlanProgress.InsideArea(expected + "\\Child", expected));
    }

    [Fact]
    public void DashboardShowsInteractiveLineChartAboveKpiCards()
    {
        var report = DevelopmentMetrics.Aggregate("Project", "Area", 2,
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), [], 0);
        var html = DevelopmentMetrics.Html(report);
        Assert.Contains("KPI development", html);
        Assert.Contains("<polyline", html);
        Assert.Contains("data-series=\"opened\"", html);
        Assert.Contains("data-series=\"closed\"", html);
        Assert.Contains("data-series=\"duration\"", html);
        Assert.True(html.IndexOf("top-trend", StringComparison.Ordinal) <
                    html.IndexOf("class=\"kpis\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TopTrendOffersSvgSafeTogglesAndAllMonthNames()
    {
        var report = DevelopmentMetrics.Aggregate("Project", "Area", 3,
            new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero), [], 0);
        var html = DevelopmentMetrics.TopLineChart(report);
        Assert.Contains("Jan 2026", html);
        Assert.Contains(">Feb</text>", html);
        Assert.Contains(">Mar</text>", html);
        Assert.Contains("group.removeAttribute(\"hidden\")", html);
        Assert.Contains("group.setAttribute(\"hidden\", \"\")", html);
        Assert.DoesNotContain("group.hidden =", html);
        Assert.Contains("data-series=\"closed\"", html);
    }

    [Fact]
    public void MonthLabelsDoNotRequireNamedCultures()
    {
        var report = DevelopmentMetrics.Aggregate("Project", "Area", 12,
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), [], 0);
        var html = DevelopmentMetrics.TopLineChart(report);
        Assert.Contains(">Nov 2025</text>", html);
        Assert.Contains(">Jan 2026</text>", html);
        Assert.Contains(">Oct</text>", html);
        Assert.DoesNotContain("en-US", html);
    }

    [Fact]
    public void TrendChartExplainsTheSelectedSeriesAndMissingData()
    {
        var report = DevelopmentMetrics.Aggregate("Project", "Area", 2,
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), [], 0);
        var html = DevelopmentMetrics.TopLineChart(report);
        Assert.Contains("id=\"top-trend-status\"", html);
        Assert.Contains("no data for this period", html);
        Assert.Contains("labels[selected]", html);
        Assert.Contains("querySelectorAll('circle')", html);
    }

    [Fact]
    public void CommitMetricsExcludeUnavailableCountsAndKeepEntirePrInventory()
    {
        var report = DevelopmentMetrics.Aggregate("Project", "Area", 1,
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), [], 0);
        var details = new[]
        {
            new DevelopmentMetrics.PrDetail(12, "<Unsafe>", "Alice", "refs/heads/f1", "refs/heads/main", 4),
            new DevelopmentMetrics.PrDetail(13, "Fix", "Bob", "refs/heads/f2", "refs/heads/main", 2),
            new DevelopmentMetrics.PrDetail(14, "Change", "Carol", "refs/heads/f3", "refs/heads/main", null)
        };
        var enriched = DevelopmentMetrics.WithPullRequests(report, details);
        Assert.Equal(6, enriched.Commits);
        Assert.Equal(3, enriched.AverageCommitsPerPr);
        Assert.Equal(1, enriched.CommitCountUnavailable);
        Assert.Equal(3, enriched.PullRequests!.Count);
        var html = DevelopmentMetrics.Html(enriched);
        Assert.Contains("Avg commits/PR", html);
        Assert.Contains("Commits", html);
        Assert.Contains("&lt;Unsafe&gt;", html);
        Assert.DoesNotContain("<Unsafe>", html);
        Assert.Contains("refs/heads/f1", html);
        Assert.True(html.IndexOf("id=\"pr-inventory\"", StringComparison.Ordinal) >
                    html.IndexOf("Scope and definitions", StringComparison.Ordinal));
    }

    [Fact]
    public void CommitAverageIsNotReportedWhenAllCountsAreUnavailable()
    {
        var report = DevelopmentMetrics.Aggregate("Project", "Area", 1,
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), [], 0);
        var enriched = DevelopmentMetrics.WithPullRequests(report,
            [new DevelopmentMetrics.PrDetail(42, "PR", "Alice", "branch", "main", null)]);
        Assert.Null(enriched.AverageCommitsPerPr);
        Assert.Equal(1, enriched.CommitCountUnavailable);
    }

    [Fact]
    public void InventoryDisplaysApproversAndClosureIdentity()
    {
        var pr = Pr(42, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero)) with
        {
            Reviewers = [new Reviewer("Alice", 10), new Reviewer("Bob", 5), new Reviewer("Charlie", -10)],
            ClosedBy = new IdentityRef("Release Manager")
        };
        var detail = DevelopmentMetrics.Detail(pr, 3);
        Assert.Equal("Alice, Bob", detail.ApprovedBy);
        Assert.Equal("Release Manager", detail.ClosedBy);
        var report = DevelopmentMetrics.WithPullRequests(
            DevelopmentMetrics.Aggregate("Project", "Area", 1,
                new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), [pr], 0), [detail]);
        var html = DevelopmentMetrics.PrInventoryHtml(report);
        Assert.Contains("Approved by", html);
        Assert.Contains("Closed by", html);
        Assert.Contains("Alice, Bob", html);
        Assert.Contains("Release Manager", html);
        Assert.DoesNotContain("Charlie", html);
    }

    [Fact]
    public void PrInventoryIncludesDocWizzStyleTableControls()
    {
        var report = DevelopmentMetrics.Aggregate("Project", "Area", 1,
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), [], 0);
        var html = DevelopmentMetrics.PrInventoryHtml(report);
        Assert.Contains("id=\"pr-table-filter\"", html);
        Assert.Contains("id=\"pr-table-size\"", html);
        Assert.Contains("id=\"pr-table-prev\"", html);
        Assert.Contains("id=\"pr-table-next\"", html);
        Assert.Contains("aria-sort", html);
        Assert.Contains("sortColumn === 0", html);
        Assert.Contains("filter.addEventListener('input'", html);
    }

    [Fact]
    public void DashboardRendersAreaTestCoverageSnapshot()
    {
        var report = DevelopmentMetrics.Aggregate("Project", "Area", 1,
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), [], 0)
            with { Tests = new DevelopmentMetrics.TestMetrics(10, 3, 7, 24, 4, 2) };
        var html = DevelopmentMetrics.Html(report);
        Assert.Contains("Tests &amp; coverage", html);
        Assert.Contains("Stories without test cases", html);
        Assert.Contains("Test cases outside plans", html);
        Assert.Contains("test links and plan assignments are not limited", html);
        Assert.True(html.IndexOf("Tests &amp; coverage", StringComparison.Ordinal) <
            html.IndexOf("Monthly pull requests", StringComparison.Ordinal));
    }

    [Fact]
    public void LinkedTestCasesAreDetectedWithoutCheckingTheirAreaPath()
    {
        var story = new WorkItem(100, []) {
            Relations = [
                new WorkItemRelation("Microsoft.VSTS.Common.TestedBy-Forward",
                    "https://dev.azure.com/example/_apis/wit/workItems/601"),
                new WorkItemRelation("Microsoft.VSTS.Common.TestedBy-Reverse",
                    "https://dev.azure.com/example/_apis/wit/workItems/602"),
                new WorkItemRelation("System.LinkTypes.Hierarchy-Forward",
                    "https://dev.azure.com/example/_apis/wit/workItems/999")
            ]
        };
        Assert.Equal([601, 602], DevelopmentMetrics.LinkedTestCaseIds(story).Order().ToArray());
        Assert.Empty(DevelopmentMetrics.LinkedTestCaseIds(new WorkItem(101, [])));
    }

}
