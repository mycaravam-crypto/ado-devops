namespace AdoCli.Cli;

using AdoCli.Api;

/// <summary>What a command needs: its arguments, the configuration and a lazily created client.</summary>
public sealed class Context(Args args)
{
    AdoClient? _client;

    public Args Args => args;

    public Config Config { get; } = Config.Load();

    public AdoClient Client => _client ??= Config is { Server: { } server, Pat: { } pat }
        ? new AdoClient(server, pat, args.Has("--debug"))
        : throw new AdoException("not logged in.\n\nRun:\n  ado auth login <server-url>", AdoException.Auth);
}
