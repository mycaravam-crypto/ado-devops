namespace AdoCli.Cli;

using AdoCli.Api;

/// <summary>
/// ado workitem template list|show|create|edit|delete: the work item templates of a team (--team, by default the
/// project's default team). <c>ado workitem create --template</c> applies one.
/// </summary>
public static class WorkItemTemplateCommands
{
    public static Task<int> RunAsync(Context ctx) => ctx.Args.At(2) switch
    {
        "list" => ListAsync(ctx),
        "show" => ShowAsync(ctx),
        "create" => CreateAsync(ctx),
        "edit" => EditAsync(ctx),
        "delete" => DeleteAsync(ctx),
        var sub => throw AdoException.Usage($"unknown command 'workitem template{(sub is null ? "" : " " + sub)}'; use list, show, create, edit or delete. Run 'ado workitem --help'."),
    };

    /// <summary>ado workitem template list [--type T]: name, work item type, id and description of each template.</summary>
    public static async Task<int> ListAsync(Context ctx)
    {
        var (project, team) = await TeamAsync(ctx);
        var templates = (await ctx.Client.GetWorkItemTemplatesAsync(project, team, ctx.Args.Get("--type")))
            .OrderBy(t => t.WorkItemTypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ctx.Json)
            return Output.WriteJson(templates);

        Output.Table(["NAME", "TYPE", "ID", "DESCRIPTION"], templates.Select(t => new[] { t.Name, t.WorkItemTypeName, t.Id.ToString(), OneLine(t.Description) }));
        Console.WriteLine();
        Console.WriteLine($"{(templates.Count == 1 ? "1 template" : $"{templates.Count} templates")} of team {team}");
        return 0;
    }

    /// <summary>ado workitem template show &lt;name|id&gt;: the template and every field value it sets.</summary>
    public static async Task<int> ShowAsync(Context ctx)
    {
        var (project, team) = await TeamAsync(ctx);
        var t = await ResolveAsync(ctx.Client, project, team, Target(ctx, "show"));
        if (ctx.Json)
            return Output.WriteJson(t);

        Console.WriteLine($"""
            {t.Name}

            Type:        {t.WorkItemTypeName}
            Id:          {t.Id}
            Team:        {team}
            """);
        if (!string.IsNullOrWhiteSpace(t.Description))
            Console.WriteLine($"Description: {OneLine(t.Description)}");
        var (rows, blocks) = FieldDisplay(t.Fields ?? []);
        Console.WriteLine();
        if (rows.Count == 0 && blocks.Count == 0)
            Console.WriteLine("No field values.");
        if (rows.Count > 0)
            Output.Table(["FIELD", "VALUE"], rows);
        foreach (var (name, text) in blocks)
            Console.WriteLine($"\n{name}:\n{text}");
        return 0;
    }

    /// <summary>ado workitem template create --name N --type T [field options]: a new template with the given field values.</summary>
    public static async Task<int> CreateAsync(Context ctx)
    {
        var a = ctx.Args;
        var name = a.Get("--name") is { Length: > 0 } n ? n : throw AdoException.Usage("--name is required, e.g. --name \"Standard story\"");
        var type = a.Get("--type") is { Length: > 0 } ty ? ty : throw AdoException.Usage("--type is required, e.g. --type \"User Story\" or --type \"Test Case\"");
        var fields = WorkItemCommands.Fields(a);
        if (fields.Count == 0)
            throw AdoException.Usage("a template needs at least one field; pass --title, --description, --state, --assigned-to, --area, --iteration, --tags or --field Name=value");

        var (project, team) = await TeamAsync(ctx);
        var created = await ctx.Client.CreateWorkItemTemplateAsync(project, team,
            new WorkItemTemplate(Guid.Empty, name, a.Get("--template-description"), type, fields));
        if (ctx.Json)
            return Output.WriteJson(created);
        Console.WriteLine($"Created {created.WorkItemTypeName} template '{created.Name}' ({created.Id}) for team {team}");
        return 0;
    }

    /// <summary>
    /// ado workitem template edit &lt;name|id&gt;: changes name, description or field values; fields not mentioned are kept,
    /// --remove-field drops one. The server only replaces whole templates, so the current one is read first.
    /// </summary>
    public static async Task<int> EditAsync(Context ctx)
    {
        var a = ctx.Args;
        var target = Target(ctx, "edit");
        var set = WorkItemCommands.Fields(a);
        var remove = WorkItemCommands.Values(a, "--remove-field");
        if (set.Count == 0 && remove.Count == 0 && a.Get("--name") is null && a.Get("--template-description") is null)
            throw AdoException.Usage("nothing to change; pass --name, --template-description, --remove-field Name or field options such as --title or --field Name=value");

        var (project, team) = await TeamAsync(ctx);
        var current = await ResolveAsync(ctx.Client, project, team, target);
        var updated = Edited(current, a.Get("--name"), a.Get("--template-description"), set, remove);
        var saved = await ctx.Client.ReplaceWorkItemTemplateAsync(project, team, updated);
        if (ctx.Json)
            return Output.WriteJson(saved);
        Console.WriteLine($"Updated {saved.WorkItemTypeName} template '{saved.Name}' ({saved.Id})");
        return 0;
    }

    /// <summary>The template with the changes applied; field names match ignoring case, as the server treats them.</summary>
    public static WorkItemTemplate Edited(WorkItemTemplate current, string? name, string? description,
        IReadOnlyDictionary<string, string> set, IReadOnlyCollection<string> remove)
    {
        var fields = new Dictionary<string, string>(current.Fields ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var field in remove)
            if (!fields.Remove(field))
                throw AdoException.Usage($"template '{current.Name}' sets no field '{field}'");
        foreach (var (field, value) in set)
            fields[field] = value;
        if (name is { Length: 0 })
            throw AdoException.Usage("--name must not be empty");
        return current with
        {
            Name = name ?? current.Name,
            Description = description ?? current.Description,
            Fields = new Dictionary<string, string>(fields),
        };
    }

    /// <summary>ado workitem template delete &lt;name|id&gt;: asks first unless --yes.</summary>
    public static async Task<int> DeleteAsync(Context ctx)
    {
        var target = Target(ctx, "delete");
        var (project, team) = await TeamAsync(ctx);
        var t = await ResolveAsync(ctx.Client, project, team, target, withFields: false);
        if (!ctx.Args.Has("--yes") && !ctx.Args.Has("-y"))
        {
            if (Console.IsInputRedirected)
                throw AdoException.Usage("pass --yes to delete the template; there is no terminal to confirm on");
            if (!Term.Confirm($"Delete {t.WorkItemTypeName} template '{t.Name}' of team {team}? [y/N] "))
            {
                Console.Error.WriteLine("Aborted.");
                return AdoException.General;
            }
        }
        await ctx.Client.DeleteWorkItemTemplateAsync(project, team, t.Id);
        Console.Error.WriteLine($"Deleted {t.WorkItemTypeName} template '{t.Name}' ({t.Id})");
        return 0;
    }

    /// <summary>The project and the team whose templates to use: --team, else the project's default team.</summary>
    public static async Task<(string Project, string Team)> TeamAsync(Context ctx)
    {
        var project = ctx.RequireProject();
        if (ctx.Args.Get("--team") is { Length: > 0 } team)
            return (project, team);
        var defaultTeam = (await ctx.Client.GetProjectAsync(project)).DefaultTeam
            ?? throw AdoException.Usage($"project {project} has no default team; pass --team <name>");
        return (project, defaultTeam.Name);
    }

    /// <summary>
    /// A template by id, or by name ignoring case; with <paramref name="withFields"/> it is read again to get its field values,
    /// which lists leave out. Two templates may share a name, then only the id is unambiguous.
    /// </summary>
    public static async Task<WorkItemTemplate> ResolveAsync(AdoClient client, string project, string team, string nameOrId, bool withFields = true)
    {
        if (Guid.TryParse(nameOrId, out var id))
            return await client.GetWorkItemTemplateAsync(project, team, id);

        var matches = (await client.GetWorkItemTemplatesAsync(project, team))
            .Where(t => t.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            0 => throw new AdoException($"no work item template named '{nameOrId}' in team {team}; see 'ado workitem template list'", AdoException.NotFound),
            1 => withFields ? await client.GetWorkItemTemplateAsync(project, team, matches[0].Id) : matches[0],
            _ => throw AdoException.Usage($"{matches.Count} templates of team {team} are named '{nameOrId}'; pass one of their ids: " +
                string.Join(", ", matches.Select(t => $"{t.Id} ({t.WorkItemTypeName})"))),
        };
    }

    /// <summary>Field values for display, sorted by reference name: one-line values as rows, HTML or multi-line ones as text blocks.</summary>
    public static (List<string[]> Rows, List<(string Name, string Text)> Blocks) FieldDisplay(IReadOnlyDictionary<string, string> fields)
    {
        var rows = new List<string[]>();
        var blocks = new List<(string, string)>();
        foreach (var (name, raw) in fields.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            // A JSON null deserializes to a null string despite the declared type.
            var value = raw ?? "";
            var html = value.Contains('<') && value.Contains('>');
            var text = html ? WorkItemCommands.PlainText(value) : value.Trim();
            if (html || text.Contains('\n'))
                blocks.Add((name, text));
            else
                rows.Add([name, text]);
        }
        return (rows, blocks);
    }

    static string Target(Context ctx, string sub) =>
        ctx.Args.At(3) ?? throw AdoException.Usage($"usage: ado workitem template {sub} <name|id>");

    static string OneLine(string? text) => (text ?? "").ReplaceLineEndings(" ").Trim();
}
