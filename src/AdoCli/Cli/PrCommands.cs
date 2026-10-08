namespace AdoCli.Cli;

using System.Diagnostics;
using AdoCli.Api;
using AdoCli.Git;

public static class PrCommands
{
    /// <summary>ado pr list: PRs of the repository (or project), active unless --status says otherwise; --mine keeps your own, --limit caps the count.</summary>
    public static async Task<int> ListAsync(Context ctx)
    {
        var sw = Stopwatch.StartNew();
        var mine = ctx.Args.Has("--mine");
        var status = ctx.Args.Get("--status") ?? "active";
        Guid? creator = mine ? (await ctx.Client.GetConnectionDataAsync()).AuthenticatedUser.Id : null;
        var prs = await ctx.Client.GetPullRequestsAsync(ctx.RequireProject(), ctx.Repo, status, creator, ctx.Limit);
        if (ctx.Json)
            return Output.WriteJson(prs);

        Output.Table(["ID", "TITLE", "AUTHOR", "TARGET", "STATUS"], prs.Select(ListRow));
        Output.WriteSummary(prs.Count, "pull request", ctx.Limit, Describe(status, mine, ctx.Repo), sw.Elapsed);
        return 0;
    }

    /// <summary>The query of <c>pr list</c> in words, for the summary line.</summary>
    public static List<string> Describe(string status, bool mine, string? repo)
    {
        var parts = new List<string> { status == "all" ? "any status" : status };
        if (mine)
            parts.Add("created by you");
        parts.Add(repo is null ? "all repositories" : $"repository {repo}");
        return parts;
    }

    /// <summary>One row of <c>pr list</c>; status stays last so its color codes never skew the padding.</summary>
    public static string[] ListRow(PullRequest p) =>
    [
        p.PullRequestId.ToString(),
        Output.Truncate(p.Title, 50),
        p.CreatedBy.DisplayName,
        Output.Branch(p.TargetRefName),
        Output.Status(p.Status),
    ];

    /// <summary>ado pr show &lt;id&gt;: PR details with changed file count, description and reviewer votes.</summary>
    public static async Task<int> ShowAsync(Context ctx)
    {
        var pr = await ctx.Client.GetPullRequestAsync(ctx.Id("pr show <id>"));
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
        var pr = await ctx.Client.GetPullRequestAsync(ctx.Id("pr context <id>"));
        var commits = ctx.Client.GetPullRequestCommitsAsync(pr);
        var changes = ctx.Client.GetPullRequestChangesAsync(pr);
        var workItems = ctx.Client.GetWorkItemsAsync(await ctx.Client.GetPullRequestWorkItemIdsAsync(pr));
        return Output.WriteJson(new { pullRequest = pr, commits = await commits, changes = await changes, workItems = await workItems });
    }

    /// <summary>ado pr diff &lt;id&gt;: git diff target...source in a clone of the PR's repository; with --json the changed files from the server.</summary>
    public static async Task<int> DiffAsync(Context ctx)
    {
        if (ctx.Json)
            return Output.WriteJson(await ctx.Client.GetPullRequestChangesAsync(await ctx.Client.GetPullRequestAsync(ctx.Id("pr diff <id>"))));

        var pr = await GetInCurrentRepoAsync(ctx, "pr diff <id>");
        if (pr.LastMergeSourceCommit is null || pr.LastMergeTargetCommit is null)
            throw new AdoException($"PR #{pr.PullRequestId} has no merge commits to compare yet");

        GitClient.Check([.. ctx.GitConfig(), "fetch", "--quiet", "origin", pr.TargetRefName, pr.SourceRefName]);
        return GitClient.Passthrough("diff", $"{pr.LastMergeTargetCommit.CommitId}...{pr.LastMergeSourceCommit.CommitId}");
    }

    public static async Task<int> CheckoutAsync(Context ctx)
    {
        var pr = await GetInCurrentRepoAsync(ctx, "pr checkout <id>");
        if (GitClient.Check("status", "--porcelain", "--untracked-files=no").Length > 0)
            throw new AdoException("you have uncommitted changes; commit or stash them first");

        var branch = $"pr/{pr.PullRequestId}";
        var exists = GitClient.Run(["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"]).ExitCode == 0;
        foreach (var args in CheckoutCommands(branch, pr.SourceRefName, exists))
            GitClient.Check([.. ctx.GitConfig(), .. args]);
        Console.Error.WriteLine($"Switched to branch {branch} ({Output.Branch(pr.SourceRefName)})");
        return 0;
    }

    /// <summary>ado pr create: from --title/--description/--source/--target, or prompts when interactive; source defaults to the current branch, target to the default branch.</summary>
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
        var pr = await ctx.Client.GetPullRequestAsync(ctx.Id("pr approve <id>"));
        var me = (await ctx.Client.GetConnectionDataAsync()).AuthenticatedUser.Id;
        var reviewer = await ctx.Client.VoteAsync(pr, me, 10);
        Console.Error.WriteLine($"Approved PR #{pr.PullRequestId}: {pr.Title}");
        return ctx.Json ? Output.WriteJson(reviewer) : 0;
    }

    /// <summary>ado pr merge &lt;id&gt;: completes an active PR after confirmation (--yes skips it); the server may only queue completion until policies pass.</summary>
    public static async Task<int> MergeAsync(Context ctx)
    {
        var pr = await ctx.Client.GetPullRequestAsync(ctx.Id("pr merge <id>"));
        if (pr.Status != "active")
            throw new AdoException($"PR #{pr.PullRequestId} is {pr.Status}, not active");

        Console.Error.WriteLine($"""
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
        Console.Error.WriteLine(result.Status == "completed"
            ? $"Merged PR #{pr.PullRequestId}"
            : $"Completion requested for PR #{pr.PullRequestId}; it merges once policies pass and there are no conflicts");
        return ctx.Json ? Output.WriteJson(result) : 0;
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
    static async Task<PullRequest> GetInCurrentRepoAsync(Context ctx, string usage)
    {
        var pr = await ctx.Client.GetPullRequestAsync(ctx.Id(usage));
        if (!string.Equals(ctx.Remote?.Repo, pr.Repository.Name, StringComparison.OrdinalIgnoreCase))
            throw AdoException.Usage($"PR #{pr.PullRequestId} belongs to repository '{pr.Repository.Name}'; run this inside a clone of it");
        return pr;
    }

    static string Vote(int vote) => vote switch
    {
        > 0 => "✓", // approved (10) or approved with suggestions (5)
        <= -10 => "✗",
        < 0 => "…", // waiting for author
        _ => "?",
    };
}
