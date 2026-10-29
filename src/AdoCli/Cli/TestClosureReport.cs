namespace AdoCli.Cli;

using System.Net;
using System.Text;
using System.Text.Json;
using AdoCli.Api;

/// <summary>Read-only, point-based test closure snapshot. Does not assert release readiness.</summary>
public static class TestClosureReport
{
    public sealed record Report(string Format, string Project, int PlanId, string PlanName,
        string? Area, DateTimeOffset GeneratedAt, int Points, int Passed, int Failed,
        int Blocked, int NotRun, double ExecutionPercent, double PassPercent,
        IReadOnlyList<TestPlanProgress.SuiteProgress> Suites)
    {
        public int Executed => Passed + Failed;
        public int Open => Blocked + NotRun + Failed;
    }

    public static Report FromProgress(string project, TestPlanProgress.Report progress, DateTimeOffset generatedAt) =>
        new("ado.test-closure/v1", project, progress.PlanId, progress.PlanName, progress.Area,
            generatedAt, progress.Points, progress.Passed, progress.Failed, progress.Blocked,
            progress.NotRun, progress.ExecutionPercent, progress.PassPercent, progress.Suites);

    /// <summary>Summary is escaped and deliberately has no automatic release recommendation.</summary>
    public static string SummaryHtml(Report report)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
        var sb = new StringBuilder();
        sb.Append("<section id=\"closure\"><h2>Test closure summary</h2><p class=\"muted\">Project: ")
            .Append(E(report.Project)).Append(" · Area: ").Append(E(report.Area ?? "all"))
            .Append(" · Generated (UTC): ")
            .Append(report.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))
            .Append("</p><div class=\"meta\">");
        foreach (var (name, value) in new (string, string)[]
        {
            ("Test points", report.Points.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Passed", report.Passed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Failed", report.Failed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Blocked", report.Blocked.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Not run", report.NotRun.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Executed", report.ExecutionPercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%"),
            ("Pass rate", report.PassPercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%")
        })
            sb.Append("<span>").Append(name).Append(": ").Append(E(value)).Append("</span>");
        sb.Append("</div><p>Outstanding results (failed, blocked or not run): ")
            .Append(report.Open).Append(".</p>")
            .Append("<p class=\"muted\">Results are current test-point outcomes (suite × case × configuration). ")
            .Append("Executed means Passed + Failed; no-run and blocked points are not executed. ")
            .Append("This is a factual snapshot, not a release approval or a historical end-of-period snapshot.</p>")
            .Append("</section>");
        return sb.ToString();
    }

    public static async Task<int> RunAsync(Context ctx)
    {
        var id = ctx.Id("testplan report <id> [--area path] [--format html|json] [--output file]");
        var format = ctx.Args.Get("--format") ?? (ctx.Json ? "json" : "html");
        if (format is not ("html" or "json"))
            throw AdoException.Usage("--format must be html or json");
        var project = ctx.RequireProject();
        var progress = await TestPlanProgress.BuildAsync(ctx.Client, project, id, ctx.Args.Get("--area"));
        var report = FromProgress(project, progress, DateTimeOffset.UtcNow);
        string output;
        if (format == "json")
        {
            output = JsonSerializer.Serialize(report, Json.Options) + "\n";
        }
        else
        {
            var file = await TestPlanCommands.ExportAsync(ctx.Client, project, id);
            // Reuse the established offline HTML shell. Parameter values are excluded for safer sharing.
            var (shared, errors) = await TestPlanCommands.ResolveSharedStepsAsync(ctx.Client, file);
            var html = TestPlanHtml.Render(file, shared, errors, progress, excludeParameters: true);
            output = html.Replace("<title>Test plan report</title>", "<title>Test closure report</title>", StringComparison.Ordinal)
                .Replace("<main><div class=\"eyebrow\">", "<main>" + SummaryHtml(report) + "<div class=\"eyebrow\">", StringComparison.Ordinal);
        }
        if (ctx.Args.Get("--output") is { } path)
        {
            File.WriteAllText(path, output);
            Console.Error.WriteLine($"Wrote test closure report to {path}");
        }
        else Console.Write(output);
        return 0;
    }
}
