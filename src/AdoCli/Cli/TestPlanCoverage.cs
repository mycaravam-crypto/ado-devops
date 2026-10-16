namespace AdoCli.Cli;

using System.Net;
using System.Text;
using AdoCli.Api;

/// <summary>Requirement and execution coverage for one plan and one Area Path.</summary>
public static class TestPlanCoverage
{
    public sealed record Requirement(int Id, string Title, int LinkedCases, bool Covered);
    public sealed record Report(int PlanId, string PlanName, string Area, int Requirements, int CoveredRequirements,
        int PlannedCases, int ExecutedCases, int PassedCases, IReadOnlyList<Requirement> Items)
    {
        public double RequirementCoverage => Percent(CoveredRequirements, Requirements);
        public double ExecutionCoverage => Percent(ExecutedCases, PlannedCases);
        public double PassRate => Percent(PassedCases, ExecutedCases);
    }

    public static double Percent(int part, int total) => total == 0 ? 0 : Math.Round(part * 100.0 / total, 1);

    public static int? LinkedId(string? url)
    {
        if (url is null) return null;
        var last = url.TrimEnd('/').Split('/').LastOrDefault();
        return int.TryParse(last, out var id) && id > 0 ? id : null;
    }

    public static bool IsTestLink(WorkItemRelation relation) =>
        relation.Rel.Equals("Microsoft.VSTS.Common.TestedBy-Forward", StringComparison.OrdinalIgnoreCase)
        || relation.Rel.Equals("Microsoft.VSTS.Common.TestedBy-Reverse", StringComparison.OrdinalIgnoreCase);

    /// <summary>Calculates requirement coverage and latest execution outcomes for a single test plan and Area Path, including child areas.</summary>
    public static async Task<Report> BuildAsync(AdoClient client, string project, int planId, string area)
    {
        var plan = await client.GetTestPlanAsync(project, planId);
        var suites = await client.GetTestSuitesAsync(project, planId);
        var caseIds = new HashSet<int>();
        foreach (var suite in suites)
            caseIds.UnionWith(await client.GetSuiteTestCaseIdsAsync(project, planId, suite.Id));

        // All states, including completed requirements. Do not silently omit historical coverage.
        var reqIds = await client.QueryWorkItemIdsAsync(project, new WorkItemFilter
        {
            Everyone = true, Area = area, States = ["any"],
            Types = ["User Story", "Product Backlog Item", "Requirement"]
        });
        var requirements = new List<Requirement>();
        foreach (var id in reqIds.Distinct())
        {
            var w = await client.GetWorkItemWithRelationsAsync(id);
            var linked = (w.Relations ?? [])
                .Where(IsTestLink).Select(r => LinkedId(r.Url))
                .OfType<int>().Distinct().Count(caseIds.Contains);
            requirements.Add(new(id, w.Field("System.Title"), linked, linked > 0));
        }

        var latest = new Dictionary<int, string>();
        foreach (var run in await client.GetTestRunsAsync(project, planId))
            foreach (var result in await client.GetRunResultsAsync(project, run.Id))
                if (result.TestCase?.Id is { } id && caseIds.Contains(id) && !latest.ContainsKey(id))
                    latest[id] = result.Outcome ?? "Unspecified";

        // Blocked and NotExecuted are not counted as executed.
        var executed = latest.Values.Count(v => v.Equals("Passed", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Aborted", StringComparison.OrdinalIgnoreCase));
        var passed = latest.Values.Count(v => v.Equals("Passed", StringComparison.OrdinalIgnoreCase));
        return new(planId, plan.Name, area, requirements.Count,
            requirements.Count(r => r.Covered), caseIds.Count, executed, passed, requirements);
    }

    /// <summary>Runs the read-only testplan coverage command and writes text, JSON or HTML to stdout or the requested file.</summary>
    public static async Task<int> RunAsync(Context ctx)
    {
        var id = ctx.Id("testplan coverage <id> --area <path> [--format html] [--output file]");
        var area = ctx.Args.Get("--area");
        if (string.IsNullOrWhiteSpace(area))
            throw AdoException.Usage("--area is required for software-specific coverage");
        var format = ctx.Args.Get("--format") ?? (ctx.Json ? "json" : "text");
        if (format is not ("text" or "json" or "html"))
            throw AdoException.Usage("--format must be text, json or html");
        var report = await BuildAsync(ctx.Client, ctx.RequireProject(), id, area);
        string output = format switch
        {
            "html" => Html(report),
            "json" => System.Text.Json.JsonSerializer.Serialize(report, Json.Options) + "\n",
            _ => $"Test plan #{report.PlanId}: {report.PlanName}\nArea: {report.Area}\n" +
                 $"Requirements: {report.CoveredRequirements}/{report.Requirements} ({report.RequirementCoverage}%)\n" +
                 $"Execution: {report.ExecutedCases}/{report.PlannedCases} ({report.ExecutionCoverage}%)\n" +
                 $"Passed: {report.PassedCases}/{report.ExecutedCases} ({report.PassRate}%)\n" +
                 string.Join("", report.Items.Where(r => !r.Covered).Select(r => $"Uncovered: #{r.Id} {r.Title}\n"))
        };
        if (ctx.Args.Get("--output") is { } path)
        {
            File.WriteAllText(path, output);
            Console.Error.WriteLine($"Wrote test plan coverage to {path}");
        }
        else Console.Write(output);
        return 0;
    }

    public static string Html(Report report)
    {
        static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>Test coverage</title><style>")
            .Append("body{font:15px/1.6 system-ui,sans-serif;color:#243246;background:#f6f8fb;max-width:950px;margin:40px auto;padding:0 20px}")
            .Append("h1{margin-bottom:5px}.muted{color:#64748b}.metrics{display:flex;flex-wrap:wrap;gap:16px;margin:26px 0}")
            .Append(".metric{flex:1;min-width:180px;background:white;padding:20px;border:1px solid #dce4ed;border-radius:8px}")
            .Append(".metric strong{display:block;font-size:28px}table{border-collapse:collapse;width:100%;background:white}")
            .Append("th,td{padding:10px;text-align:left;border-bottom:1px solid #dce4ed}th{background:#e9eff7}")
            .Append("@media print{body{background:white;margin:0}}")
            .Append("</style></head><body><h1>").Append(E(report.PlanName)).Append("</h1><div class=\"muted\">Plan #")
            .Append(report.PlanId).Append(" · Area: ").Append(E(report.Area)).Append("</div><div class=\"metrics\">");
        void Metric(string title, double percentage, int a, int b)
        {
            html.Append("<section class=\"metric\">").Append(E(title)).Append("<strong>")
                .Append(percentage.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                .Append("%</strong>").Append(a).Append(" / ").Append(b).Append("</section>");
        }
        Metric("Requirement coverage", report.RequirementCoverage, report.CoveredRequirements, report.Requirements);
        Metric("Execution coverage", report.ExecutionCoverage, report.ExecutedCases, report.PlannedCases);
        Metric("Pass rate", report.PassRate, report.PassedCases, report.ExecutedCases);
        html.Append("</div><h2>Uncovered requirements</h2><table><thead><tr><th>ID</th><th>Requirement</th></tr></thead><tbody>");
        foreach (var req in report.Items.Where(r => !r.Covered).OrderBy(r => r.Id))
            html.Append("<tr><td>#").Append(req.Id).Append("</td><td>").Append(E(req.Title)).Append("</td></tr>");
        html.Append("</tbody></table></body></html>");
        return html.ToString();
    }
}
