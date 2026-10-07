namespace AdoCli.Cli;

using System.Net;
using System.Text.RegularExpressions;
using AdoCli.Api;

public static partial class WorkItemCommands
{
    public static async Task<int> ListAsync(Context ctx)
    {
        var items = await ctx.Client.GetMyWorkItemsAsync(ctx.Project, ctx.Limit);
        if (ctx.Json)
            return Output.WriteJson(items);

        Output.Table(["ID", "TYPE", "STATE", "TITLE"], items.Select(w => new[]
        {
            w.Id.ToString(), w.Field("System.WorkItemType"), w.Field("System.State"), w.Field("System.Title"),
        }));
        return 0;
    }

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
