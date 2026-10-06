namespace AdoCli.Cli;

using AdoCli.Api;

public static class BuildCommands
{
    public static async Task<int> ListAsync(Context ctx)
    {
        var builds = await ctx.Client.GetBuildsAsync(ctx.RequireProject());
        if (ctx.Json)
            return Output.WriteJson(builds);

        Output.Table(["ID", "DEFINITION", "BRANCH", "STATUS"], builds.Select(b => new[]
        {
            b.Id.ToString(), b.Definition.Name, Output.Branch(b.SourceBranch), Output.Status(b.Result ?? b.Status),
        }));
        return 0;
    }

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
