namespace AdoCli.Cli;

using AdoCli.Api;
using AdoCli.Git;

public static class PrCommands
{
    public static async Task<int> ListAsync(Context ctx)
    {
        Guid? creator = ctx.Args.Has("--mine") ? (await ctx.Client.GetConnectionDataAsync()).AuthenticatedUser.Id : null;
        var prs = await ctx.Client.GetPullRequestsAsync(ctx.RequireProject(), ctx.Repo, ctx.Args.Get("--status") ?? "active", creator);
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
        Console.WriteLine($"Changes:    {(await ctx.Client.GetPullRequestChangesAsync(pr)).Count} files");
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

    /// <summary>Everything needed to understand a PR, as one JSON document. Always JSON.</summary>
    public static async Task<int> ContextAsync(Context ctx)
    {
        var pr = await ctx.Client.GetPullRequestAsync(Id(ctx, "context"));
        var commits = ctx.Client.GetPullRequestCommitsAsync(pr);
        var changes = ctx.Client.GetPullRequestChangesAsync(pr);
        var workItems = ctx.Client.GetWorkItemsAsync(await ctx.Client.GetPullRequestWorkItemIdsAsync(pr));
        return Output.WriteJson(new { pullRequest = pr, commits = await commits, changes = await changes, workItems = await workItems });
    }

    public static async Task<int> DiffAsync(Context ctx)
    {
        if (ctx.Json)
            return Output.WriteJson(await ctx.Client.GetPullRequestChangesAsync(await ctx.Client.GetPullRequestAsync(Id(ctx, "diff"))));

        var pr = await GetInCurrentRepoAsync(ctx, "diff");
        if (pr.LastMergeSourceCommit is null || pr.LastMergeTargetCommit is null)
            throw new AdoException($"PR #{pr.PullRequestId} has no merge commits to compare yet");

        GitClient.Check("fetch", "--quiet", "origin", pr.TargetRefName, pr.SourceRefName);
        return GitClient.Run(["diff", $"{pr.LastMergeTargetCommit.CommitId}...{pr.LastMergeSourceCommit.CommitId}"], capture: false).ExitCode;
    }

    public static async Task<int> CheckoutAsync(Context ctx)
    {
        var pr = await GetInCurrentRepoAsync(ctx, "checkout");
        if (GitClient.Check("status", "--porcelain", "--untracked-files=no").Length > 0)
            throw new AdoException("you have uncommitted changes; commit or stash them first");

        var branch = $"pr/{pr.PullRequestId}";
        var exists = GitClient.Run(["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"]).ExitCode == 0;
        foreach (var args in CheckoutCommands(branch, pr.SourceRefName, exists))
            GitClient.Check(args);
        Console.WriteLine($"Switched to branch {branch} ({Output.Branch(pr.SourceRefName)})");
        return 0;
    }

    public static async Task<int> CreateAsync(Context ctx)
    {
        var (project, repo) = (ctx.RequireProject(), ctx.RequireRepo());
        var a = ctx.Args;
        string title, description, source, target;
        if (a.Get("--title") is { } t)
        {
            title = t;
            description = a.Get("--description") ?? "";
            source = a.Get("--source") ?? CurrentBranch();
            target = a.Get("--target") ?? await DefaultBranchAsync(ctx, project, repo);
        }
        else
        {
            if (Console.IsInputRedirected)
                throw AdoException.Usage("--title is required when not running interactively");
            title = Term.Prompt("Title");
            description = Term.Prompt("Description", "");
            source = Term.Prompt("Source branch", a.Get("--source") ?? CurrentBranch());
            target = Term.Prompt("Target branch", a.Get("--target") ?? await DefaultBranchAsync(ctx, project, repo));
        }
        if (title.Length == 0)
            throw AdoException.Usage("a title is required");

        var pr = await ctx.Client.CreatePullRequestAsync(project, repo, Output.Ref(source), Output.Ref(target), title, description);
        if (ctx.Json)
            return Output.WriteJson(pr);
        Console.WriteLine($"Created PR #{pr.PullRequestId}: {pr.Title}");
        return 0;
    }

    public static async Task<int> ApproveAsync(Context ctx)
    {
        var pr = await ctx.Client.GetPullRequestAsync(Id(ctx, "approve"));
        var me = (await ctx.Client.GetConnectionDataAsync()).AuthenticatedUser.Id;
        await ctx.Client.VoteAsync(pr, me, 10);
        Console.WriteLine($"Approved PR #{pr.PullRequestId}: {pr.Title}");
        return 0;
    }

    public static async Task<int> MergeAsync(Context ctx)
    {
        var pr = await ctx.Client.GetPullRequestAsync(Id(ctx, "merge"));
        if (pr.Status != "active")
            throw new AdoException($"PR #{pr.PullRequestId} is {pr.Status}, not active");

        Console.WriteLine($"""
            PR #{pr.PullRequestId}
            {pr.Title}

            Target: {Output.Branch(pr.TargetRefName)}
            Source: {Output.Branch(pr.SourceRefName)}

            """);
        if (!ctx.Args.Has("--yes") && !ctx.Args.Has("-y") && !Term.Confirm(ctx.Args.Has("--squash") ? "Squash merge? [y/N] " : "Merge? [y/N] "))
        {
            Console.Error.WriteLine("Aborted.");
            return AdoException.General;
        }

        var result = await ctx.Client.CompletePullRequestAsync(pr, ctx.Args.Has("--squash"));
        Console.WriteLine(result.Status == "completed"
            ? $"Merged PR #{pr.PullRequestId}"
            : $"Completion requested for PR #{pr.PullRequestId}; it merges once policies pass and there are no conflicts");
        return 0;
    }

    static string CurrentBranch() => GitClient.Check("rev-parse", "--abbrev-ref", "HEAD");

    static async Task<string> DefaultBranchAsync(Context ctx, string project, string repo) =>
        Output.Branch((await ctx.Client.GetRepositoryAsync(project, repo)).DefaultBranch) is { Length: > 0 } b
            ? b
            : throw AdoException.Usage("repository has no default branch; pass --target <branch>");

    /// <summary>An existing local branch is only fast-forwarded, so local commits on it are never lost.</summary>
    public static string[][] CheckoutCommands(string branch, string sourceRef, bool branchExists) => branchExists
        ? [["fetch", "--quiet", "origin", sourceRef], ["checkout", "--quiet", branch], ["merge", "--quiet", "--ff-only", "FETCH_HEAD"]]
        : [["fetch", "--quiet", "origin", sourceRef], ["checkout", "--quiet", "-b", branch, "FETCH_HEAD"]];

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
