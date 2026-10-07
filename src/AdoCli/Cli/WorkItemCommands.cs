namespace AdoCli.Cli;

using System.Net;
using System.Text.RegularExpressions;
using AdoCli.Api;

public static partial class WorkItemCommands
{
    /// <summary>ado workitem list: work items assigned to you, open ones unless --state says otherwise; the filter options narrow it down, --all widens it to everyone.</summary>
    public static async Task<int> ListAsync(Context ctx)
    {
        if (ctx.Args.Has("--ids"))
        {
            // One id per line, for: ado workitem list ... --ids | ado workitem edit - ...
            foreach (var id in await ctx.Client.QueryWorkItemIdsAsync(ctx.Project, Filter(ctx.Args), ctx.Limit))
                Console.WriteLine(id);
            return 0;
        }

        var items = await ctx.Client.QueryWorkItemsAsync(ctx.Project, Filter(ctx.Args), ctx.Limit);
        if (ctx.Json)
            return Output.WriteJson(items);

        Output.Table(["ID", "TYPE", "STATE", "PRI", "ITERATION", "CHANGED", "TITLE"], items.Select(ListRow));
        return 0;
    }

    /// <summary>The query of <c>workitem list</c> from its options.</summary>
    public static WorkItemFilter Filter(Args a) => new()
    {
        Everyone = a.Has("--all"),
        AssignedTo = a.Get("--assigned-to"),
        Types = Values(a, "--type"),
        States = Values(a, "--state"),
        Area = a.Get("--area"),
        Iteration = a.Get("--iteration"),
        Tags = Values(a, "--tag"),
        TitleContains = a.Get("--title-contains"),
        Contains = a.Get("--contains"),
        Wiql = a.Get("--wiql"),
    };

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
        a.GetAll(option)
            .SelectMany(v => (v ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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

    /// <summary>
    /// ado workitem edit &lt;id&gt;...: sets the fields given as options; others stay unchanged. Several ids, or ids read from
    /// stdin with "-", show what will change and ask first (--yes skips that, --dry-run only shows it).
    /// </summary>
    public static async Task<int> EditAsync(Context ctx)
    {
        var a = ctx.Args;
        if (a.At(2) is null)
            throw AdoException.Usage(EditUsage);
        var fields = Fields(a);
        var find = a.Get("--replace-title");
        if (find is not null)
        {
            if (find.Length == 0)
                throw AdoException.Usage("--replace-title needs the text to replace");
            if (a.Get("--with") is null)
                throw AdoException.Usage("--replace-title needs --with <new text>, e.g. --replace-title abc --with xyz");
            if (fields.ContainsKey("System.Title"))
                throw AdoException.Usage("pass either --title or --replace-title, not both");
        }
        if (fields.Count == 0 && find is null)
            throw AdoException.Usage("nothing to change; pass --title, --replace-title, --description, --state, --assigned-to, --area, --iteration, --tags, --comment or --field Name=value");
        var ids = EditIds(a, Console.In);

        if (ids.Count == 1 && find is null && !a.Has("--dry-run"))
        {
            var w = await ctx.Client.UpdateWorkItemAsync(ids[0], fields);
            Console.Error.WriteLine($"Updated work item #{w.Id}: {w.Field("System.Title")}");
            return ctx.Json ? Output.WriteJson(w) : 0;
        }
        return await BulkEditAsync(ctx, ids, fields, find, a.Get("--with") ?? "");
    }

    const string EditUsage = "usage: ado workitem edit <id>... [--title t] [--replace-title old --with new] [--state s] [--field Name=value] ...; or - to read ids from stdin";

    static async Task<int> BulkEditAsync(Context ctx, List<int> ids, Dictionary<string, string> fields, string? find, string replacement)
    {
        var a = ctx.Args;
        var items = await ctx.Client.GetWorkItemsAsync(ids);
        var failed = 0;
        foreach (var id in ids.Except(items.Select(w => w.Id)))
        {
            Console.Error.WriteLine($"#{id}: not found");
            failed++;
        }

        var changes = Changes(items, fields, find, replacement);
        var dryRun = a.Has("--dry-run");
        if (dryRun && ctx.Json)
            return Output.WriteJson(changes.Select(c => new { c.Item.Id, c.Fields }));

        var preview = dryRun ? Console.Out : Console.Error;
        if (changes.Count == 0)
        {
            Console.Error.WriteLine(find is null ? "No work items to update." : $"No title contains '{find}'; nothing to update.");
            return failed > 0 ? AdoException.General : 0;
        }
        Preview(preview, changes, fields, find is not null);
        if (dryRun)
            return failed > 0 ? AdoException.General : 0;

        if (changes.Count > 1 && !a.Has("--yes") && !a.Has("-y"))
        {
            if (Console.IsInputRedirected)
                throw AdoException.Usage($"pass --yes to update {changes.Count} work items; there is no terminal to confirm on");
            if (!Term.Confirm($"Update {changes.Count} work items? [y/N] "))
            {
                Console.Error.WriteLine("Aborted.");
                return AdoException.General;
            }
        }

        var updated = new List<WorkItem>();
        foreach (var (item, itemFields) in changes)
        {
            try
            {
                var w = await ctx.Client.UpdateWorkItemAsync(item.Id, itemFields);
                Console.Error.WriteLine($"Updated work item #{w.Id}: {w.Field("System.Title")}");
                updated.Add(w);
            }
            catch (AdoException e)
            {
                // One rejected item (an invalid state transition, say) should not stop the rest.
                Console.Error.WriteLine($"#{item.Id}: {e.Message}");
                failed++;
            }
        }
        if (failed > 0)
            Console.Error.WriteLine($"{failed} of {ids.Count} work items were not updated.");
        if (ctx.Json)
            Output.WriteJson(updated);
        return failed > 0 ? AdoException.General : 0;
    }

    /// <summary>
    /// The ids after "workitem edit", in order and without duplicates; "-" reads whitespace-separated ids from
    /// <paramref name="stdin"/>, e.g. from <c>ado workitem list --ids</c>.
    /// </summary>
    public static List<int> EditIds(Args a, TextReader stdin)
    {
        var tokens = new List<string>();
        foreach (var arg in a.Positional.Skip(2))
            tokens.AddRange(arg == "-" ? stdin.ReadToEnd().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : [arg]);
        if (tokens.Count == 0 && !a.Positional.Skip(2).Contains("-"))
            throw AdoException.Usage(EditUsage);
        var ids = new List<int>();
        foreach (var token in tokens)
            ids.Add(int.TryParse(token.TrimStart('#'), out var id) && id > 0 ? id : throw AdoException.Usage($"not a work item id: '{token}'"));
        return ids.Distinct().ToList();
    }

    /// <summary>
    /// What to set on each work item: <paramref name="fields"/>, and with <paramref name="find"/> the title with every
    /// occurrence replaced (ignoring case, like the query's CONTAINS). Items whose title does not contain it are left out.
    /// </summary>
    public static List<(WorkItem Item, Dictionary<string, string> Fields)> Changes(
        IEnumerable<WorkItem> items, IReadOnlyDictionary<string, string> fields, string? find, string replacement)
    {
        var changes = new List<(WorkItem, Dictionary<string, string>)>();
        foreach (var w in items)
        {
            var itemFields = new Dictionary<string, string>(fields);
            if (find is not null)
            {
                var title = w.Field("System.Title");
                var renamed = title.Replace(find, replacement, StringComparison.OrdinalIgnoreCase);
                if (renamed == title)
                    continue;
                itemFields["System.Title"] = renamed;
            }
            changes.Add((w, itemFields));
        }
        return changes;
    }

    static void Preview(TextWriter to, List<(WorkItem Item, Dictionary<string, string> Fields)> changes, Dictionary<string, string> fields, bool renames)
    {
        to.WriteLine(changes.Count == 1 ? "1 work item will be updated:" : $"{changes.Count} work items will be updated:");
        string[] headers = renames ? ["ID", "TYPE", "STATE", "TITLE", "NEW TITLE"] : ["ID", "TYPE", "STATE", "TITLE"];
        Output.Table(headers, changes.Select(c => new[]
        {
            c.Item.Id.ToString(), c.Item.Field("System.WorkItemType"), c.Item.Field("System.State"), c.Item.Field("System.Title"),
            c.Fields.GetValueOrDefault("System.Title", ""),
        }.Take(headers.Length).ToArray()), to);
        foreach (var (name, value) in fields)
            to.WriteLine($"  {name} = {value}");
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
