namespace AdoCli.Cli;

using System.Net;
using System.Text.RegularExpressions;
using AdoCli.Api;

public static partial class WorkItemCommands
{
    /// <summary>ado workitem list: work items assigned to you, open ones unless --state says otherwise; --type and --state narrow it down.</summary>
    public static async Task<int> ListAsync(Context ctx)
    {
        var items = await ctx.Client.GetMyWorkItemsAsync(ctx.Project, ctx.Limit, Values(ctx.Args, "--type"), Values(ctx.Args, "--state"));
        if (ctx.Json)
            return Output.WriteJson(items);

        Output.Table(["ID", "TYPE", "STATE", "PRI", "ITERATION", "CHANGED", "TITLE"], items.Select(ListRow));
        return 0;
    }

    /// <summary>A row of <c>workitem list</c>: id, type, state, priority, iteration path, date last changed (local time) and title.</summary>
    public static string[] ListRow(WorkItem w) =>
    [
        w.Id.ToString(),
        w.Field("System.WorkItemType"),
        w.Field("System.State"),
        w.Field("Microsoft.VSTS.Common.Priority"),
        w.Field("System.IterationPath"),
        DateTimeOffset.TryParse(w.Field("System.ChangedDate"), out var changed) ? changed.ToLocalTime().ToString("yyyy-MM-dd") : "",
        w.Field("System.Title"),
    ];

    /// <summary>Every value of a repeatable option, also split at commas: --type Bug,Task --type "User Story".</summary>
    public static List<string> Values(Args a, string option) =>
        a.GetAll(option).SelectMany(v => (v ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static async Task<int> ShowAsync(Context ctx)
    {
        var w = await ctx.Client.GetWorkItemAsync(ctx.Id("workitem show <id>"));
        if (ctx.Json)
            return Output.WriteJson(w);

        Console.WriteLine($"""
            #{w.Id}
            {w.Field("System.Title")}

            Type:     {w.Field("System.WorkItemType")}
            State:    {w.Field("System.State")}
            Assigned: {w.Field("System.AssignedTo")}
            """);
        var (rows, blocks) = OtherFields(w);
        if (rows.Count > 0)
        {
            Console.WriteLine();
            Output.Table(["FIELD", "VALUE"], rows);
        }
        foreach (var (name, text) in blocks)
            Console.WriteLine($"\n{name}:\n{text}");
        return 0;
    }

    /// <summary>ado workitem create: from --type/--title and field options, or prompts for type, title and description when interactive.</summary>
    public static async Task<int> CreateAsync(Context ctx)
    {
        var project = ctx.RequireProject();
        var a = ctx.Args;
        string type, title, description;
        if (a.Get("--title") is { } t)
        {
            type = a.Get("--type") ?? throw AdoException.Usage("--type is required, e.g. --type Bug");
            title = t;
            description = a.Get("--description") ?? "";
        }
        else
        {
            if (Console.IsInputRedirected)
                throw AdoException.Usage("--type and --title are required when not running interactively");
            type = Term.Prompt("Type", a.Get("--type"));
            title = Term.Prompt("Title");
            description = Term.Prompt("Description", "");
        }
        if (type.Length == 0 || title.Length == 0)
            throw AdoException.Usage("a type and a title are required");

        var fields = Fields(a);
        fields["System.Title"] = title;
        if (description.Length > 0)
            fields["System.Description"] = Html(description);

        var w = await ctx.Client.CreateWorkItemAsync(project, type, fields);
        if (ctx.Json)
            return Output.WriteJson(w);
        Console.WriteLine($"Created {w.Field("System.WorkItemType")} #{w.Id}: {w.Field("System.Title")}");
        return 0;
    }

    /// <summary>ado workitem edit &lt;id&gt;: sets the fields given as options; others stay unchanged.</summary>
    public static async Task<int> EditAsync(Context ctx)
    {
        var id = ctx.Id("workitem edit <id> [--title t] [--description d] [--state s] [--assigned-to who] [--field Name=value] ...");
        var fields = Fields(ctx.Args);
        if (fields.Count == 0)
            throw AdoException.Usage("nothing to change; pass --title, --description, --state, --assigned-to, --area, --iteration, --tags, --comment or --field Name=value");

        var w = await ctx.Client.UpdateWorkItemAsync(id, fields);
        Console.Error.WriteLine($"Updated work item #{w.Id}: {w.Field("System.Title")}");
        return ctx.Json ? Output.WriteJson(w) : 0;
    }

    static readonly (string Option, string Field)[] FieldOptions =
    [
        ("--title", "System.Title"),
        ("--state", "System.State"),
        ("--assigned-to", "System.AssignedTo"),
        ("--area", "System.AreaPath"),
        ("--iteration", "System.IterationPath"),
        ("--tags", "System.Tags"),
    ];

    /// <summary>Fields to set from the options, keyed by reference name; --field Name=value comes last and wins.</summary>
    public static Dictionary<string, string> Fields(Args a)
    {
        var fields = new Dictionary<string, string>();
        foreach (var (option, field) in FieldOptions)
            if (a.Get(option) is { } value)
                fields[field] = value;
        if (a.Get("--description") is { } description)
            fields["System.Description"] = Html(description);
        // System.History is the discussion: setting it adds a comment.
        if (a.Get("--comment") is { } comment)
            fields["System.History"] = Html(comment);
        foreach (var f in a.GetAll("--field"))
        {
            var eq = f?.IndexOf('=') ?? -1;
            if (eq <= 0)
                throw AdoException.Usage($"--field expects Name=value, e.g. --field Microsoft.VSTS.Common.Priority=1 (got '{f}')");
            fields[f![..eq]] = f[(eq + 1)..];
        }
        return fields;
    }

    /// <summary>Plain text as HTML for description and comment fields, keeping line breaks.</summary>
    public static string Html(string text) =>
        WebUtility.HtmlEncode(text).ReplaceLineEndings("<br>");

    // Shown in the header of `workitem show`, so not repeated in its field list.
    static readonly HashSet<string> HeaderFields = ["System.Id", "System.Title", "System.WorkItemType", "System.State", "System.AssignedTo"];

    /// <summary>
    /// Every field the header leaves out, sorted by reference name: one-line values as rows,
    /// HTML or multi-line values (Description, Repro Steps, ...) as text blocks. Empty fields are skipped.
    /// </summary>
    public static (List<string[]> Rows, List<(string Name, string Text)> Blocks) OtherFields(WorkItem w)
    {
        var rows = new List<string[]>();
        var blocks = new List<(string, string)>();
        foreach (var name in w.Fields.Keys.Where(n => !HeaderFields.Contains(n)).Order(StringComparer.OrdinalIgnoreCase))
        {
            var value = w.Field(name);
            var text = Tag().IsMatch(value) ? PlainText(value) : value.Trim();
            if (text.Length == 0)
                continue;
            if (text.Contains('\n') || Tag().IsMatch(value))
                blocks.Add((name, text));
            else
                rows.Add([name, text]);
        }
        return (rows, blocks);
    }

    /// <summary>Descriptions are HTML; line breaks are kept, other markup dropped.</summary>
    public static string PlainText(string html) =>
        WebUtility.HtmlDecode(Tag().Replace(LineBreak().Replace(html, "\n"), "")).Trim();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|li|h\d)>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();
}
