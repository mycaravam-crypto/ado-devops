namespace AdoCli.Cli;

using AdoCli.Api;
using AdoCli.Git;

public static class RepoCommands
{
    public static async Task<int> ListAsync(Context ctx)
    {
        var repos = (await ctx.Client.GetRepositoriesAsync(ctx.Project))
            .OrderBy(r => r.Project.Name).ThenBy(r => r.Name).ToList();
        if (ctx.Json)
            return Output.WriteJson(repos);

        Output.Table(["NAME", "PROJECT", "DEFAULT BRANCH"], repos.Select(r => new[] { r.Name, r.Project.Name, Output.Branch(r.DefaultBranch) }));
        return 0;
    }

    public static async Task<int> ShowAsync(Context ctx)
    {
        var repo = await GetAsync(ctx);
        if (ctx.Json)
            return Output.WriteJson(repo);

        Console.WriteLine($"""
            {repo.Name}

            Project:        {repo.Project.Name}
            Default branch: {Output.Branch(repo.DefaultBranch)}
            Clone URL:      {repo.RemoteUrl}
            Web URL:        {repo.WebUrl}
            """);
        return 0;
    }

    public static async Task<int> CloneAsync(Context ctx)
    {
        var repo = await GetAsync(ctx);
        string[] args = ctx.Args.At(3) is { } dir ? ["clone", repo.RemoteUrl!, dir] : ["clone", repo.RemoteUrl!];
        return GitClient.Run(args, capture: false).ExitCode;
    }

    /// <summary>What the origin remote says, without calling the server. SSH remotes carry no server URL and are assumed to be the configured one.</summary>
    public static Task<int> StatusAsync(Context ctx)
    {
        var remote = ctx.Remote ?? throw new AdoException("not inside a clone of an Azure DevOps repository (no matching origin remote)");
        var configured = ctx.Config.Server is { } s && (remote.Server is null || string.Equals(remote.Server, s.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        var server = remote.Server ?? ctx.Config.Server;
        if (ctx.Json)
            return Task.FromResult(Output.WriteJson(new { server, project = remote.Project, repository = remote.Repo, configured }));

        Console.WriteLine($"""
            Server:     {server}
            Project:    {remote.Project}
            Repository: {remote.Repo}
            """);
        if (!configured)
            Console.Error.WriteLine("\nWarning: this is not the configured server; run 'ado auth login <server-url>' for it.");
        return Task.FromResult(0);
    }

    static Task<Repository> GetAsync(Context ctx) =>
        ctx.Client.GetRepositoryAsync(ctx.RequireProject(), ctx.Args.At(2) ?? ctx.RequireRepo());
}
