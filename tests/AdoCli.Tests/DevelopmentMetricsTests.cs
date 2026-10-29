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
        Assert.DoesNotContain("<script>", html);
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

}
