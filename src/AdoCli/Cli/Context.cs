namespace AdoCli.Cli;

using AdoCli.Api;
using AdoCli.Git;

/// <summary>What a command needs: arguments, configuration, git context and a lazily created client.</summary>
public sealed class Context(Args args)
{
    AdoClient? _client;
    Lazy<Remote?>? _remote;

    public Args Args => args;

    public Config Config { get; } = Config.Load();

    public bool Json => args.Has("--json");

    /// <summary>The origin remote of the current git repository, when it points at Azure DevOps.</summary>
    public Remote? Remote => (_remote ??= new(() => GitClient.DetectRemote(Config.Server))).Value;

    public string? Server => Config.Server ?? Remote?.Server;

    /// <summary>--project, else the current repository's project, else ADO_PROJECT / config.</summary>
    public string? Project => args.Get("--project") ?? Remote?.Project ?? Config.Project;

    public string? Repo => args.Get("--repo") ?? Remote?.Repo;

    public string RequireProject() => Project ??
        throw AdoException.Usage("could not determine the project; run inside a cloned repository or pass --project <name>");

    public string RequireRepo() => Repo ??
        throw AdoException.Usage("could not determine the repository; run inside a cloned repository or pass --repo <name>");

    /// <summary>The numeric argument after the subcommand, e.g. 142 in "ado pr show 142".</summary>
    public int Id(string usage) =>
        int.TryParse(args.At(2), out var id) ? id : throw AdoException.Usage($"usage: ado {usage}");

    public AdoClient Client => _client ??= Server is { } server && Config.Pat is { } pat
        ? new AdoClient(server, pat, args.Has("--debug"), apiVersion: Config.ApiVersion ?? AdoClient.DefaultApiVersion)
        : throw new AdoException("not logged in.\n\nRun:\n  ado auth login <server-url>", AdoException.Auth);
}
