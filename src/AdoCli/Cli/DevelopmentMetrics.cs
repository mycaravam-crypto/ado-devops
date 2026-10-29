namespace AdoCli.Cli;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AdoCli.Api;

/// <summary>Area-scoped PR metrics, based only on explicitly linked work items.</summary>
public static class DevelopmentMetrics
{
    public sealed record Month(string Period, int Opened, int Closed, double? AverageDays);
    public sealed record Report(string Format, string Project, string Area, DateTimeOffset GeneratedAt,
        int Months, int MatchedPullRequests, int UnlinkedPullRequests, int Opened, int Closed,
        double? AverageDays, IReadOnlyList<Month> Timeline);

    public static Report Aggregate(string project, string area, int months, DateTimeOffset generatedAt,
        IEnumerable<PullRequest> matched, int unlinked)
    {
        var start = new DateTimeOffset(generatedAt.Year, generatedAt.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1 - months);
        var end = start.AddMonths(months);
        var prs = matched.ToList();
        var rows = new List<Month>();
        for (var month = start; month < end; month = month.AddMonths(1))
        {
            var next = month.AddMonths(1);
            var opened = prs.Count(p => p.CreationDate is { } date && date >= month && date < next);
            var closed = prs.Where(p => p.ClosedDate is { } date && date >= month && date < next).ToList();
            rows.Add(new Month(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), opened, closed.Count,
                AverageDays(closed)));
        }
        var observedClosed = prs.Where(p => p.ClosedDate is { } date && date >= start && date < end).ToList();
        return new("ado.development-metrics/v1", project, area, generatedAt, months, prs.Count, unlinked,
            rows.Sum(r => r.Opened), rows.Sum(r => r.Closed), AverageDays(observedClosed), rows);
    }

    public static double? AverageDays(IEnumerable<PullRequest> prs)
    {
        var durations = prs.Where(p => p.CreationDate.HasValue && p.ClosedDate.HasValue &&
                p.ClosedDate.Value >= p.CreationDate.Value)
            .Select(p => (p.ClosedDate!.Value - p.CreationDate!.Value).TotalDays).ToArray();
        return durations.Length == 0 ? null : Math.Round(durations.Average(), 1);
    }

    /// <summary>Only explicitly linked work items with a matching Area Path qualify a PR.</summary>
    public static async Task<(List<PullRequest> Matched, int Unlinked)> CollectAsync(
        AdoClient client, string project, string area, int months)
    {
        var all = await client.GetPullRequestsAsync(project, null, "all", null);
        var cutoff = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1,
            0, 0, 0, TimeSpan.Zero).AddMonths(1 - months);
        // Include older PRs closed during the selected period as well.
        var candidates = all.Where(p =>
            (p.CreationDate is { } created && created >= cutoff) ||
            (p.ClosedDate is { } closed && closed >= cutoff)).ToList();
        var matched = new List<PullRequest>();
        var unlinked = 0;
        foreach (var pr in candidates)
        {
            var ids = await client.GetPullRequestWorkItemIdsAsync(pr);
            if (ids.Count == 0) { unlinked++; continue; }
            var items = await client.GetWorkItemsAsync(ids.Distinct(), ["System.AreaPath"]);
            if (items.Any(w => TestPlanProgress.InsideArea(w.Field("System.AreaPath"), area)))
                matched.Add(pr);
        }
        return (matched, unlinked);
    }

    public static string Html(Report report)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
        static string V(double? value) => value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "n/a";
        var max = Math.Max(1, report.Timeline.SelectMany(m => new[] { m.Opened, m.Closed }).DefaultIfEmpty().Max());
        var sb = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <title>Development metrics</title>
            <style>
            :root{color-scheme:light dark;--canvas:#f3f5f8;--surface:#fff;--ink:#172334;--text:#344457;--muted:#5c6b7e;--line:#dce2eb;--accent:#235bbd;--accent2:#168567}
            @media(prefers-color-scheme:dark){:root{--canvas:#101620;--surface:#192331;--ink:#f1f5fb;--text:#d2ddeb;--muted:#a3b3c6;--line:#35465a;--accent:#8eb7ff;--accent2:#59b99d}}
            *{box-sizing:border-box}body{margin:0;padding:32px 18px;background:var(--canvas);color:var(--text);font:15px/1.6 system-ui,Segoe UI,sans-serif}
            main{max-width:1100px;margin:auto;background:var(--surface);border:1px solid var(--line);border-radius:14px;padding:clamp(20px,4vw,52px)}
            h1,h2{color:var(--ink);line-height:1.25}h1{font-size:32px;margin:0 0 8px}h2{font-size:21px;margin:28px 0 12px}
            .muted{color:var(--muted)}.kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px;margin:24px 0}
            .kpi{border:1px solid var(--line);border-radius:10px;padding:16px}.kpi strong{display:block;font-size:26px;color:var(--ink)}.kpi span{color:var(--muted);font-size:13px}
            table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:10px;border-bottom:1px solid var(--line);text-align:left}th{color:var(--muted);font-size:12px}
            .bars{min-width:110px;display:flex;gap:5px;align-items:center}.bar{height:9px;min-width:0;border-radius:3px}.opened{background:var(--accent)}.closed{background:var(--accent2)}
            .scroll{overflow-x:auto}.legend{display:flex;gap:18px;font-size:13px;margin:8px 0}.swatch{display:inline-block;width:10px;height:10px;margin-right:6px;border-radius:2px}
            @media print{body{padding:0;background:#fff}main{border:0;padding:0}.bar{-webkit-print-color-adjust:exact;print-color-adjust:exact}}
            </style></head><body><main>
            """);
        sb.Append("<p class=\"muted\">Azure DevOps · Development performance</p><h1>Development metrics</h1><p>")
            .Append(E(report.Project)).Append(" · Area: ").Append(E(report.Area))
            .Append("</p><p class=\"muted\">Generated (UTC): ")
            .Append(report.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(" · Last ").Append(report.Months).Append(" calendar months</p><div class=\"kpis\">");
        foreach (var (name, value) in new[] {
            ("PRs opened", report.Opened.ToString(CultureInfo.InvariantCulture)),
            ("PRs closed", report.Closed.ToString(CultureInfo.InvariantCulture)),
            ("Average duration (days)", V(report.AverageDays)),
            ("PRs in selected area", report.MatchedPullRequests.ToString(CultureInfo.InvariantCulture))
        })
            sb.Append("<div class=\"kpi\"><strong>").Append(E(value)).Append("</strong><span>").Append(E(name)).Append("</span></div>");
        sb.Append("</div><h2>Monthly pull requests</h2><div class=\"legend\"><span><i class=\"swatch opened\"></i>Opened</span><span><i class=\"swatch closed\"></i>Closed</span></div>")
            .Append("<div class=\"scroll\"><table><thead><tr><th>Month (UTC)</th><th>Opened</th><th>Closed</th><th>Average days to close</th><th>Volume</th></tr></thead><tbody>");
        foreach (var m in report.Timeline)
        {
            sb.Append("<tr><td>").Append(E(m.Period)).Append("</td><td>").Append(m.Opened)
                .Append("</td><td>").Append(m.Closed).Append("</td><td>").Append(V(m.AverageDays))
                .Append("</td><td><div class=\"bars\" aria-label=\"Opened ").Append(m.Opened)
                .Append(", closed ").Append(m.Closed).Append("\"><div class=\"bar opened\" style=\"width:")
                .Append(m.Opened * 100 / max).Append("%\"></div><div class=\"bar closed\" style=\"width:")
                .Append(m.Closed * 100 / max).Append("%\"></div></div></td></tr>");
        }
        sb.Append("</tbody></table></div><h2>Scope and definitions</h2><p class=\"muted\">")
            .Append("A PR belongs to the selected Area Path only if it is linked to at least one work item in that area or a child area. ")
            .Append("Unlinked PRs among the date-range candidates: ").Append(report.UnlinkedPullRequests).Append(". ")
            .Append("Opened and closed are separate monthly event counts, not a cohort conversion rate. ")
            .Append("Closed includes completed and abandoned PRs; average duration uses closure minus creation time and excludes records without valid dates. ")
            .Append("Missing or unlinked work items can cause undercounting. Times are UTC.</p></main></body></html>");
        return sb.ToString();
    }

    public static async Task<int> RunAsync(Context ctx)
    {
        var area = ctx.Args.Get("--area");
        if (string.IsNullOrWhiteSpace(area))
            throw AdoException.Usage("--area is required (example: ado metrics --area Product\\ALPHA)");
        var format = ctx.Args.Get("--format") ?? (ctx.Json ? "json" : "html");
        if (format is not ("html" or "json"))
            throw AdoException.Usage("--format must be html or json");
        var value = ctx.Args.Get("--months") ?? "12";
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var months) || months is < 1 or > 60)
            throw AdoException.Usage("--months must be between 1 and 60");
        var project = ctx.RequireProject();
        var (matched, unlinked) = await CollectAsync(ctx.Client, project, area, months);
        var report = Aggregate(project, area, months, DateTimeOffset.UtcNow, matched, unlinked);
        var output = format == "json" ? JsonSerializer.Serialize(report, Json.Options) + "\n" : Html(report);
        if (ctx.Args.Get("--output") is { } path)
        {
            File.WriteAllText(path, output);
            Console.Error.WriteLine($"Wrote development metrics to {path}");
        }
        else Console.Write(output);
        return 0;
    }
}
