namespace AdoCli.Cli;

using AdoCli.Api;
using AdoCli.Git;

public static class PrCommands
{
    public static async Task<int> ListAsync(Context ctx)
    {
        Guid? creator = ctx.Args.Has("--mine") ? (await ctx.Client.GetConnectionDataAsync()).AuthenticatedUser.Id : null;
        var repo = ctx.Repo;
        var project = repo is null ? ctx.Project : ctx.RequireProject();
        var prs = await ctx.Client.GetPullRequestsAsync(project, repo, ctx.Args.Get("--status") ?? "active", creator);
        if (ctx.Json)
            return Output.WriteJson(prs);

        Output.Table(["ID", "TITLE", "AUTHOR", "STATUS"], prs.Select(p => new[]
        {
            p.PullRequestId.ToString(), Output.Truncate(p.Title, 50), p.CreatedBy.DisplayName, Output.Status(p.Status),
        }));
        return 0;
    }

    public static async Task<int> ShowAsync(Context ctx)
    {
        var pr = await ctx.Client.GetPullRequestAsync(Id(ctx, "show"));
        if (ctx.Json)
            return Output.WriteJson(pr);

        Console.WriteLine($"""
            PR #{pr.PullRequestId}
            {pr.Title}

            Author:     {pr.CreatedBy.DisplayName}
            Status:     {Output.Status(pr.Status)}
            Repository: {pr.Repository.Name}
            Target:     {Output.Branch(pr.TargetRefName)}
            Source:     {Output.Branch(pr.SourceRefName)}
            """);
        if (!string.IsNullOrWhiteSpace(pr.Description))
            Console.WriteLine($"\nDescription:\n{pr.Description}");
        if (pr.Reviewers is { Count: > 0 } reviewers)
        {
            Console.WriteLine("\nReviewers:");
            foreach (var r in reviewers)
                Console.WriteLine($"  {Vote(r.Vote)} {r.DisplayName}");
        }
        return 0;
    }

    public static async Task<int> DiffAsync(Context ctx)
    {
        var pr = await GetInCurrentRepoAsync(ctx, "diff");
        if (pr.LastMergeSourceCommit is null || pr.LastMergeTargetCommit is null)
            throw new AdoException($"PR #{pr.PullRequestId} has no merge commits to compare yet");

        GitClient.Check("fetch", "--quiet", "origin", pr.TargetRefName, pr.SourceRefName);
        return GitClient.Run(["diff", $"{pr.LastMergeTargetCommit.CommitId}...{pr.LastMergeSourceCommit.CommitId}"], capture: false).ExitCode;
    }

    /// <summary>Loads a PR and checks that the current directory is a clone of its repository.</summary>
    static async Task<PullRequest> GetInCurrentRepoAsync(Context ctx, string verb)
    {
        var pr = await ctx.Client.GetPullRequestAsync(Id(ctx, verb));
        if (!string.Equals(ctx.Remote?.Repo, pr.Repository.Name, StringComparison.OrdinalIgnoreCase))
            throw AdoException.Usage($"PR #{pr.PullRequestId} belongs to repository '{pr.Repository.Name}'; run this inside a clone of it");
        return pr;
    }

    static int Id(Context ctx, string verb) =>
        int.TryParse(ctx.Args.At(2), out var id) ? id : throw AdoException.Usage($"usage: ado pr {verb} <id>");

    static string Vote(int vote) => vote switch
    {
        > 0 => "✓", // approved (10) or approved with suggestions (5)
        <= -10 => "✗",
        < 0 => "…", // waiting for author
        _ => "?",
    };
}
