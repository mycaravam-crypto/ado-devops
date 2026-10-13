namespace AdoCli.Cli;

using System.Text.RegularExpressions;
using AdoCli.Api;
using AdoCli.Git;

/// <summary>The JSON document of <c>ado commit context</c>, schema <see cref="CommitCommands.Schema"/>.</summary>
public sealed record CommitContext(
    string Schema,
    string Server,
    string Project,
    CommitRepository Repository,
    CommitInfo Commit,
    CommitWorkItems WorkItems,
    List<string> Warnings);

/// <summary>The repository the commit was looked up in.</summary>
public sealed record CommitRepository(Guid Id, string Name);

/// <summary>The commit itself; <see cref="Comment"/> is the full message.</summary>
public sealed record CommitInfo(string CommitId, List<string> Parents, GitUser? Author, GitUser? Committer, string Comment);

/// <summary>
/// Status "verified": <see cref="Items"/> are all links the server reported, possibly none.
/// Status "unavailable": the server did not say, so <see cref="Items"/> is null; unknown is never reported as none.
/// </summary>
public sealed record CommitWorkItems(string Status, List<CommitWorkItem>? Items);

/// <summary>An explicitly linked work item; <see cref="Source"/> names where the link comes from.</summary>
public sealed record CommitWorkItem(int Id, string Source, string? Url, string WebUrl);

public static partial class CommitCommands
{
    public const string Schema = "ado.commit-context/v1";

    /// <summary>ado commit context &lt;commit&gt;: a commit and the work items explicitly linked to it, as JSON. Read-only: GET requests only.</summary>
    public static async Task<int> ContextAsync(Context ctx)
    {
        var (project, repo) = (ctx.RequireProject(), ctx.RequireRepo());
        var sha = ResolveCommit(ctx, project, repo);
        var repository = await ctx.Client.GetRepositoryAsync(project, repo);
        var commit = await ctx.Client.GetCommitAsync(project, repo, sha);
        var linked = await ctx.Client.GetCommitWorkItemsAsync(project, repo, commit.CommitId);
        return Output.WriteJson(Build(ctx.Client.Server, repository, commit, linked));
    }

    /// <summary>
    /// The context document. Links count as verified only when the work item query returned the same commit with a
    /// workItems array; anything else is "unavailable" with a warning that says why.
    /// </summary>
    public static CommitContext Build(string server, Repository repository, GitCommit commit, GitCommit? linked)
    {
        var warnings = new List<string>();
        List<CommitWorkItem>? items = null;
        if (linked is null)
            warnings.Add("work item links unavailable: the server returned no commit for the work item query");
        else if (!string.Equals(linked.CommitId, commit.CommitId, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"work item links unavailable: the work item query returned commit {linked.CommitId} instead of {commit.CommitId}");
        else if (linked.WorkItems is null)
            warnings.Add("work item links unavailable: the server did not report work items for the commit (searchCriteria.includeWorkItems not supported?)");
        else
        {
            items = [];
            foreach (var group in linked.WorkItems.GroupBy(w => w.Id))
            {
                if (int.TryParse(group.Key, out var id))
                    items.Add(new(id, "commit-workitem", group.First().Url, $"{server}/_workitems/edit/{id}"));
                else
                    warnings.Add($"ignored a work item link with an invalid id '{group.Key}'");
            }
            items.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        return new(
            Schema,
            server,
            repository.Project.Name,
            new(repository.Id, repository.Name),
            new(commit.CommitId, commit.Parents ?? [], commit.Author, commit.Committer, commit.Comment ?? ""),
            new(items is null ? "unavailable" : "verified", items),
            warnings);
    }

    /// <summary>
    /// The full SHA to look up: a 40-digit SHA as given; anything else (short SHA, HEAD, branch) only inside a clone of the
    /// same repository, through git rev-parse. The commit is never searched for in other repositories.
    /// </summary>
    public static string ResolveCommit(Context ctx, string project, string repo)
    {
        var arg = ctx.Args.At(2) ?? throw AdoException.Usage("usage: ado commit context <commit>");
        if (FullSha().IsMatch(arg))
            return arg.ToLowerInvariant();

        if (ctx.Remote is not { } remote
            || !string.Equals(remote.Repo, repo, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(remote.Project, project, StringComparison.OrdinalIgnoreCase))
            throw AdoException.Usage($"'{arg}' is not a full 40-character commit SHA; pass the full SHA, or run inside a clone of '{repo}' to resolve it with git");

        var r = GitClient.Run(["rev-parse", "--verify", "--quiet", "--end-of-options", arg + "^{commit}"]);
        return r.ExitCode == 0
            ? r.Stdout.Trim()
            : throw new AdoException($"'{arg}' is not a commit in this clone; fetch it or pass the full SHA", AdoException.NotFound);
    }

    [GeneratedRegex("^[0-9a-fA-F]{40}$")]
    private static partial Regex FullSha();
}
