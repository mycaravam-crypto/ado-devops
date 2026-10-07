namespace AdoCli.Cli;

using AdoCli.Api;
using AdoCli.Git;

/// <summary>A setting in effect and where it came from: a flag, an environment variable, "config file", "git remote", "default" or "not set".</summary>
public sealed record Setting(string Key, string? Value, string Source);

/// <summary>What a command needs: arguments, configuration, git context and a lazily created client.</summary>
public sealed class Context(Args args, Config? config = null)
{
    AdoClient? _client;
    Lazy<Remote?>? _remote;

    public Args Args => args;

    public Config Config { get; } = config ?? Config.Load();

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

    /// <summary>--insecure is true, --insecure=false turns a configured insecure mode off; null when not given.</summary>
    public bool? InsecureFlag => !args.Has("--insecure") ? null : args.Get("--insecure") is { } v ? Config.ParseBool("--insecure", v) : true;

    /// <summary>Effective TLS options; a CA file of "none" (e.g. --ca-cert none) switches a configured one off.</summary>
    public Tls Tls => new(
        InsecureFlag ?? Config.Insecure ?? false,
        Setting("caCert").Value is { } ca && !ca.Equals("none", StringComparison.OrdinalIgnoreCase) ? ca : null);

    /// <summary>The handler for every <see cref="AdoClient"/>; warns when certificate checks are off.</summary>
    public HttpMessageHandler CreateHandler()
    {
        var tls = Tls;
        if (tls.Insecure)
            Console.Error.WriteLine($"Warning: TLS certificate verification is disabled (from {Setting("insecure").Source}).");
        return tls.CreateHandler();
    }

    /// <summary>Every setting in <see cref="Config.Keys"/> order, with the same precedence the commands use.</summary>
    public IReadOnlyList<Setting> Settings() => Config.Keys.Select(Setting).ToList();

    public Setting Setting(string key) => key switch
    {
        "server" => Config.Server is { } s ? FromConfig(key, s) : Remote?.Server is { } r ? new(key, r, "git remote") : new(key, null, "not set"),
        "pat" => Config.Pat is null ? new(key, null, "not set") : FromConfig(key, "********"),
        "project" => args.Get("--project") is { } p ? new(key, p, "--project")
            : Remote?.Project is { } r ? new(key, r, "git remote")
            : Config.Project is { } c ? FromConfig(key, c) : new(key, null, "not set"),
        "apiVersion" => Config.ApiVersion is { } v ? FromConfig(key, v) : new(key, AdoClient.DefaultApiVersion, "default"),
        "insecure" => InsecureFlag is { } f ? new(key, Bool(f), "--insecure")
            : Config.Insecure is { } i ? FromConfig(key, Bool(i)) : new(key, "false", "default"),
        "caCert" => args.Get("--ca-cert") is { } ca ? new(key, ca, "--ca-cert")
            : Config.CaCert is { } c ? FromConfig(key, c) : new(key, null, "not set"),
        _ => throw AdoException.Usage($"unknown setting '{key}'; known: {string.Join(", ", Config.Keys)}"),
    };

    Setting FromConfig(string key, string value) => new(key, value, Config.Sources.GetValueOrDefault(key, "config file"));

    static string Bool(bool b) => b ? "true" : "false";

    public AdoClient Client => _client ??= Server is { } server && Config.Pat is { } pat
        ? new AdoClient(server, pat, args.Has("--debug"), CreateHandler(), Config.ApiVersion ?? AdoClient.DefaultApiVersion)
        : throw new AdoException("not logged in.\n\nRun:\n  ado auth login <server-url>", AdoException.Auth);
}
