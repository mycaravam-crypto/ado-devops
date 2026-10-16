namespace AdoCli.Cli;

using System.Text.Json;
using AdoCli.Api;

/// <summary>Exports explicit Azure DevOps requirement-to-case links for offline DocWizz import.</summary>
public static class TestPlanTraceExport
{
    /// <summary>A case assigned to one or more suites, without steps or parameter values.</summary>
    public sealed record Case(int Id, string Title, string Area, int[] SuiteIds);
    /// <summary>A requirement and its explicitly linked test cases in the selected plan.</summary>
    public sealed record Requirement(int Id, string Title, int[] TestCaseIds);
    /// <summary>Versioned, read-only snapshot; results are current point outcomes, not historical coverage.</summary>
    public sealed record Snapshot(string Format, string Project, string Area, DateTimeOffset GeneratedAt,
        int PlanId, string PlanName, Case[] TestCases, Requirement[] Requirements);

    /// <summary>Collects cases and explicit TestedBy links without exporting sensitive step or parameter content.</summary>
    public static async Task<Snapshot> BuildAsync(AdoClient client, string project, int planId, string area)
    {
        var plan = await client.GetTestPlanAsync(project, planId);
        var suites = await client.GetTestSuitesAsync(project, planId);
        var assignments = new Dictionary<int, List<int>>();
        foreach (var suite in suites)
            foreach (var id in await client.GetSuiteTestCaseIdsAsync(project, planId, suite.Id))
            {
                if (!assignments.TryGetValue(id, out var list)) assignments[id] = list = [];
                if (!list.Contains(suite.Id)) list.Add(suite.Id);
            }
        var cases = await client.GetWorkItemsAsync(assignments.Keys, ["System.Title", "System.AreaPath"]);
        var selected = cases.Where(c => TestPlanProgress.InsideArea(c.Field("System.AreaPath"), area))
            .OrderBy(c => c.Id).ToList();
        var caseIds = selected.Select(c => c.Id).ToHashSet();
        var records = selected.Select(c => new Case(c.Id, c.Field("System.Title"),
            c.Field("System.AreaPath"), assignments[c.Id].Order().ToArray())).ToArray();
        var ids = await client.QueryWorkItemIdsAsync(project, new WorkItemFilter
        {
            Everyone = true, Area = area, States = ["any"],
            Types = ["Requirement", "User Story", "Product Backlog Item"]
        });
        var requirements = new List<Requirement>();
        foreach (var id in ids.Distinct().Order())
        {
            var item = await client.GetWorkItemWithRelationsAsync(id);
            var linked = (item.Relations ?? []).Where(TestPlanCoverage.IsTestLink)
                .Select(r => TestPlanCoverage.LinkedId(r.Url)).OfType<int>()
                .Where(caseIds.Contains).Distinct().Order().ToArray();
            requirements.Add(new(id, item.Field("System.Title"), linked));
        }
        return new("ado.test-traceability/v1", project, area, DateTimeOffset.UtcNow,
            planId, plan.Name, records, requirements.ToArray());
    }

    /// <summary>Runs the trace-export command and writes versioned JSON to stdout or a file.</summary>
    public static async Task<int> RunAsync(Context ctx)
    {
        var id = ctx.Id("testplan trace-export <id> --area <path> [--output file]");
        var area = ctx.Args.Get("--area");
        if (string.IsNullOrWhiteSpace(area)) throw AdoException.Usage("--area is required");
        var data = await BuildAsync(ctx.Client, ctx.RequireProject(), id, area);
        var json = JsonSerializer.Serialize(data, Json.Options) + "\n";
        if (ctx.Args.Get("--output") is { } file) File.WriteAllText(file, json);
        else Console.Write(json);
        return 0;
    }
}
