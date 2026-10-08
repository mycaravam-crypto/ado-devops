namespace AdoCli.Cli;

using System.Diagnostics;
using AdoCli.Api;

public static class BuildCommands
{
    /// <summary>ado build list: builds of the current project, most recently queued first; all unless --limit is given.</summary>
    public static async Task<int> ListAsync(Context ctx)
    {
        var sw = Stopwatch.StartNew();
        var builds = await ctx.Client.GetBuildsAsync(ctx.RequireProject(), ctx.Limit);
        if (ctx.Json)
            return Output.WriteJson(builds);

        Output.Table(["ID", "DEFINITION", "BRANCH", "STATUS"], builds.Select(b => new[]
        {
            b.Id.ToString(), b.Definition.Name, Output.Branch(b.SourceBranch), Output.Status(b.Result ?? b.Status),
        }));
        Output.WriteSummary(builds.Count, "build", ctx.Limit, [], sw.Elapsed);
        return 0;
    }

    /// <summary>ado build show &lt;id&gt;: one build of the current project.</summary>
    public static async Task<int> ShowAsync(Context ctx)
    {
        var b = await ctx.Client.GetBuildAsync(ctx.RequireProject(), ctx.Id("build show <id>"));
        if (ctx.Json)
            return Output.WriteJson(b);

        Console.WriteLine($"""
            Build {b.BuildNumber} (#{b.Id})

            Definition: {b.Definition.Name}
            Branch:     {Output.Branch(b.SourceBranch)}
            Status:     {Output.Status(b.Result ?? b.Status)}
            Requested:  {b.RequestedFor?.DisplayName}
            URL:        {b.Links?.Web?.Href}
            """);
        return 0;
    }

    /// <summary>ado build run &lt;definition-id&gt;: queues a build, of --branch or the definition's default branch.</summary>
    public static async Task<int> RunAsync(Context ctx)
    {
        var branch = ctx.Args.Get("--branch") is { } name ? Output.Ref(name) : null;
        var b = await ctx.Client.QueueBuildAsync(ctx.RequireProject(), ctx.Id("build run <definition-id>"), branch);
        if (ctx.Json)
            return Output.WriteJson(b);

        Console.WriteLine($"Queued build #{b.Id} of {b.Definition.Name} ({Output.Branch(b.SourceBranch)})");
        if (b.Links?.Web?.Href is { } url)
            Console.WriteLine(url);
        return 0;
    }
}
