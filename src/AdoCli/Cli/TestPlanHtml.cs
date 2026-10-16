namespace AdoCli.Cli;

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AdoCli.Api;

/// <summary>Standalone, read-only HTML report from the same data as the editable JSON export.</summary>
public static class TestPlanHtml
{
    static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    static string Plain(string? value) => Regex.Replace(
        WebUtility.HtmlDecode(Regex.Replace(value ?? "", "<[^>]*>", " ")),
        @"\s+", " ").Trim();
    static string Label(string? value) => E(Plain(value));

    public static string Render(TestPlanFile file)
    {
        var plan = file.Plan ?? throw new ArgumentException("Missing plan.");
        var suites = file.Suites ?? [];
        var cases = (file.TestCases ?? []).ToDictionary(c => c.Id);
        var children = suites.ToLookup(s => s.ParentId);
        var visited = new HashSet<int>();
        var sb = new StringBuilder();
        sb.AppendLine("""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="light">
            <title>Test plan report</title>
            <style>
            :root{font:15px/1.55 system-ui,-apple-system,Segoe UI,sans-serif;color:#253247;background:#f6f8fb}
            *{box-sizing:border-box}body{margin:0}a{color:#245b92;text-decoration:none}a:hover{text-decoration:underline}
            .layout{display:grid;grid-template-columns:265px minmax(0,1fr);min-height:100vh}
            aside{background:#fff;border-right:1px solid #dce4ed;padding:24px 18px;position:sticky;top:0;height:100vh;overflow:auto}
            aside h2{font-size:12px;text-transform:uppercase;letter-spacing:.1em;color:#708096}
            aside a{display:block;padding:7px 10px;border-radius:6px;overflow-wrap:anywhere}
            aside a:hover{background:#eef3fa}.depth{margin-left:14px}
            main{width:100%;max-width:1150px;padding:42px 44px;margin:0 auto}
            h1{font-size:30px;line-height:1.2;margin:8px 0}h2{font-size:22px;margin:35px 0 12px}
            h3{font-size:17px;margin:0 0 10px}.muted{color:#68788c}.eyebrow{font-size:12px;text-transform:uppercase;letter-spacing:.1em;color:#627a95}
            .meta{display:flex;gap:12px;flex-wrap:wrap;margin:18px 0 28px}.meta span{background:#e9eff7;border-radius:6px;padding:5px 10px;font-size:13px}
            .case{background:#fff;border:1px solid #dce4ed;border-radius:9px;padding:20px;margin:16px 0;break-inside:avoid}
            .case small{color:#69798e}.table-wrap{overflow-x:auto}table{width:100%;border-collapse:collapse;table-layout:fixed;margin-top:12px}
            th,td{border-bottom:1px solid #e1e7ee;padding:10px 12px;text-align:left;vertical-align:top;overflow-wrap:anywhere;white-space:pre-wrap}
            th{background:#f0f4f9;font-size:12px;color:#50647d}th:first-child{width:56px}th:nth-child(2),th:nth-child(3){width:calc((100% - 56px)/2)}
            .empty{color:#7a8595;font-style:italic}.suite{margin-bottom:30px}.badge{font-size:12px;padding:3px 7px;border-radius:5px;background:#edf2f8}
            @media(max-width:750px){.layout{display:block}aside{height:auto;position:static;border-bottom:1px solid #dce4ed}main{padding:24px 16px}}
            @media print{aside{display:none}.layout{display:block}main{max-width:none;padding:0}.case{break-inside:avoid}}
            </style></head><body><div class="layout"><aside><h2>Test plan · Suites</h2>
            """);
        void Nav(SuiteInfo suite, int depth)
        {
            if (!visited.Add(suite.Id)) return;
            sb.Append("<a class=\"").Append(depth > 0 ? "depth" : "").Append("\" href=\"#suite-")
                .Append(suite.Id).Append("\">").Append(E(suite.Name)).AppendLine("</a>");
            foreach (var child in children[suite.Id]) Nav(child, depth + 1);
        }
        foreach (var s in suites.Where(s => s.ParentId is null || suites.All(p => p.Id != s.ParentId))) Nav(s, 0);
        foreach (var s in suites) Nav(s, 0);
        sb.Append("</aside><main><div class=\"eyebrow\">Test plan #").Append(plan.Id)
            .Append("</div><h1>").Append(E(plan.Name)).Append("</h1><p class=\"muted\">")
            .Append(E(file.Project)).Append("</p><div class=\"meta\"><span>Status: ")
            .Append(E(plan.State)).Append("</span><span>Iteration: ").Append(E(plan.Iteration))
            .Append("</span><span>").Append(suites.Count).Append(" suites</span><span>")
            .Append(cases.Count).AppendLine(" unique test cases</span></div>");
        var displayed = new HashSet<int>();
        void Section(SuiteInfo suite, int depth)
        {
            if (!displayed.Add(suite.Id)) return;
            sb.Append("<section class=\"suite\" id=\"suite-").Append(suite.Id)
                .Append("\"><h2>").Append(E(suite.Name)).Append(" <span class=\"badge\">#")
                .Append(suite.Id).Append("</span></h2><p class=\"muted\">")
                .Append(suite.TestCaseIds.Count).AppendLine(" assigned test cases</p>");
            foreach (var id in suite.TestCaseIds.Distinct())
            {
                if (!cases.TryGetValue(id, out var c))
                {
                    sb.Append("<p class=\"empty\">Test case #").Append(id).AppendLine(" not available</p>");
                    continue;
                }
                sb.Append("<article class=\"case\"><h3>").Append(E(c.Title)).Append("</h3><small>Test case #")
                    .Append(c.Id).Append(" · Revision ").Append(c.Rev).Append("</small>");
                if (c.Fields?.GetValueOrDefault("System.State") is { Length: > 0 } state)
                    sb.Append(" <span class=\"badge\">").Append(E(state)).Append("</span>");
                sb.AppendLine("<div class=\"table-wrap\"><table><thead><tr><th>#</th><th>Action</th><th>Expected result</th></tr></thead><tbody>");
                void Steps(IEnumerable<TestStep> steps, string prefix)
                {
                    var n = 0;
                    foreach (var step in steps)
                    {
                        n++;
                        var number = prefix + n;
                        sb.Append("<tr><td>").Append(E(number)).Append("</td><td>");
                        if (step.SharedStepsId is { } shared)
                        {
                            sb.Append("Shared steps #").Append(shared).Append("</td><td class=\"muted\">Referenced shared steps</td></tr>");
                            if (step.Steps is { Count: > 0 }) Steps(step.Steps, number + ".");
                        }
                        else
                        {
                            sb.Append(Label(step.Action)).Append("</td><td>")
                                .Append(Label(step.ExpectedResult)).AppendLine("</td></tr>");
                        }
                    }
                }
                Steps(c.Steps ?? [], "");
                if (c.Steps is not { Count: > 0 })
                    sb.AppendLine("<tr><td colspan=\"3\" class=\"empty\">No steps</td></tr>");
                sb.AppendLine("</tbody></table></div></article>");
            }
            sb.AppendLine("</section>");
            foreach (var child in children[suite.Id]) Section(child, depth + 1);
        }
        displayed.Clear();
        foreach (var s in suites.Where(s => s.ParentId is null || suites.All(p => p.Id != s.ParentId))) Section(s, 0);
        foreach (var s in suites) Section(s, 0);
        sb.AppendLine("</main></div></body></html>");
        return sb.ToString();
    }
}
