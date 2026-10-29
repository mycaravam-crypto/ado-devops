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

    /// <summary>Renders the complete offline test plan report, optionally including resolved Shared Steps and test progress.</summary>
    public static string Render(TestPlanFile file, IReadOnlyDictionary<int, IReadOnlyList<TestStep>>? sharedSteps = null, IReadOnlyDictionary<int, string>? sharedErrors = null, TestPlanProgress.Report? progress = null, bool excludeParameters = false, string? reportTitle = null, string? introductionHtml = null)
    {
        var plan = file.Plan ?? throw new ArgumentException("Missing plan.");
        var generatedAt = DateTimeOffset.UtcNow;
        var suites = file.Suites ?? [];
        reportTitle ??= "Test plan report";
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
            :root{--canvas:#f3f5f8;--surface:#fff;--surface-soft:#f8fafc;--ink:#172334;--text:#344457;--muted:#5c6b7e;--line:#dce2eb;--accent:#235bbd;--accent-soft:#e9f0fd;--shadow:0 2px 5px #1221370a,0 14px 44px #1221370b;--header-height:68px}
            :root[data-theme="dark"]{color-scheme:dark;--canvas:#101620;--surface:#192331;--surface-soft:#202d3d;--ink:#f1f5fb;--text:#d2ddeb;--muted:#a3b3c6;--line:#35465a;--accent:#8eb7ff;--accent-soft:#253b59;--shadow:0 12px 42px #0003}
            @media(prefers-color-scheme:dark){:root:not([data-theme="light"]){color-scheme:dark;--canvas:#101620;--surface:#192331;--surface-soft:#202d3d;--ink:#f1f5fb;--text:#d2ddeb;--muted:#a3b3c6;--line:#35465a;--accent:#8eb7ff;--accent-soft:#253b59;--shadow:0 12px 42px #0003}}
            *{box-sizing:border-box}html{scroll-behavior:smooth;scroll-padding-top:90px}body{margin:0;color:var(--text);background:var(--canvas);font:15px/1.7 system-ui,-apple-system,Segoe UI,sans-serif}
            button,input{font:inherit}button{cursor:pointer}a{color:var(--accent)}a:hover{color:#174a9c}:focus-visible{outline:3px solid var(--accent);outline-offset:2px}
            header{position:sticky;top:0;z-index:30;min-height:var(--header-height);display:flex;align-items:center;gap:14px;padding:10px clamp(16px,2.5vw,36px);background:var(--surface);border-bottom:1px solid var(--line)}
            .brand{display:flex;align-items:center;gap:10px;color:var(--ink);font-weight:750;font-size:18px;text-decoration:none}.brand-mark{display:grid;place-items:center;width:34px;height:34px;border-radius:10px;background:#285bb0;color:#fff;font-size:13px}
            .header-label{margin-left:12px;font-size:13px;color:var(--muted)}.icon-button{width:38px;height:38px;border:1px solid var(--line);border-radius:9px;background:var(--surface);color:var(--ink)}
            #nav-toggle,#nav-close{display:none}.layout{display:grid;grid-template-columns:minmax(200px,250px) minmax(0,1fr);gap:clamp(18px,2vw,32px);max-width:1400px;margin:auto;padding:24px clamp(16px,2vw,36px) 64px;align-items:start}
            .side{position:sticky;top:calc(var(--header-height) + 24px);max-height:calc(100vh - var(--header-height) - 50px);overflow-y:auto;scrollbar-width:thin;padding-right:8px}
            .side-top{display:flex;justify-content:space-between;align-items:center;margin:0 0 15px;color:var(--ink);font-size:12px;font-weight:750;text-transform:uppercase;letter-spacing:.1em}
            .filter-wrap{margin-bottom:16px}#side-filter{width:100%;padding:9px 12px;background:var(--surface-soft);border:1px solid var(--line);border-radius:9px;color:var(--ink);font-size:13px}
            .side ul{list-style:none;margin:0;padding:0}.side ul ul{margin:2px 0 4px 10px;padding-left:2px;border-left:1px solid var(--line)}
            .side li{margin:1px 0}.side a,.side summary{display:block;padding:6px 10px;color:var(--text);font-size:13px;line-height:1.45;text-decoration:none;border-left:3px solid transparent;border-radius:0 8px 8px 0;overflow-wrap:break-word}
            .side>ul>li>a,.side>ul>li>details>summary{color:var(--ink);font-weight:650}
            .side summary{display:flex;align-items:center;cursor:pointer;list-style:none;gap:6px}.side summary::-webkit-details-marker{display:none}.side summary::after{content:'›';margin-left:auto;color:var(--muted);font-size:15px}
            .side details[open]>summary::after{transform:rotate(90deg)}.side summary a{flex:1;padding:0;border:0;color:inherit}.side a:hover,.side summary:hover{background:var(--surface-soft)}
            .side a[aria-current="location"]{background:var(--accent-soft);color:var(--accent);border-left-color:var(--accent);font-weight:700}
            .side-empty{font-size:13px;color:var(--muted)}main{min-width:0;background:var(--surface);border:1px solid var(--line);border-radius:14px;box-shadow:var(--shadow);padding:clamp(22px,3.5vw,54px);overflow-wrap:anywhere}
            h1{font-size:clamp(28px,3vw,39px);letter-spacing:-.045em;line-height:1.16;margin:8px 0;color:var(--ink)}h2{font-size:22px;margin:36px 0 12px;border-bottom:1px solid var(--line);padding-bottom:11px;color:var(--ink)}
            h3{font-size:17px;margin:0 0 10px;color:var(--ink)}h4{color:var(--ink)}.muted{color:var(--muted)}.eyebrow{font-size:11px;text-transform:uppercase;letter-spacing:.12em;color:var(--accent);font-weight:800}
            .meta{display:flex;gap:12px;flex-wrap:wrap;margin:18px 0 28px}.meta span{background:var(--accent-soft);border-radius:6px;padding:5px 10px;font-size:13px}
            .case{background:var(--surface);border:1px solid var(--line);border-radius:10px;padding:20px;margin:16px 0;break-inside:avoid}.case small{color:var(--muted)}
            .table-wrap{max-width:100%;overflow-x:auto;margin:18px 0 26px;border:1px solid var(--line);border-radius:10px}table{width:100%;border-spacing:0;border-collapse:separate;font-size:13px}
            th,td{border-bottom:1px solid var(--line);padding:10px 14px;text-align:left;vertical-align:top;overflow-wrap:normal}td{overflow-wrap:anywhere}th{background:var(--surface-soft);color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.045em}
            tbody tr:nth-child(even){background:var(--surface-soft)}tbody tr:last-child td{border-bottom:0}.empty{color:var(--muted);font-style:italic}.suite{margin-bottom:30px;scroll-margin-top:90px}.badge{font-size:12px;padding:3px 7px;border-radius:5px;background:var(--accent-soft)}
            @media(max-width:900px){#nav-toggle{display:inline-flex;align-items:center;justify-content:center}.layout{display:block;padding:16px}.side{display:none;position:fixed;z-index:45;top:var(--header-height);bottom:0;left:0;width:min(85vw,340px);max-height:none;padding:20px;background:var(--surface);border-right:1px solid var(--line);box-shadow:10px 0 24px #0002}body[data-nav-open="true"] .side{display:block}#nav-close{display:inline-flex;align-items:center;justify-content:center}main{width:100%}}
            @media(max-width:580px){header{padding:10px 13px}.header-label{display:none}.layout{padding:0 0 36px}main{border:0;border-radius:0;box-shadow:none;padding:24px 16px}}
            @media print{header,.side{display:none!important}.layout{display:block;padding:0}main{border:0;box-shadow:none;padding:0}.case{break-inside:avoid}}
            </style></head><body><header><button id="nav-toggle" class="icon-button" type="button" aria-controls="site-nav" aria-expanded="false" aria-label="Open navigation">☰</button><div class="brand"><span class="brand-mark">TP</span><span>Test Plans</span></div><span class="header-label">Azure DevOps · Report</span><button id="theme-toggle" class="icon-button" type="button" aria-label="Switch theme" title="Switch theme" style="margin-left:auto">☾</button></header><div class="layout"><nav class="side" id="site-nav" aria-label="Test suites"><div class="side-top">Explore <button id="nav-close" class="icon-button" type="button" aria-label="Close navigation">×</button></div><div class="filter-wrap"><input id="side-filter" type="search" placeholder="Filter suites..." aria-label="Filter suites"></div><p id="side-empty" class="side-empty" hidden>No suites match.</p><ul>
            """);
        void Nav(SuiteInfo suite)
        {
            if (!visited.Add(suite.Id)) return;
            var descendants = children[suite.Id].Where(c => !visited.Contains(c.Id)).ToList();
            sb.Append("<li>");
            if (descendants.Count > 0)
                sb.Append("<details open><summary>");
            sb.Append("<a href=\"#suite-").Append(suite.Id).Append("\">").Append(E(suite.Name)).Append("</a>");
            if (descendants.Count > 0)
            {
                sb.Append("</summary><ul>");
                foreach (var child in descendants) Nav(child);
                sb.Append("</ul></details>");
            }
            sb.Append("</li>");
        }
        foreach (var suite in suites.Where(s => s.ParentId is null || suites.All(p => p.Id != s.ParentId))) Nav(suite);
        foreach (var suite in suites) Nav(suite);
        sb.Append("</ul></nav><main>");
        if (introductionHtml is not null) sb.Append(introductionHtml);
        sb.Append("<div class=\"eyebrow\">Test plan #").Append(plan.Id)
            .Append("</div><h1>").Append(E(plan.Name)).Append("</h1><p class=\"muted\">")
            .Append(E(file.Project)).Append("</p><div class=\"meta\"><span>Status: ")
            .Append(E(plan.State)).Append("</span><span>Iteration: ").Append(E(plan.Iteration))
            .Append("</span><span>").Append(suites.Count).Append(" suites</span><span>")
            .Append(cases.Count).AppendLine(" unique test cases</span></div>");
        if (progress is not null) sb.Append(TestPlanProgress.Html(progress));
        sb.Append("<p class=\"muted\">Generated (UTC): ").Append(generatedAt.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(" · Source: Azure DevOps test plan/work items");
        if (progress is not null) sb.Append(" · Progress area: ").Append(E(progress.Area ?? "all"));
        sb.AppendLine("</p>");
        if (!excludeParameters) sb.AppendLine("<p class=\"muted\">Parameter values may contain sensitive data.</p>");
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
                if (c.Fields?.GetValueOrDefault("System.AreaPath") is { Length: > 0 } area)
                    sb.Append("<p class=\"muted\">Area Path: ").Append(E(area)).AppendLine("</p>");
                if (!excludeParameters && c.Fields?.GetValueOrDefault("Microsoft.VSTS.TCM.Parameters") is { Length: > 0 } definitions)
                    sb.Append("<p class=\"muted\">Parameters: ").Append(Label(definitions)).AppendLine("</p>");
                if (!excludeParameters && c.Fields?.GetValueOrDefault("Microsoft.VSTS.TCM.LocalDataSource") is { Length: > 0 } source)
                {
                    var rows = TestPlanParameters.Rows(source);
                    if (rows.Count > 0)
                    {
                        var columns = rows.SelectMany(row => row.Keys).Distinct().ToList();
                        sb.AppendLine("<h4>Parameter values</h4><div class=\"table-wrap\"><table><thead><tr>");
                        foreach (var col in columns) sb.Append("<th>").Append(E(col)).Append("</th>");
                        sb.AppendLine("</tr></thead><tbody>");
                        foreach (var row in rows)
                        {
                            sb.Append("<tr>");
                            foreach (var col in columns)
                                sb.Append("<td>").Append(E(row.GetValueOrDefault(col))).Append("</td>");
                            sb.AppendLine("</tr>");
                        }
                        sb.AppendLine("</tbody></table></div>");
                    }
                    else
                        sb.AppendLine("<p class=\"muted\">Parameter data present (not a supported table format).</p>");
                }
                sb.AppendLine("<div class=\"table-wrap\"><table><thead><tr><th>#</th><th>Action</th><th>Expected result</th></tr></thead><tbody>");
                var activeShared = new HashSet<int>();
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
                            if (sharedErrors is not null && sharedErrors.TryGetValue(shared, out var error))
                                sb.Append("<tr><td></td><td colspan=\"2\" class=\"empty\">").Append(E(error)).AppendLine("</td></tr>");
                            else if (sharedSteps is not null && sharedSteps.TryGetValue(shared, out var expanded))
                            {
                                if (!activeShared.Add(shared))
                                    sb.AppendLine("<tr><td></td><td colspan=\"2\" class=\"empty\">Circular shared steps reference</td></tr>");
                                else
                                {
                                    Steps(expanded, number + ".");
                                    activeShared.Remove(shared);
                                }
                            }
                            else if (step.Steps is { Count: > 0 }) Steps(step.Steps, number + ".");
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
        sb.AppendLine("""
            </main></div>
            <script>
            (() => {
              const root = document.documentElement;
              const themeButton = document.getElementById('theme-toggle');
              const themeKey = 'ado.testplan.theme';
              try {
                const stored = localStorage.getItem(themeKey);
                if (stored === 'light' || stored === 'dark') root.dataset.theme = stored;
              } catch (_) { /* file:// storage may be disabled */ }
              const isDark = () => root.dataset.theme === 'dark' ||
                (!root.dataset.theme && matchMedia('(prefers-color-scheme: dark)').matches);
              const updateThemeLabel = () => {
                const dark = isDark();
                themeButton.textContent = dark ? '☀' : '☾';
                themeButton.setAttribute('aria-label', dark ? 'Switch to light mode' : 'Switch to dark mode');
                themeButton.title = themeButton.getAttribute('aria-label');
              };
              themeButton.addEventListener('click', () => {
                root.dataset.theme = isDark() ? 'light' : 'dark';
                try { localStorage.setItem(themeKey, root.dataset.theme); } catch (_) { /* optional */ }
                updateThemeLabel();
              });
              matchMedia('(prefers-color-scheme: dark)').addEventListener?.('change', updateThemeLabel);
              updateThemeLabel();
              const nav = document.getElementById('site-nav');
              const toggle = document.getElementById('nav-toggle');
              const close = document.getElementById('nav-close');
              const filter = document.getElementById('side-filter');
              const links = Array.from(nav.querySelectorAll('a[href^="#suite-"]'));
              const empty = document.getElementById('side-empty');
              const setOpen = open => { document.body.dataset.navOpen = String(open); toggle.setAttribute('aria-expanded', String(open)); };
              toggle.addEventListener('click', () => setOpen(document.body.dataset.navOpen !== 'true'));
              close.addEventListener('click', () => { setOpen(false); toggle.focus(); });
              links.forEach(link => link.addEventListener('click', () => setOpen(false)));
              filter.addEventListener('input', () => {
                const query = filter.value.trim().toLocaleLowerCase();
                const walk = list => {
                  let any = false;
                  for (const li of list.children) {
                    if (li.tagName !== 'LI') continue;
                    const a = li.querySelector(':scope > a, :scope > details > summary > a');
                    const child = li.querySelector(':scope > details > ul');
                    const childMatch = child ? walk(child) : false;
                    const match = (a?.textContent || '').toLocaleLowerCase().includes(query);
                    li.hidden = !(match || childMatch);
                    if (query && childMatch) li.querySelector(':scope > details').open = true;
                    any ||= !li.hidden;
                  }
                  return any;
                };
                empty.hidden = walk(nav.querySelector(':scope > ul'));
              });
              const update = () => {
                let active = links[0];
                for (const link of links) {
                  const target = document.getElementById(link.hash.slice(1));
                  if (target && target.getBoundingClientRect().top <= 155) active = link;
                }
                links.forEach(link => link === active ? link.setAttribute('aria-current', 'location') : link.removeAttribute('aria-current'));
              };
              addEventListener('scroll', update, { passive:true });
              update();
            })();
            </script></body></html>
            """);
        return sb.ToString().Replace("<title>Test plan report</title>", "<title>" + E(reportTitle) + "</title>", StringComparison.Ordinal);
    }
}
