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
        if (PlainText(w.Field("System.Description")) is { Length: > 0 } description)
            Console.WriteLine($"\nDescription:\n{description}");
        return 0;
    }

    /// <summary>Descriptions are HTML; line breaks are kept, other markup dropped.</summary>
    public static string PlainText(string html) =>
        WebUtility.HtmlDecode(Tag().Replace(LineBreak().Replace(html, "\n"), "")).Trim();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|li|h\d)>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();
}
