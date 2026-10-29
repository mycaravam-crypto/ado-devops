namespace AdoCli.Cli;

using System.Net;
using System.Diagnostics;
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
        public int Outstanding => Blocked + NotRun + Failed;
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
            .Append(report.Outstanding).Append(".</p>")
            .Append("<p class=\"muted\">Results are current test-point outcomes (suite × case × configuration). ")
            .Append("Executed means Passed + Failed; no-run and blocked points are not executed. ")
            .Append("This is a factual snapshot, not a release approval or a historical end-of-period snapshot.</p>")
            .Append("</section>");
        return sb.ToString();
    }

    /// <summary>Keep case and suite details within the same Area Path as point metrics.</summary>
    public static TestPlanFile FilterByArea(TestPlanFile file, string? area)
    {
        if (string.IsNullOrWhiteSpace(area)) return file;
        var cases = (file.TestCases ?? []).Where(c => c.Fields is not null &&
            c.Fields.TryGetValue("System.AreaPath", out var path) &&
            TestPlanProgress.InsideArea(path ?? "", area)).ToList();
        var ids = cases.Select(c => c.Id).ToHashSet();
        var suites = (file.Suites ?? []).Select(s => s with
        {
            TestCaseIds = s.TestCaseIds.Where(ids.Contains).ToList()
        }).Where(s => s.TestCaseIds.Count > 0).ToList();
        return file with { Suites = suites, TestCases = cases };
    }

    /// <summary>Empty signature lines for handwritten sign-off; not a digital signature.</summary>
    public static string SignatureHtml() => """
        <section id="signatures" class="closure-signatures">
          <h2>Sign-off / Unterschriften</h2>
          <p class="muted">Signatures document review, not automatic approval of test results.</p>
          <div class="signature-grid">
            <div><h3>Kunde / Customer</h3><div class="signature-line"></div><p>Name / Funktion</p><div class="signature-line"></div><p>Ort, Datum / Unterschrift</p></div>
            <div><h3>Lieferant / Supplier</h3><div class="signature-line"></div><p>Name / Funktion</p><div class="signature-line"></div><p>Ort, Datum / Unterschrift</p></div>
          </div>
        </section>
        <style>
          .signature-grid{display:grid;grid-template-columns:1fr 1fr;gap:32px;margin:24px 0}
          .signature-grid>div{break-inside:avoid}
          .signature-line{height:52px;border-bottom:1px solid var(--ink)}
          .closure-signatures{break-inside:avoid}
          @media(max-width:650px){.signature-grid{grid-template-columns:1fr}}
          @media print{.closure-signatures{break-before:page}}
        </style>
        """;

    public static async Task<int> RunAsync(Context ctx)
    {
        var id = ctx.Id("testplan report <id> [--area path] [--format html|json|pdf] [--output file]");
        var format = ctx.Args.Get("--format") ?? (ctx.Json ? "json" : "html");
        if (format is not ("html" or "json" or "pdf"))
            throw AdoException.Usage("--format must be html, json or pdf");
        if (format == "pdf" && string.IsNullOrWhiteSpace(ctx.Args.Get("--output")))
            throw AdoException.Usage("--output is required for PDF export");
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
            var scoped = FilterByArea(file, report.Area);
            output = TestPlanHtml.Render(scoped, shared, errors, progress, excludeParameters: true,
                reportTitle: "Test closure report", introductionHtml: SummaryHtml(report),
                closingHtml: SignatureHtml());
        }
        if (format == "pdf")
        {
            await TestClosurePdf.WriteAsync(output, ctx.Args.Get("--output")!);
            Console.Error.WriteLine($"Wrote test closure PDF to {ctx.Args.Get("--output")}");
            return 0;
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
