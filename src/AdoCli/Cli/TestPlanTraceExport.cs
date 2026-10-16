namespace AdoCli.Cli;

using System.Text.Json;
using AdoCli.Api;

/// <summary>Exports explicit Azure DevOps requirement-to-case links for offline DocWizz import.</summary>
public static class TestPlanTraceExport
{
    /// <summary>A case assigned to one or more suites, without steps or parameter values.</summary>
    public sealed record Case(int Id, string Title, string Area, int[] SuiteIds);
    /// <summary>A requirement and its explicitly linked test cases in the selected plan.</summary>
    public sealed record Requirement(int Id, string Title, int[] TestCaseIds,
        string? Description = null, string? AcceptanceCriteria = null,
        string? StoryPoints = null, string? Priority = null);

    // Work item process templates may name their estimation field StoryPoints, Effort, or Size.
    static Requirement Describe(WorkItem item, int[] linked)
    {
        static string? Field(WorkItem item, params string[] names)
        {
            foreach (var name in names)
                if (item.Field(name) is { Length: > 0 } value) return value;
            return null;
        }
        return new(item.Id, item.Field("System.Title"), linked,
            Field(item, "System.Description"),
            Field(item, "Microsoft.VSTS.Common.AcceptanceCriteria"),
            Field(item, "Microsoft.VSTS.Scheduling.StoryPoints", "Microsoft.VSTS.Scheduling.Effort", "Microsoft.VSTS.Scheduling.Size"),
            Field(item, "Microsoft.VSTS.Common.Priority"));
    }
    /// <summary>Versioned, read-only snapshot; results are current point outcomes, not historical coverage.</summary>
    public sealed record Snapshot(string Format, string Project, string Area, DateTimeOffset GeneratedAt,
        int PlanId, string PlanName, Case[] TestCases, Requirement[] Requirements);

    /// <summary>Collects cases and explicit TestedBy links without exporting sensitive step or parameter content.</summary>
    public static async Task<Snapshot> BuildAsync(AdoClient client, string project, int planId, string area, CliProgress? progress = null)
    {
        progress?.Stage("load plan and suites");
        var plan = await client.GetTestPlanAsync(project, planId);
        var suites = await client.GetTestSuitesAsync(project, planId);
        var assignments = new Dictionary<int, List<int>>();
        var suiteDone = 0;
        foreach (var suite in suites)
        {
            foreach (var id in await client.GetSuiteTestCaseIdsAsync(project, planId, suite.Id))
            {
                if (!assignments.TryGetValue(id, out var list)) assignments[id] = list = [];
                if (!list.Contains(suite.Id)) list.Add(suite.Id);
            }
            progress?.Report("suites", ++suiteDone, suites.Count);
        }
        progress?.Stage("load test cases");
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
        var requirementIds = ids.Distinct().Order().ToArray();
        var requirementDone = 0;
        foreach (var id in requirementIds)
        {
            var item = await client.GetWorkItemWithRelationsAsync(id);
            var linked = (item.Relations ?? []).Where(TestPlanCoverage.IsTestLink)
                .Select(r => TestPlanCoverage.LinkedId(r.Url)).OfType<int>()
                .Where(caseIds.Contains).Distinct().Order().ToArray();
            requirements.Add(Describe(item, linked));
            progress?.Report("requirements", ++requirementDone, requirementIds.Length);
        }
        return new("ado.test-traceability/v1", project, area, DateTimeOffset.UtcNow,
            planId, plan.Name, records, requirements.ToArray());
    }

    /// <summary>A plan that contains at least one test case within the selected Area Path.</summary>
    public sealed record Plan(int Id, string Name);
    /// <summary>Preserves exact plan/suite membership even when cases occur in multiple plans.</summary>
    public sealed record Assignment(int PlanId, int SuiteId, int TestCaseId);
    /// <summary>A version 2 snapshot covering all matching plans, deduplicated by test case ID.</summary>
    public sealed record MultiSnapshot(string Format, string Project, string Area, DateTimeOffset GeneratedAt,
        Plan[] Plans, Case[] TestCases, Assignment[] Assignments, Requirement[] Requirements);

    /// <summary>Collects all plan memberships and explicit requirement links for an Area Path; excludes steps and parameters.</summary>
    public static async Task<MultiSnapshot> BuildAllAsync(AdoClient client, string project, string area, CliProgress? progress = null)
    {
        progress?.Stage("list plans");
        var plans = await client.GetTestPlansAsync(project);
        var assignments = new List<Assignment>();
        var planDone = 0;
        foreach (var plan in plans.OrderBy(p => p.Id))
        {
            var suites = await client.GetTestSuitesAsync(project, plan.Id);
            foreach (var suite in suites)
                foreach (var caseId in await client.GetSuiteTestCaseIdsAsync(project, plan.Id, suite.Id))
                    assignments.Add(new(plan.Id, suite.Id, caseId));
            progress?.Report("plans", ++planDone, plans.Count);
        }
        progress?.Stage("load test cases");
        var ids = assignments.Select(a => a.TestCaseId).Distinct().ToArray();
        var workItems = await client.GetWorkItemsAsync(ids, ["System.Title", "System.AreaPath"]);
        var selected = workItems.Where(w => TestPlanProgress.InsideArea(w.Field("System.AreaPath"), area))
            .OrderBy(w => w.Id).ToArray();
        var selectedIds = selected.Select(w => w.Id).ToHashSet();
        var memberships = assignments.Where(a => selectedIds.Contains(a.TestCaseId))
            .Distinct().OrderBy(a => a.PlanId).ThenBy(a => a.SuiteId).ThenBy(a => a.TestCaseId).ToArray();
        var planIds = memberships.Select(a => a.PlanId).ToHashSet();
        var includedPlans = plans.Where(p => planIds.Contains(p.Id)).OrderBy(p => p.Id)
            .Select(p => new Plan(p.Id, p.Name)).ToArray();
        var cases = selected.Select(w => new Case(w.Id, w.Field("System.Title"), w.Field("System.AreaPath"),
            memberships.Where(a => a.TestCaseId == w.Id).Select(a => a.SuiteId).Distinct().Order().ToArray())).ToArray();
        var reqIds = await client.QueryWorkItemIdsAsync(project, new WorkItemFilter
        {
            Everyone = true, Area = area, States = ["any"],
            Types = ["Requirement", "User Story", "Product Backlog Item"]
        });
        var requirements = new List<Requirement>();
        var requirementIds = reqIds.Distinct().Order().ToArray();
        var requirementDone = 0;
        foreach (var id in requirementIds)
        {
            var item = await client.GetWorkItemWithRelationsAsync(id);
            var linked = (item.Relations ?? []).Where(TestPlanCoverage.IsTestLink)
                .Select(r => TestPlanCoverage.LinkedId(r.Url)).OfType<int>()
                .Where(selectedIds.Contains).Distinct().Order().ToArray();
            requirements.Add(Describe(item, linked));
            progress?.Report("requirements", ++requirementDone, requirementIds.Length);
        }
        return new("ado.test-traceability/v2", project, area, DateTimeOffset.UtcNow,
            includedPlans, cases, memberships, requirements.ToArray());
    }

    /// <summary>Runs the trace-export command and writes versioned JSON to stdout or a file.</summary>
    public static async Task<int> RunAsync(Context ctx)
    {
        var idText = ctx.Args.At(2);
        if (idText is not null && (!int.TryParse(idText, out var parsed) || parsed <= 0))
            throw AdoException.Usage("testplan trace-export [id] --area <path> [--output file]");
        var area = ctx.Args.Get("--area");
        if (string.IsNullOrWhiteSpace(area)) throw AdoException.Usage("--area is required");
        var progress = new CliProgress("testplan trace-export");
        object data = idText is null
            ? await BuildAllAsync(ctx.Client, ctx.RequireProject(), area, progress)
            : await BuildAsync(ctx.Client, ctx.RequireProject(), int.Parse(idText), area, progress);
        var json = JsonSerializer.Serialize(data, Json.Options) + "\n";
        if (ctx.Args.Get("--output") is { } file) File.WriteAllText(file, json);
        else Console.Write(json);
        return 0;
    }
}
