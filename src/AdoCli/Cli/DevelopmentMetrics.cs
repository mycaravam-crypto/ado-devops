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
    public sealed record TestMetrics(int UserStories, int UserStoriesWithoutTests, int LinkedUserStories,
        int TestCases, int TestPlans, int UnassignedTestCases);
    public sealed record PrDetail(int PrId, string Title, string CreatedBy, string SourceBranch, string TargetBranch, int? Commits, string ApprovedBy = "", string ClosedBy = "");
    public sealed record Report(string Format, string Project, string Area, DateTimeOffset GeneratedAt,
        int Months, int MatchedPullRequests, int UnlinkedPullRequests, int Opened, int Closed,
        double? AverageDays, IReadOnlyList<Month> Timeline,
        int Merged = 0, int Abandoned = 0, int OpenAtEnd = 0, double? MedianDays = null,
        int Commits = 0, double? AverageCommitsPerPr = null, int CommitCountUnavailable = 0,
        IReadOnlyList<PrDetail>? PullRequests = null, TestMetrics? Tests = null);

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
            rows.Sum(r => r.Opened), rows.Sum(r => r.Closed), AverageDays(observedClosed), rows,
            observedClosed.Count(p => p.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)),
            observedClosed.Count(p => p.Status.Equals("abandoned", StringComparison.OrdinalIgnoreCase)),
            prs.Count(p => p.CreationDate is { } created && created < end &&
                (p.ClosedDate is null || p.ClosedDate >= end)), MedianDays(observedClosed));
    }

    /// <summary>Area-scoped test inventory at report generation time; test plans count when they contain at least one test case from the area.</summary>
    public static async Task<TestMetrics> CollectTestMetricsAsync(AdoClient client, string project, string area, Action<string>? progress = null)
    {
        area = NormalizeAreaPath(area);
        var storyIds = (await client.QueryWorkItemIdsAsync(project, new WorkItemFilter
        {
            Everyone = true, Area = area, States = ["any"], Types = ["User Story"]
        })).Distinct().ToList();
        // Test Cases commonly live in a separate QA area. Count tested-by links from
        // stories in the selected area regardless of the Test Case's own Area Path.
        var areaCaseIds = (await client.QueryWorkItemIdsAsync(project, new WorkItemFilter
        {
            Everyone = true, Area = area, States = ["any"], Types = ["Test Case"]
        })).ToHashSet();
        var linkedCaseIds = new HashSet<int>();
        var uncovered = 0;
        foreach (var id in storyIds)
        {
            var story = await client.GetWorkItemWithRelationsAsync(id);
            var linked = (story.Relations ?? [])
                .Where(TestPlanCoverage.IsTestLink)
                .Select(r => TestPlanCoverage.LinkedId(r.Url))
                .OfType<int>().ToHashSet();
            if (linked.Count == 0) uncovered++;
            linkedCaseIds.UnionWith(linked);
        }
        var testIds = areaCaseIds.Union(linkedCaseIds).ToHashSet();
        progress?.Invoke($"Found {storyIds.Count} user stories, {areaCaseIds.Count} test cases inside area, " +
            $"{linkedCaseIds.Count} test cases linked from stories (including other areas), " +
            $"{uncovered} stories without Tested By links.");
        var plans = await client.GetTestPlansAsync(project);
        var matchingPlans = 0;
        var assignedCases = new HashSet<int>();
        foreach (var plan in plans)
        {
            var hasCases = false;
            foreach (var suite in await client.GetTestSuitesAsync(project, plan.Id))
            {
                var ids = await client.GetSuiteTestCaseIdsAsync(project, plan.Id, suite.Id);
                foreach (var id in ids)
                    if (testIds.Contains(id))
                    {
                        assignedCases.Add(id);
                        hasCases = true;
                    }
            }
            if (hasCases) matchingPlans++;
        }
        progress?.Invoke($"Checked {plans.Count} plans; {matchingPlans} include test cases from the area.");
        return new(storyIds.Count, uncovered, storyIds.Count - uncovered,
            testIds.Count, matchingPlans, testIds.Count - assignedCases.Count);
    }

    /// <summary>Attach PR inventory and total commit count; unavailable counts are excluded from the average.</summary>
    public static Report WithPullRequests(Report report, IReadOnlyList<PrDetail> details)
    {
        var available = details.Where(p => p.Commits.HasValue).ToList();
        var total = available.Sum(p => p.Commits!.Value);
        return report with
        {
            PullRequests = details,
            Commits = total,
            AverageCommitsPerPr = available.Count == 0 ? null :
                Math.Round((double)total / available.Count, 1),
            CommitCountUnavailable = details.Count - available.Count
        };
    }

    public static PrDetail Detail(PullRequest pr, int? commits) =>
        new(pr.PullRequestId, pr.Title, pr.CreatedBy.DisplayName, pr.SourceRefName, pr.TargetRefName, commits,
            string.Join(", ", (pr.Reviewers ?? []).Where(r => r.Vote is 10 or 5)
                .Select(r => r.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase)),
            pr.ClosedBy?.DisplayName ?? "");

    public static double? AverageDays(IEnumerable<PullRequest> prs)
    {
        var durations = prs.Where(p => p.CreationDate.HasValue && p.ClosedDate.HasValue &&
                p.ClosedDate.Value >= p.CreationDate.Value)
            .Select(p => (p.ClosedDate!.Value - p.CreationDate!.Value).TotalDays).ToArray();
        return durations.Length == 0 ? null : Math.Round(durations.Average(), 1);
    }

    /// <summary>Median elapsed days for closed PRs with valid dates.</summary>
    public static double? MedianDays(IEnumerable<PullRequest> prs)
    {
        var values = prs.Where(p => p.CreationDate.HasValue && p.ClosedDate.HasValue &&
                p.ClosedDate.Value >= p.CreationDate.Value)
            .Select(p => (p.ClosedDate!.Value - p.CreationDate!.Value).TotalDays)
            .Order().ToArray();
        if (values.Length == 0) return null;
        var median = values.Length % 2 == 1 ? values[values.Length / 2] :
            (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2;
        return Math.Round(median, 1);
    }

    /// <summary>Include PR activity in the window and old active PRs in the outstanding backlog.</summary>
    public static bool IsCandidate(PullRequest pr, DateTimeOffset cutoff) =>
        (pr.CreationDate is { } created && created >= cutoff) ||
        (pr.ClosedDate is { } closed && closed >= cutoff) ||
        (pr.ClosedDate is null && pr.CreationDate is { } older && older < cutoff &&
            pr.Status.Equals("active", StringComparison.OrdinalIgnoreCase));

    /// <summary>Azure DevOps Area Paths use backslashes; accept slash-separated CLI input.</summary>
    public static string NormalizeAreaPath(string area) =>
        area.Trim().Replace('/', '\\').TrimEnd('\\');

    /// <summary>Only explicitly linked work items with a matching Area Path qualify a PR; optional callback receives processed, total, matched and unlinked counts.</summary>
    public static async Task<(List<PullRequest> Matched, int Unlinked)> CollectAsync(
        AdoClient client, string project, string area, int months, Action<int, int, int, int>? progress = null)
    {
        area = NormalizeAreaPath(area);
        var all = await client.GetPullRequestsAsync(project, null, "all", null);
        var cutoff = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1,
            0, 0, 0, TimeSpan.Zero).AddMonths(1 - months);
        // Include older PRs closed during the selected period as well.
        var candidates = all.Where(p => IsCandidate(p, cutoff)).ToList();
        var matched = new List<PullRequest>();
        var unlinked = 0;
        var processed = 0;
        foreach (var pr in candidates)
        {
            var ids = await client.GetPullRequestWorkItemIdsAsync(pr);
            if (ids.Count == 0) unlinked++;
            else
            {
                var items = await client.GetWorkItemsAsync(ids.Distinct(), ["System.AreaPath"]);
                if (items.Any(w => TestPlanProgress.InsideArea(w.Field("System.AreaPath"), area)))
                    matched.Add(pr);
            }
            progress?.Invoke(++processed, candidates.Count, matched.Count, unlinked);
        }
        if (candidates.Count == 0) progress?.Invoke(0, 0, 0, 0);
        return (matched, unlinked);
    }

    /// <summary>Report why Area Path matching did not find linked PRs.</summary>
    public static string NoMatchesMessage(string area, int unlinked) =>
        $"No pull requests were matched to Area Path '{area}'. " +
        $"{unlinked} candidate PR(s) have no linked work items; the other candidates have links but none match the specified area. " +
        "Azure DevOps PRs have no Area Path of their own. Link PRs to work items in this area " +
        "or use --scope repository --repo <name> for repository-wide metrics (not Area Path metrics).";

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
            .Append(" · Last ").Append(report.Months).Append(" calendar months</p>");
        sb.Append(TopLineChart(report));
        sb.Append("<div class=\"kpis\">");
        foreach (var (name, value) in new[] {
            ("PRs opened", report.Opened.ToString(CultureInfo.InvariantCulture)),
            ("PRs closed", report.Closed.ToString(CultureInfo.InvariantCulture)),
            ("Average duration (days)", V(report.AverageDays)),
            ("Median duration (days)", V(report.MedianDays)),
            ("Merged PRs", report.Merged.ToString(CultureInfo.InvariantCulture)),
            ("Abandoned PRs", report.Abandoned.ToString(CultureInfo.InvariantCulture)),
            ("Open at period end", report.OpenAtEnd.ToString(CultureInfo.InvariantCulture)),
            ("PRs in selected area", report.MatchedPullRequests.ToString(CultureInfo.InvariantCulture)),
            ("Commits", report.Commits.ToString(CultureInfo.InvariantCulture)),
            ("Avg commits/PR", V(report.AverageCommitsPerPr))
        })
            sb.Append("<div class=\"kpi\"><strong>").Append(E(value)).Append("</strong><span>").Append(E(name)).Append("</span></div>");
        sb.Append("</div>");
        if (report.Tests is { } tests)
        {
            sb.Append("<h2>Tests &amp; coverage</h2><p class=\"muted\">Area Path snapshot at report generation time; test links and plan assignments are not limited to the PR date range.</p><div class=\"kpis\">");
            foreach (var (name, value) in new[] {
                ("User stories", tests.UserStories),
                ("Stories without test cases", tests.UserStoriesWithoutTests),
                ("Stories with test cases", tests.LinkedUserStories),
                ("Test cases", tests.TestCases),
                ("Test plans", tests.TestPlans),
                ("Test cases outside plans", tests.UnassignedTestCases)
            })
                sb.Append("<div class=\"kpi\"><strong>").Append(value).Append("</strong><span>").Append(E(name)).Append("</span></div>");
            sb.Append("</div>");
        }
        sb.Append("<h2>Monthly pull requests</h2><div class=\"legend\"><span><i class=\"swatch opened\"></i>Opened</span><span><i class=\"swatch closed\"></i>Closed</span></div>")
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
        sb.Append("</tbody></table></div>").Append(InteractiveChart(report)).Append("<h2>Scope and definitions</h2><p class=\"muted\">")
            .Append("A PR belongs to the selected Area Path only if it is linked to at least one work item in that area or a child area. ")
            .Append("Unlinked PRs among the date-range candidates: ").Append(report.UnlinkedPullRequests).Append(". ")
            .Append("Opened and closed are separate monthly event counts, not a cohort conversion rate. ")
            .Append("Closed includes completed and abandoned PRs; average duration uses closure minus creation time and excludes records without valid dates. ")
            .Append("Missing or unlinked work items can cause undercounting. Times are UTC.</p>");
        sb.Append(PrInventoryHtml(report));
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    /// <summary>Escaped, complete inventory of the PRs included in the selected scope and period.</summary>
    public static string PrInventoryHtml(Report report)
    {
        static string E(string? v) => WebUtility.HtmlEncode(v ?? "");
        var sb = new StringBuilder();
        sb.Append("<section id=\"pr-inventory\"><h2>Pull requests in scope</h2>")
            .Append("<p class=\"muted\">Includes matching PRs opened or closed during the period, plus older PRs still active. ")
            .Append("Commit counts represent commits associated with each PR; missing counts are excluded from the average. ")
            .Append("Unavailable commit counts: ").Append(report.CommitCountUnavailable).Append(".</p>")
            .Append("""
                <div class="pr-table-tools">
                  <label for="pr-table-filter">Filter table</label>
                  <input id="pr-table-filter" type="search" placeholder="Search PRs…" autocomplete="off">
                  <span id="pr-table-count" aria-live="polite"></span>
                  <label>Rows <select id="pr-table-size"><option>25</option><option>50</option><option>100</option></select></label>
                  <button type="button" id="pr-table-prev">Previous</button>
                  <span id="pr-table-page"></span>
                  <button type="button" id="pr-table-next">Next</button>
                </div>
                """)
            .Append("<div class=\"scroll\"><table id=\"pr-table\"><thead><tr><th>PR ID</th><th>Title</th><th>Created by</th>")
            .Append("<th>Source branch</th><th>Target branch</th><th>Approved by</th><th>Closed by</th></tr></thead><tbody>");
        foreach (var pr in report.PullRequests ?? [])
            sb.Append("<tr><td>").Append(pr.PrId).Append("</td><td>").Append(E(pr.Title))
                .Append("</td><td>").Append(E(pr.CreatedBy)).Append("</td><td>")
                .Append(E(pr.SourceBranch)).Append("</td><td>").Append(E(pr.TargetBranch))
                .Append("</td><td>").Append(E(string.IsNullOrEmpty(pr.ApprovedBy) ? "—" : pr.ApprovedBy))
                .Append("</td><td>").Append(E(string.IsNullOrEmpty(pr.ClosedBy) ? "—" : pr.ClosedBy))
                .Append("</td></tr>");
        return sb.Append("""
            </tbody></table></div></section>
            <style>
            .pr-table-tools{display:flex;flex-wrap:wrap;align-items:center;gap:10px;margin:16px 0;font-size:13px}
            .pr-table-tools input{flex:1 1 180px;min-width:120px;padding:7px;border:1px solid var(--line);background:var(--surface);color:var(--text);border-radius:7px}
            .pr-table-tools select,.pr-table-tools button{font:inherit;padding:6px 9px;border:1px solid var(--line);border-radius:6px;background:var(--surface);color:var(--text)}
            .pr-table-tools button:not(:disabled){cursor:pointer}
            .pr-table-tools button:disabled{opacity:.45}
            #pr-table th button{font:inherit;font-weight:600;color:inherit;border:0;background:none;cursor:pointer;text-align:left;padding:0}
            #pr-table th button:focus-visible,.pr-table-tools :focus-visible{outline:2px solid var(--accent);outline-offset:2px}
            #pr-table tbody tr[hidden]{display:none}
            </style>
            <script>
            (() => {
              const table = document.getElementById('pr-table');
              if (!table) return;
              const rows = [...table.tBodies[0].rows];
              const filter = document.getElementById('pr-table-filter');
              const size = document.getElementById('pr-table-size');
              const count = document.getElementById('pr-table-count');
              const previous = document.getElementById('pr-table-prev');
              const next = document.getElementById('pr-table-next');
              const pageLabel = document.getElementById('pr-table-page');
              let page = 0, sortColumn = -1, descending = false;
              const headers = [...table.tHead.rows[0].cells];
              headers.forEach((th, column) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.textContent = th.textContent;
                th.replaceChildren(button);
                button.addEventListener('click', () => {
                  descending = sortColumn === column ? !descending : false;
                  sortColumn = column; page = 0; update();
                });
              });
              function update() {
                const query = filter.value.trim().toLowerCase();
                const filtered = rows.filter(row => row.textContent.toLowerCase().includes(query));
                if (sortColumn >= 0) {
                  filtered.sort((a, b) => {
                    const left = a.cells[sortColumn].textContent.trim();
                    const right = b.cells[sortColumn].textContent.trim();
                    const result = sortColumn === 0
                      ? Number(left) - Number(right)
                      : left.localeCompare(right, undefined, {numeric: true, sensitivity: 'base'});
                    return descending ? -result : result;
                  });
                }
                const perPage = Number(size.value);
                const pages = Math.max(1, Math.ceil(filtered.length / perPage));
                page = Math.min(page, pages - 1);
                rows.forEach(row => { row.hidden = true; });
                const visible = filtered.slice(page * perPage, (page + 1) * perPage);
                visible.forEach(row => { row.hidden = false; table.tBodies[0].appendChild(row); });
                count.textContent = filtered.length + ' / ' + rows.length + ' PRs';
                pageLabel.textContent = (page + 1) + ' / ' + pages;
                previous.disabled = page === 0;
                next.disabled = page >= pages - 1;
                headers.forEach((th, i) => th.setAttribute('aria-sort',
                  i === sortColumn ? (descending ? 'descending' : 'ascending') : 'none'));
              }
              filter.addEventListener('input', () => { page = 0; update(); });
              size.addEventListener('change', () => { page = 0; update(); });
              previous.addEventListener('click', () => { if (page > 0) { page--; update(); } });
              next.addEventListener('click', () => { page++; update(); });
              update();
            })();
            </script>
            """).ToString();
    }

    /// <summary>Offline SVG trend above KPIs; switches units instead of mixing counts and days.</summary>
    public static string TopLineChart(Report report)
    {
        static string Points(IReadOnlyList<Month> months, Func<Month, double?> selector)
        {
            var values = months.Select(selector).ToArray();
            var maximum = Math.Max(1, values.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty().Max());
            var span = Math.Max(1, values.Length - 1);
            return string.Join(" ", values.Select((v, i) => v is null ? null :
                $"{(40 + i * 680.0 / span).ToString("0.##", CultureInfo.InvariantCulture)},{(170 - v.Value * 135 / maximum).ToString("0.##", CultureInfo.InvariantCulture)}")
                .Where(v => v is not null));
        }
        var sb = new StringBuilder("""
            <section class="top-trend" aria-labelledby="top-trend-heading">
            <div class="top-trend-heading"><h2 id="top-trend-heading">KPI development</h2>
            <div class="top-trend-controls" role="group" aria-label="Choose trend KPI">
            <button type="button" class="trend-choice" data-series="opened" aria-pressed="true">Opened</button>
            <button type="button" class="trend-choice" data-series="closed" aria-pressed="false">Closed</button>
            <button type="button" class="trend-choice" data-series="duration" aria-pressed="false">Average days</button>
            </div></div>
            <svg class="top-trend-plot" viewBox="0 0 760 210" role="img" aria-label="Monthly KPI trend; detailed values in the table below">
            <line x1="40" y1="170" x2="720" y2="170" stroke="currentColor" opacity=".28"/>
            <line x1="40" y1="102.5" x2="720" y2="102.5" stroke="currentColor" opacity=".12"/>
            <line x1="40" y1="35" x2="720" y2="35" stroke="currentColor" opacity=".12"/>
            """);
        foreach (var (key, selector, color) in new (string Key, Func<Month, double?> Selector, string Color)[]
        {
            ("opened", m => m.Opened, "var(--accent)"),
            ("closed", m => m.Closed, "var(--accent2)"),
            ("duration", m => m.AverageDays, "var(--accent)")
        })
        {
            sb.Append("<g class=\"trend-series\" data-series=\"").Append(key).Append("\"");
            if (key != "opened") sb.Append(" hidden");
            sb.Append("><polyline fill=\"none\" stroke=\"").Append(color)
                .Append("\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\" points=\"")
                .Append(Points(report.Timeline, selector)).Append("\"/>");
            var maximum = Math.Max(1, report.Timeline.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty().Max());
            var span = Math.Max(1, report.Timeline.Count - 1);
            for (var i = 0; i < report.Timeline.Count; i++)
            {
                var month = report.Timeline[i];
                var value = selector(month);
                if (!value.HasValue) continue;
                var x = (40 + i * 680.0 / span).ToString("0.##", CultureInfo.InvariantCulture);
                var y = (170 - value.Value * 135 / maximum).ToString("0.##", CultureInfo.InvariantCulture);
                sb.Append("<circle cx=\"").Append(x).Append("\" cy=\"").Append(y)
                    .Append("\" r=\"4\" fill=\"").Append(color).Append("\"><title>")
                    .Append(month.Period).Append(": ").Append(value.Value.ToString("0.#", CultureInfo.InvariantCulture))
                    .Append(key == "duration" ? " days" : " PRs").Append("</title></circle>");
            }
            sb.Append("</g>");
        }
        var labelSpan = Math.Max(1, report.Timeline.Count - 1);
        for (var i = 0; i < report.Timeline.Count; i++)
        {
            var month = report.Timeline[i];
            var date = DateTime.ParseExact(month.Period, "yyyy-MM", CultureInfo.InvariantCulture);
            var label = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" }[date.Month - 1];
            if (date.Month == 1 || i == 0) label += " " + date.Year.ToString(CultureInfo.InvariantCulture);
            var x = (40 + i * 680.0 / labelSpan).ToString("0.##", CultureInfo.InvariantCulture);
            sb.Append("<text x=\"").Append(x).Append("\" y=\"200\" font-size=\"11\" text-anchor=\"middle\" fill=\"currentColor\">")
                .Append(label).Append("</text>");
        }
        sb.Append("""
            </svg><p class="muted" id="top-trend-status" aria-live="polite">Opened PRs · monthly values</p><p class="muted">Monthly UTC trend · select a metric to compare months. Exact values are listed below.</p>
            </section>
            <style>
            .top-trend{margin:24px 0;padding:20px;border:1px solid var(--line);border-radius:10px}
            .top-trend-heading{display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap}
            .top-trend-heading h2{margin:0}.top-trend-controls{display:flex;gap:6px;flex-wrap:wrap}
            .trend-choice{font:inherit;font-size:13px;cursor:pointer;border:1px solid var(--line);background:transparent;color:var(--text);border-radius:6px;padding:6px 10px}
            .trend-choice[aria-pressed="true"]{border-color:var(--accent);color:var(--accent);font-weight:700}
            .trend-choice:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
            .top-trend-plot{display:block;width:100%;height:auto;max-height:310px;margin-top:16px;overflow:visible}
            .trend-series[hidden]{display:none}
            </style>
            <script>
            (() => {
              const section = document.querySelector('.top-trend');
              if (!section) return;
              const buttons = [...section.querySelectorAll('.trend-choice')];
              const groups = [...section.querySelectorAll('.trend-series')];
              buttons.forEach(button => button.addEventListener('click', () => {
                const selected = button.dataset.series;
                buttons.forEach(item => item.setAttribute('aria-pressed', String(item === button)));
                groups.forEach(group => {
                  if (group.dataset.series === selected) group.removeAttribute("hidden");
                  else group.setAttribute("hidden", "");
                });
                const current = groups.find(group => group.dataset.series === selected);
                const labels = {opened: 'Opened PRs', closed: 'Closed PRs', duration: 'Average days to close'};
                const status = section.querySelector('#top-trend-status');
                if (status) status.textContent = (labels[selected] || selected) +
                  (current && current.querySelectorAll('circle').length > 0
                    ? ' · monthly values' : ' · no data for this period');
              }));
            })();
            </script>
            """);
        return sb.ToString();
    }

    /// <summary>Offline interactive chart with keyboard-accessible metric selection.</summary>
    public static string InteractiveChart(Report report)
    {
        var sb = new StringBuilder();
        sb.Append("<section aria-label=\"Monthly KPI chart\"><h2>Interactive monthly trend</h2>")
            .Append("<p class=\"muted\">Choose a metric; hover over a bar or select a month to inspect values.</p>")
            .Append("<div class=\"metric-controls\"><button type=\"button\" class=\"metric-choice\" data-metric=\"opened\" aria-pressed=\"true\">Opened</button>")
            .Append("<button type=\"button\" class=\"metric-choice\" data-metric=\"closed\" aria-pressed=\"false\">Closed</button>")
            .Append("<button type=\"button\" class=\"metric-choice\" data-metric=\"duration\" aria-pressed=\"false\">Average days to close</button></div>")
            .Append("<div id=\"metric-chart\" class=\"metric-chart\" role=\"group\" aria-label=\"Monthly metric values\">");
        foreach (var month in report.Timeline)
        {
            sb.Append("<button type=\"button\" class=\"metric-month\" data-period=\"")
                .Append(month.Period).Append("\" data-opened=\"").Append(month.Opened)
                .Append("\" data-closed=\"").Append(month.Closed).Append("\" data-duration=\"")
                .Append(month.AverageDays?.ToString("0.#", CultureInfo.InvariantCulture) ?? "")
                .Append("\"><span class=\"metric-amount\"></span><span class=\"metric-column\"><span class=\"metric-fill\"></span></span>")
                .Append("<span class=\"metric-period\">").Append(month.Period).Append("</span></button>");
        }
        sb.Append("</div><p id=\"metric-selection\" class=\"muted\" aria-live=\"polite\">Select a month for details.</p></section>");
        sb.Append("""
            <style>
              .metric-controls{display:flex;gap:8px;flex-wrap:wrap;margin:16px 0}
              .metric-choice{font:inherit;color:var(--text);background:var(--surface);border:1px solid var(--line);border-radius:8px;padding:7px 12px;cursor:pointer}
              .metric-choice[aria-pressed="true"]{border-color:var(--accent);color:var(--accent);font-weight:700}
              .metric-chart{display:flex;gap:8px;overflow-x:auto;align-items:stretch;padding:12px 0 8px;border-bottom:1px solid var(--line)}
              .metric-month{font:inherit;cursor:pointer;flex:1 0 56px;min-width:56px;max-width:120px;display:flex;flex-direction:column;align-items:center;gap:6px;border:1px solid transparent;border-radius:8px;background:transparent;color:var(--text);padding:8px 3px}
              .metric-month:hover,.metric-month:focus-visible{border-color:var(--accent)}
              .metric-month[aria-pressed="true"]{background:var(--canvas);border-color:var(--accent)}
              .metric-column{width:24px;height:135px;display:flex;align-items:flex-end;background:var(--canvas);border-radius:5px;overflow:hidden}
              .metric-fill{display:block;width:100%;min-height:2px;background:var(--accent);border-radius:4px 4px 0 0}
              .metric-period{font-size:11px;white-space:nowrap}.metric-amount{font-size:12px;font-weight:700}
              @media print{.metric-choice{display:none}.metric-chart{overflow:visible}.metric-month{min-width:0;flex-basis:0}.metric-fill{print-color-adjust:exact;-webkit-print-color-adjust:exact}}
            </style>
            <script>
            (() => {
              const chart = document.getElementById('metric-chart');
              if (!chart) return;
              const months = [...chart.querySelectorAll('.metric-month')];
              const choices = [...document.querySelectorAll('.metric-choice')];
              const detail = document.getElementById('metric-selection');
              const value = (node, metric) => Number(node.dataset[metric] || 0);
              function render(metric) {
                const max = Math.max(1, ...months.map(node => value(node, metric)));
                choices.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.metric === metric)));
                months.forEach(node => {
                  const amount = value(node, metric);
                  const isDuration = metric === 'duration';
                  node.querySelector('.metric-fill').style.height = (amount / max * 100) + '%';
                  node.querySelector('.metric-amount').textContent = isDuration ? (node.dataset.duration || 'n/a') : amount;
                  node.title = node.dataset.period + ': ' + (isDuration ? 'Average days' : metric) + ' ' + (isDuration ? (node.dataset.duration || 'n/a') : amount);
                });
              }
              choices.forEach(button => button.addEventListener('click', () => render(button.dataset.metric)));
              months.forEach(node => node.addEventListener('click', () => {
                months.forEach(other => other.setAttribute('aria-pressed', String(other === node)));
                detail.textContent = node.dataset.period + ': opened ' + node.dataset.opened + ', closed ' + node.dataset.closed + ', average days ' + (node.dataset.duration || 'n/a');
              }));
              render('opened');
            })();
            </script>
            """);
        return sb.ToString();
    }

    /// <summary>Write bounded progress updates to stderr, leaving stdout machine-readable.</summary>
    private const int ProgressBarWidth = 20;
    private const int ProgressUpdateIntervalPercent = 5;

    public static Action<int, int, int, int> Progress(TextWriter writer, bool interactive)
    {
        var lastPercent = -1;
        return (done, total, matched, unlinked) =>
        {
            var percent = total == 0 ? 100 : done * 100 / total;
            if (done != total && percent / ProgressUpdateIntervalPercent == lastPercent / ProgressUpdateIntervalPercent) return;
            lastPercent = percent;
            var width = ProgressBarWidth;
            var filled = percent * width / 100;
            var line = $"[{new string('#', filled)}{new string('-', width - filled)}] {percent,3}% ({done}/{total}) matched: {matched}, unlinked: {unlinked}";
            if (interactive)
            {
                writer.Write("\r" + line + (done == total ? "\n" : ""));
                writer.Flush();
            }
            else writer.WriteLine(line);
        };
    }

    public static async Task<int> RunAsync(Context ctx)
    {
        var area = ctx.Args.Get("--area");
        if (string.IsNullOrWhiteSpace(area) && ctx.Args.Get("--scope") != "repository")
            throw AdoException.Usage("--area is required (example: ado metrics --area Product\\ALPHA)");
        var format = ctx.Args.Get("--format") ?? (ctx.Json ? "json" : "html");
        if (format is not ("html" or "json"))
            throw AdoException.Usage("--format must be html or json");
        var value = ctx.Args.Get("--months") ?? "12";
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var months) || months is < 1 or > 60)
            throw AdoException.Usage("--months must be between 1 and 60");
        var project = ctx.RequireProject();
        var scope = ctx.Args.Get("--scope") ?? "area";
        if (scope == "area" && area is not null)
        {
            var normalized = NormalizeAreaPath(area);
            if (normalized != area) Console.Error.WriteLine($"Normalized Area Path: {normalized}");
            area = normalized;
        }
        if (scope is not ("area" or "repository"))
            throw AdoException.Usage("--scope must be area or repository");
        Console.Error.WriteLine($"Metrics: project={project}, scope={scope}, months={months}");
        List<PullRequest> matched;
        int unlinked;
        string displayedArea = area ?? "";
        if (scope == "repository")
        {
            var repo = ctx.Args.Get("--repo") ?? ctx.Repo;
            if (string.IsNullOrWhiteSpace(repo))
                throw AdoException.Usage("--scope repository requires --repo <name> or a detected git repository");
            Console.Error.WriteLine($"Fetching pull requests from repository {repo}...");
            var all = await ctx.Client.GetPullRequestsAsync(project, repo, "all", null);
            Console.Error.WriteLine($"Fetched {all.Count} pull requests.");
            var cutoff = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month,
                1, 0, 0, 0, TimeSpan.Zero).AddMonths(1 - months);
            matched = all.Where(p => IsCandidate(p, cutoff)).ToList();
            unlinked = 0;
            displayedArea = $"Repository: {repo} (not Area Path filtered)";
        }
        else
        {
            Console.Error.WriteLine($"Fetching project pull requests and matching linked work items in {area}...");
            var selectedArea = area ?? throw AdoException.Usage("--area is required for area scope");
            (matched, unlinked) = await CollectAsync(ctx.Client, project, selectedArea, months,
                Progress(Console.Error, !Console.IsErrorRedirected));
            if (matched.Count == 0)
                Console.Error.WriteLine("Warning: " + NoMatchesMessage(selectedArea, unlinked));
        }
        Console.Error.WriteLine($"Matched {matched.Count} pull requests; unlinked candidates: {unlinked}. Calculating KPIs...");
        var report = Aggregate(project, displayedArea, months, DateTimeOffset.UtcNow, matched, unlinked);
        Console.Error.WriteLine($"Fetching commit counts for {matched.Count} PRs...");
        var inventory = new List<PrDetail>();
        var commitErrors = 0;
        for (var i = 0; i < matched.Count; i++)
        {
            var pr = matched[i];
            int? commitCount;
            try { commitCount = (await ctx.Client.GetPullRequestCommitsAsync(pr)).Count; }
            catch (AdoException) { commitCount = null; commitErrors++; }
            inventory.Add(Detail(pr, commitCount));
            if ((i + 1) % 25 == 0 || i + 1 == matched.Count)
                Console.Error.WriteLine($"Commit counts: {i + 1}/{matched.Count} PRs checked");
        }
        if (commitErrors > 0)
            Console.Error.WriteLine($"Warning: commit counts unavailable for {commitErrors} PRs.");
        report = WithPullRequests(report, inventory);
        if (scope == "area")
        {
            var selectedAreaForTests = area ?? throw AdoException.Usage("--area is required for test coverage");
            Console.Error.WriteLine("Collecting area test coverage metrics...");
            report = report with { Tests = await CollectTestMetricsAsync(ctx.Client, project, selectedAreaForTests,
                message => Console.Error.WriteLine(message)) };
        }
        Console.Error.WriteLine($"Rendering {format.ToUpperInvariant()} report...");
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
