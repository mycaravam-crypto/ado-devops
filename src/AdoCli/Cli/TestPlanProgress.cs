namespace AdoCli.Cli;

using System.Net;
using System.Text;
using System.Text.Json;
using AdoCli.Api;

/// <summary>Point-based test plan progress with optional software Area Path filtering and run-history trend.</summary>
public static class TestPlanProgress
{
    /// <summary>The current point outcome distribution for one suite.</summary>
    public sealed record SuiteProgress(int Id, string Name, int Total, int Passed, int Failed, int Blocked, int NotRun);
    /// <summary>Execution outcomes observed on a UTC calendar date (historical results, not daily point snapshots).</summary>
    public sealed record DailyResults(DateOnly Day, int Passed, int Failed, int Other);
    /// <summary>The current test-point state and separate run-based execution history.</summary>
    public sealed record Report(int PlanId, string PlanName, string? Area, int Days, int Points,
        int Passed, int Failed, int Blocked, int NotRun, IReadOnlyList<SuiteProgress> Suites,
        IReadOnlyList<DailyResults> Trend)
    {
        /// <summary>Share of current test points with a result of Passed or Failed.</summary>
        public double ExecutionPercent => TestPlanCoverage.Percent(Passed + Failed, Points);
        /// <summary>Share of executed points that currently passed.</summary>
        public double PassPercent => TestPlanCoverage.Percent(Passed, Passed + Failed);
    }

    /// <summary>Normalizes legacy server outcomes; never counts a missing result as passed.</summary>
    public static string Outcome(string? value) => value?.ToLowerInvariant() switch
    {
        "passed" => "Passed",
        "failed" => "Failed",
        "blocked" => "Blocked",
        _ => "NotRun"
    };

    /// <summary>Builds a current point-based report and a separate 14/30-day test-run history.</summary>
    public static async Task<Report> BuildAsync(AdoClient client, string project, int planId, string? area = null, int days = 14)
    {
        if (days is not (14 or 30)) throw AdoException.Usage("--days must be 14 or 30");
        var plan = await client.GetTestPlanAsync(project, planId);
        var suites = await client.GetTestSuitesAsync(project, planId);
        var pointSets = new List<(TestSuite Suite, List<TestPointInfo> Points)>();
        foreach (var suite in suites)
            pointSets.Add((suite, await client.GetTestPointsAsync(project, planId, suite.Id)));
        HashSet<int>? selectedCases = null;
        if (!string.IsNullOrWhiteSpace(area))
        {
            var ids = pointSets.SelectMany(p => p.Points).Where(p => p.TestCase is not null)
                .Select(p => CaseId(p)).Where(id => id > 0).Distinct().ToArray();
            var items = await client.GetWorkItemsAsync(ids, ["System.AreaPath"]);
            selectedCases = items.Where(w => InsideArea(w.Field("System.AreaPath"), area))
                .Select(w => w.Id).ToHashSet();
        }
        var relevant = pointSets.Select(p => (p.Suite,
            Points: p.Points.Where(x => x.TestCase is not null
                && (selectedCases is null || selectedCases.Contains(CaseId(x)))).ToList())).ToList();
        var entries = relevant.SelectMany(p => p.Points.Select(x => (p.Suite.Id, Point: x)))
            .DistinctBy(x => (x.Id, x.Point.Id)).ToList();
        int Count(string outcome) => entries.Count(p => Outcome(p.Point.Outcome) == outcome);
        var bySuite = relevant.Select(p => new SuiteProgress(p.Suite.Id, p.Suite.Name, p.Points.Count,
            p.Points.Count(x => Outcome(x.Outcome) == "Passed"),
            p.Points.Count(x => Outcome(x.Outcome) == "Failed"),
            p.Points.Count(x => Outcome(x.Outcome) == "Blocked"),
            p.Points.Count(x => Outcome(x.Outcome) == "NotRun"))).ToList();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var trend = new Dictionary<DateOnly, (int Passed, int Failed, int Other)>();
        var validCases = entries.Select(e => CaseId(e.Point)).ToHashSet();
        foreach (var run in await client.GetTestRunsAsync(project, planId))
        {
            var date = run.CompletedDate ?? run.StartedDate;
            if (date is null || date < cutoff) continue;
            var day = DateOnly.FromDateTime(date.Value.UtcDateTime);
            foreach (var result in await client.GetRunResultsAsync(project, run.Id))
            {
                if (result.TestCase?.Id is not { } id || !validCases.Contains(id)) continue;
                var counts = trend.GetValueOrDefault(day);
                var outcome = Outcome(result.Outcome);
                trend[day] = outcome switch
                {
                    "Passed" => (counts.Passed + 1, counts.Failed, counts.Other),
                    "Failed" => (counts.Passed, counts.Failed + 1, counts.Other),
                    _ => (counts.Passed, counts.Failed, counts.Other + 1),
                };
            }
        }
        return new(planId, plan.Name, area, days, entries.Count, Count("Passed"), Count("Failed"),
            Count("Blocked"), Count("NotRun"), bySuite,
            trend.OrderBy(x => x.Key).Select(x => new DailyResults(x.Key, x.Value.Passed, x.Value.Failed, x.Value.Other)).ToList());
    }

    /// <summary>Reads a test case ID from the legacy test point response safely.</summary>
    public static int CaseId(TestPointInfo point) => int.TryParse(point.TestCase?.Id, out var id) ? id : 0;

    /// <summary>Matches an exact Area Path or one of its descendants, without matching similarly prefixed areas.</summary>
    public static bool InsideArea(string path, string area) =>
        path.Equals(area, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(area.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Renders HTML content to embed in a standalone DocWizz-style report.</summary>
    public static string Html(Report r, bool includeSummary = true)
    {
        static string E(string? v) => WebUtility.HtmlEncode(v ?? "");
        var sb = new StringBuilder();
        sb.Append(includeSummary ? "<section id=\"progress\"><h2>Test progress</h2>" : "<section id=\"progress\"><h2>Test execution details</h2>");
        if (includeSummary)
        {
                sb.Append("<p class=\"muted\">Generated (UTC): ")
                .Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append(" · Source: legacy Test API 5.0 · ");
            sb.Append("Point-based status (suite × case × configuration); execution history based on test runs. ");
            sb.Append("Area: ").Append(E(r.Area ?? "all")).Append("</p>");
            sb.Append("<div class=\"meta\"><span>Test points: ").Append(r.Points)
                .Append("</span><span>Passed: ").Append(r.Passed).Append("</span><span>Failed: ")
                .Append(r.Failed).Append("</span><span>Blocked: ").Append(r.Blocked)
                .Append("</span><span>Not run: ").Append(r.NotRun)
                .Append("</span><span>Executed: ").Append(r.ExecutionPercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                .Append("%</span><span>Pass rate: ").Append(r.PassPercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                .Append("%</span></div>");
            }
        sb.Append("<h3>By suite</h3><div class=\"table-wrap\"><table><thead><tr><th>Suite</th><th>Total</th><th>Passed</th><th>Failed</th><th>Blocked</th><th>Not run</th></tr></thead><tbody>");
        foreach (var suite in r.Suites)
            sb.Append("<tr><td>").Append(E(suite.Name)).Append("</td><td>").Append(suite.Total)
                .Append("</td><td>").Append(suite.Passed).Append("</td><td>").Append(suite.Failed)
                .Append("</td><td>").Append(suite.Blocked).Append("</td><td>").Append(suite.NotRun).Append("</td></tr>");
        sb.Append("</tbody></table></div><h3>Execution history (last ").Append(r.Days)
            .Append(" days)</h3><p class=\"muted\">Daily test result counts, not historical point-status snapshots.</p>");
        sb.Append("<div class=\"table-wrap\"><table><thead><tr><th>Day (UTC)</th><th>Passed</th><th>Failed</th><th>Other</th></tr></thead><tbody>");
        foreach (var day in r.Trend)
            sb.Append("<tr><td>").Append(day.Day).Append("</td><td>").Append(day.Passed)
                .Append("</td><td>").Append(day.Failed).Append("</td><td>").Append(day.Other).Append("</td></tr>");
        return sb.Append("</tbody></table></div></section>").ToString();
    }

    /// <summary>Reuses the established DocWizz-style report shell (sidebar, responsive navigation, dark mode).</summary>
    public static async Task<string> RenderStandaloneHtmlAsync(AdoClient client, string project, int planId, Report report)
    {
        var file = await TestPlanCommands.ExportAsync(client, project, planId);
        var (shared, errors) = await TestPlanCommands.ResolveSharedStepsAsync(client, file);
        return TestPlanHtml.Render(file, shared, errors, report);
    }

    /// <summary>Runs the testplan progress CLI and optionally writes an offline HTML report.</summary>
    public static async Task<int> RunAsync(Context ctx)
    {
        var id = ctx.Id("testplan progress <id> [--area path] [--days 14|30] [--format text|json|html]");
        var daysText = ctx.Args.Get("--days") ?? "14";
        if (!int.TryParse(daysText, out var days) || days is not (14 or 30))
            throw AdoException.Usage("--days must be 14 or 30");
        var format = ctx.Args.Get("--format") ?? (ctx.Json ? "json" : "text");
        if (format is not ("text" or "json" or "html"))
            throw AdoException.Usage("--format must be text, json or html");
        var report = await BuildAsync(ctx.Client, ctx.RequireProject(), id, ctx.Args.Get("--area"), days);
        var output = format switch
        {
            "json" => JsonSerializer.Serialize(report, Json.Options) + "\n",
            "html" => await RenderStandaloneHtmlAsync(ctx.Client, ctx.RequireProject(), id, report),
            _ => $"Test plan #{report.PlanId}: {report.PlanName}\nArea: {report.Area ?? "all"}\n" +
                $"Points: {report.Points} | Passed: {report.Passed} | Failed: {report.Failed} | Blocked: {report.Blocked} | Not Run: {report.NotRun}\n" +
                $"Executed: {report.ExecutionPercent}% | Pass rate: {report.PassPercent}%\n"
        };
        if (ctx.Args.Get("--output") is { } path) File.WriteAllText(path, output);
        else Console.Write(output);
        return 0;
    }
}
