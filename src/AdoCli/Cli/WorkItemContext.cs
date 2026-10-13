namespace AdoCli.Cli;

using System.Text.RegularExpressions;
using AdoCli.Api;

/// <summary>The JSON document of <c>ado workitem context</c>, schema <see cref="WorkItemCommands.ContextSchema"/>.</summary>
public sealed record RequirementContext(
    string Schema,
    string Server,
    int Id,
    int? Revision,
    int? CurrentRevision,
    bool IsCurrent,
    string? Url,
    string WebUrl,
    string Project,
    string Type,
    string State,
    string Title,
    string? ChangedDate,
    RequirementText Description,
    RequirementCriteria AcceptanceCriteria,
    RequirementRelations Relations,
    List<string> Warnings);

/// <summary>
/// A text field: <see cref="SourceField"/> is the reference name it was read from, null when none of
/// <see cref="CheckedFields"/> had a value; <see cref="Html"/> is the raw value, <see cref="Text"/> the plain text.
/// </summary>
public sealed record RequirementText(List<string> CheckedFields, string? SourceField, string? Html, string? Text);

/// <summary>Acceptance criteria as <see cref="RequirementText"/> plus <see cref="Items"/>, in their original order.</summary>
public sealed record RequirementCriteria(List<string> CheckedFields, string? SourceField, string? Html, string? Text, List<CriterionItem> Items);

/// <summary>One criterion; <see cref="Index"/> is its position (from 1), not a stable identifier.</summary>
public sealed record CriterionItem(int Index, string Text);

/// <summary>Status "current": <see cref="Items"/> are the item's explicit links now. "unavailable": not known for this revision, Items is null.</summary>
public sealed record RequirementRelations(string Status, List<RelatedWorkItem>? Items);

/// <summary>An explicitly linked work item: <see cref="Kind"/> is parent, child or related; title, type and state are null when it is not visible.</summary>
public sealed record RelatedWorkItem(string Kind, string Rel, int Id, string Url, string? Title, string? Type, string? State);

public static partial class WorkItemCommands
{
    /// <summary>The schema name and version of the <c>workitem context</c> document.</summary>
    public const string ContextSchema = "ado.requirement-context/v1";

    /// <summary>Description fields tried in order: the description, then a Bug's repro steps.</summary>
    public static readonly string[] DescriptionFields = ["System.Description", "Microsoft.VSTS.TCM.ReproSteps"];

    /// <summary>Acceptance criteria fields tried in order.</summary>
    public static readonly string[] CriteriaFields = ["Microsoft.VSTS.Common.AcceptanceCriteria"];

    // The link types that are returned, in output order; every other link (dependencies, commits, hyperlinks) is left out.
    static readonly (string Rel, string Kind)[] RelationKinds =
    [
        ("System.LinkTypes.Hierarchy-Reverse", "parent"),
        ("System.LinkTypes.Hierarchy-Forward", "child"),
        ("System.LinkTypes.Related", "related"),
    ];

    /// <summary>
    /// ado workitem context &lt;id&gt; [--revision n]: a work item's requirement text (title, description, acceptance
    /// criteria) and explicit parent/child/related links as JSON. Read-only.
    /// </summary>
    public static async Task<int> ContextAsync(Context ctx)
    {
        var a = ctx.Args;
        var id = ctx.Id("workitem context <id> [--revision <n>]");
        int? revision = a.Get("--revision") is not { } r ? null
            : int.TryParse(r, out var n) && n > 0 ? n : throw AdoException.Usage("--revision must be a positive number");
        string[] descriptionFields = a.Get("--description-field") is { Length: > 0 } d ? [d] : DescriptionFields;
        string[] criteriaFields = a.Get("--criteria-field") is { Length: > 0 } c ? [c] : CriteriaFields;

        var current = await ctx.Client.GetWorkItemWithRelationsAsync(id);
        WorkItem? historical = null;
        if (revision is { } rev && rev != current.Rev)
        {
            if (rev > current.Rev)
                throw new AdoException($"work item {id} has no revision {rev}; its current revision is {current.Rev}", AdoException.NotFound);
            historical = await ctx.Client.GetWorkItemRevisionAsync(id, rev);
        }

        var links = historical is null ? Relations(current) : [];
        var visible = (await ctx.Client.GetWorkItemsAsync(links.Select(l => l.Id).Distinct())).ToDictionary(w => w.Id);
        return Output.WriteJson(BuildContext(ctx.Client.Server, current, historical, visible, descriptionFields, criteriaFields));
    }

    /// <summary>
    /// The context document of <paramref name="historical"/>, or of <paramref name="current"/> when it is null.
    /// Links are only reported for the current revision; <paramref name="linked"/> holds the linked items that are visible.
    /// </summary>
    public static RequirementContext BuildContext(string server, WorkItem current, WorkItem? historical, IReadOnlyDictionary<int, WorkItem> linked,
        string[] descriptionFields, string[] criteriaFields)
    {
        var w = historical ?? current;
        var warnings = new List<string>();
        RequirementRelations relations;
        if (historical is null)
        {
            var items = Relations(current).Select(l => linked.TryGetValue(l.Id, out var item)
                ? new RelatedWorkItem(l.Kind, l.Rel, l.Id, l.Url, item.Field("System.Title"), item.Field("System.WorkItemType"), item.Field("System.State"))
                : new RelatedWorkItem(l.Kind, l.Rel, l.Id, l.Url, null, null, null)).ToList();
            warnings.AddRange(items.Where(i => i.Title is null).Select(i => $"linked work item {i.Id} is not visible (deleted or no permission)"));
            relations = new("current", items);
        }
        else
        {
            warnings.Add($"relations unavailable for revision {historical.Rev}: the server only reports the links of the current revision ({current.Rev})");
            relations = new("unavailable", null);
        }

        var project = w.Field("System.TeamProject");
        var description = Text(w, descriptionFields);
        var criteria = Text(w, criteriaFields);
        return new(
            ContextSchema,
            server,
            current.Id,
            w.Rev,
            current.Rev,
            historical is null,
            w.Url,
            project.Length > 0 ? $"{server}/{Uri.EscapeDataString(project)}/_workitems/edit/{current.Id}" : $"{server}/_workitems/edit/{current.Id}",
            project,
            w.Field("System.WorkItemType"),
            w.Field("System.State"),
            w.Field("System.Title"),
            w.Field("System.ChangedDate") is { Length: > 0 } changed ? changed : null,
            description,
            new(criteria.CheckedFields, criteria.SourceField, criteria.Html, criteria.Text, Criteria(criteria.Html)),
            relations,
            warnings);
    }

    /// <summary>The first of <paramref name="fields"/> that has a non-empty value; nothing is made up when none has.</summary>
    static RequirementText Text(WorkItem w, string[] fields)
    {
        foreach (var name in fields)
        {
            var value = w.Field(name);
            if (PlainText(value) is { Length: > 0 } text)
                return new([.. fields], name, value, text);
        }
        return new([.. fields], null, null, null);
    }

    /// <summary>
    /// The criteria of an acceptance criteria field: its list items when it is an HTML list, otherwise its non-empty lines.
    /// Text and order are kept as written.
    /// </summary>
    public static List<CriterionItem> Criteria(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];
        var texts = ListItemContent().IsMatch(html)
            ? ListItemContent().Matches(html).Select(m => Whitespace().Replace(PlainText(m.Groups[1].Value), " ").Trim())
            : PlainText(html).Split('\n').Select(l => l.Trim());
        return texts.Where(t => t.Length > 0).Select((t, i) => new CriterionItem(i + 1, t)).ToList();
    }

    /// <summary>The parent, child and related links of a work item, by kind, then id; other links are left out.</summary>
    static List<(string Kind, string Rel, int Id, string Url)> Relations(WorkItem w) =>
        (w.Relations ?? [])
            .Select(r => (Kind: RelationKinds.FirstOrDefault(k => k.Rel == r.Rel).Kind, r.Rel, Id: LinkedId(r.Url), r.Url))
            .Where(r => r.Kind is not null && r.Id is not null)
            .Select(r => (r.Kind, r.Rel, r.Id!.Value, r.Url))
            .Distinct()
            .OrderBy(r => Array.FindIndex(RelationKinds, k => k.Kind == r.Kind)).ThenBy(r => r.Value)
            .ToList();

    // .../_apis/wit/workItems/123
    static int? LinkedId(string url) =>
        LinkedWorkItem().Match(url) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var id) ? id : null;

    [GeneratedRegex(@"/workItems/(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LinkedWorkItem();

    [GeneratedRegex(@"<li\b[^>]*>(.*?)(?=<li\b|</li>|</[ou]l>|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ListItemContent();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
