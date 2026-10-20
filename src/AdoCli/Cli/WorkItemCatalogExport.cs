namespace AdoCli.Cli;

using System.Text.Json;
using AdoCli.Api;

/// <summary>Read-only, area-scoped inventory. Hierarchy is derived exclusively from explicit ADO links.</summary>
public static class WorkItemCatalogExport
{
    public const string Schema = "ado.workitem-catalog/v1";
    const string AnyState = "any";
    const int MaxWiqlRows = 20000;
    const string AreaField = "System.AreaPath";
    const string IterationField = "System.IterationPath";

    public sealed record Item(int Id, string Type, string Title, string State, string Area, string Iteration,
        int? Revision, string? ChangedDate, string? WebUrl,
        string? Description, string? AcceptanceCriteria, IReadOnlyList<string> Criteria,
        IReadOnlyList<Edge> Relations);

    public sealed record Edge(string Kind, int Id);
    public sealed record Snapshot(string Schema, string Project, string Area, string Source,
        bool Complete, IReadOnlyList<string> Warnings, IReadOnlyList<Item> Items);

    public static async Task<int> RunAsync(Context ctx)
    {
        var area = ctx.Args.Get("--area");
        if (string.IsNullOrWhiteSpace(area))
            throw AdoException.Usage("workitem export requires --area <path>");
        var project = ctx.RequireProject();
        var ids = await ctx.Client.QueryWorkItemIdsAsync(project, new WorkItemFilter
        {
            Everyone = true, States = [AnyState], Area = area
        });
        var warnings = new List<string>();
        // Azure DevOps WIQL returns at most 20,000 rows; a full-cap result cannot prove completeness.
        if (ids.Count >= MaxWiqlRows)
            warnings.Add("WIQL returned 20000 IDs: the selection may be truncated");
        var output = new List<Item>();
        var description = ctx.Args.Get("--description-field") is { Length: > 0 } d ? new[] { d } : WorkItemCommands.DescriptionFields;
        var criteria = ctx.Args.Get("--criteria-field") is { Length: > 0 } c ? new[] { c } : WorkItemCommands.CriteriaFields;
        foreach (var id in ids.Distinct().Order())
        {
            try
            {
                var item = await ctx.Client.GetWorkItemWithRelationsAsync(id);
                // BuildContext resolves and normalizes text and explicit links; no invented hierarchy.
                var detail = WorkItemCommands.BuildContext(ctx.Client.Server, item, null,
                    new Dictionary<int, WorkItem>(), description, criteria);
                var edges = (detail.Relations.Items ?? []).Select(r => new Edge(r.Kind, r.Id))
                    .Distinct().OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Id).ToArray();
                output.Add(new Item(id, detail.Type, detail.Title, detail.State,
                    item.Field(AreaField), item.Field(IterationField),
                    detail.Revision, detail.ChangedDate, detail.WebUrl, detail.Description.Text,
                    detail.AcceptanceCriteria.Text, detail.AcceptanceCriteria.Items.Select(c => c.Text).ToArray(), edges));
            }
            catch (AdoException e) when (e.ExitCode is AdoException.NotFound or AdoException.Permission)
            {
                warnings.Add($"work item {id} unavailable: {e.Message}");
            }
        }
        var complete = warnings.Count == 0 && output.Count == ids.Distinct().Count();
        var snapshot = new Snapshot(Schema, project, area, ctx.Client.Server, complete, warnings, output);
        var json = JsonSerializer.Serialize(snapshot, Json.Options) + "\n";
        var path = ctx.Args.Get("--output");
        if (path is null) Console.Write(json);
        else File.WriteAllText(path, json);
        return snapshot.Complete ? 0 : 1;
    }
}
