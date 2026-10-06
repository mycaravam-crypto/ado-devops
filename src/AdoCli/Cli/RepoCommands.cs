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
        string[] args = ctx.Args.At(3) is { } dir ? ["clone", repo.RemoteUrl, dir] : ["clone", repo.RemoteUrl];
        return GitClient.Run(args, capture: false).ExitCode;
    }

    static Task<Repository> GetAsync(Context ctx) =>
        ctx.Client.GetRepositoryAsync(ctx.RequireProject(), ctx.Args.At(2) ?? ctx.RequireRepo());
}
