namespace AdoCli;

using System.Reflection;
using AdoCli.Api;
using AdoCli.Cli;

public static class Program
{
    const string Help = """
        ado - a small CLI for Azure DevOps Server

        Usage:
          ado <command> <subcommand> [arguments] [flags]

        Commands:
          auth login <server-url>   log in with a personal access token
          auth status               show login state
          auth logout               remove stored credentials

          config list               show every setting, its value and where it comes from
          config get <key>          print one setting (exit 1 if not set)
          config set <key> <value>  save a setting in ~/.ado/config.json
          config unset <key>        remove a setting from ~/.ado/config.json
                                    keys: server, project, apiVersion, insecure, caCert, proxy

          repo list                 list repositories (of the current project, if known)
          repo show [<repo>]        show repository details
          repo clone <repo> [dir]   clone a repository with git
          repo status               show the Azure DevOps repository of the current directory

          pr list                   list pull requests of the project/repository [--mine] [--status active|completed|abandoned|all]
          pr show <id>              show a pull request
          pr context <id>           pull request with commits, changed files and work items, as JSON
          pr diff <id>              show the changes of a pull request (uses git; --json lists changed files)
          pr checkout <id>          check out a pull request as local branch pr/<id>
          pr create                 create a pull request (prompts, or --title --description --source --target)
          pr approve <id>           approve a pull request
          pr merge <id>             complete a pull request [--squash] [--yes]

          workitem list             list open work items assigned to you
          workitem show <id>        show a work item

          build list                list recent builds of the project
          build show <id>           show a build
          build run <definition-id> queue a build [--branch <branch>]

        Inside a cloned Azure DevOps repository, project and repository are detected
        from the origin remote. Otherwise pass --project <name> and --repo <name>,
        or set a default project with ADO_PROJECT.

        Settings are taken from flags, then ADO_* environment variables, then
        ~/.ado/config.json. 'ado config list' shows which one applies.

        Global flags:
          --json            machine-readable output
          --debug           log HTTP requests and show stack traces
          --insecure        skip TLS certificate checks (internal servers only);
                            --insecure=false overrides a saved setting
          --ca-cert <file>  also trust the CA certificate(s) in this PEM file;
                            --ca-cert none overrides a saved one
          --proxy <url>     use this HTTP(S) or SOCKS proxy; --proxy none connects
                            directly (default: HTTPS_PROXY or system settings)
          --help            show this help
          --version         show version
        """;

    public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);

    /// <summary>Entry point: runs one command and turns every error into a message on stderr and an exit code.</summary>
    public static async Task<int> Main(string[] argv)
    {
        var debug = argv.Contains("--debug");
        try
        {
            return await RunAsync(Args.Parse(argv));
        }
        catch (HttpRequestException e) when (!debug && Tls.IsUntrusted(e))
        {
            Console.Error.WriteLine($"Error: {Tls.UntrustedHint}");
            return AdoException.General;
        }
        catch (HttpRequestException e) when (!debug && e.StatusCode == System.Net.HttpStatusCode.ProxyAuthenticationRequired)
        {
            Console.Error.WriteLine($"Error: {Proxy.AuthRequired}");
            return AdoException.General;
        }
        catch (HttpRequestException e) when (!debug && e.HttpRequestError == HttpRequestError.ProxyTunnelError)
        {
            Console.Error.WriteLine($"Error: could not connect through the proxy: {e.Message}\n\nCheck the proxy with 'ado config get proxy'; 'ado config set proxy none' connects directly.");
            return AdoException.General;
        }
        catch (HttpRequestException e) when (!debug)
        {
            Console.Error.WriteLine($"Error: could not reach Azure DevOps Server: {e.Message}");
            return AdoException.General;
        }
        catch (TaskCanceledException) when (!debug)
        {
            Console.Error.WriteLine("Error: request to Azure DevOps Server timed out");
            return AdoException.General;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(debug ? e.ToString() : $"Error: {e.Message}");
            return e is AdoException ado ? ado.ExitCode : AdoException.General;
        }
    }

    /// <summary>Dispatches &lt;command&gt; &lt;subcommand&gt; to its handler; --help and --version need no configuration.</summary>
    static Task<int> RunAsync(Args a)
    {
        if (a.Has("--version"))
            return Print($"ado {Version}");
        if (a.Has("--help") || a.Has("-h") || a.Positional.Count == 0 || a.At(0) == "help")
            return Print(Help);

        var ctx = new Context(a);
        return (a.At(0), a.At(1)) switch
        {
            ("auth", "login") => AuthCommands.LoginAsync(ctx),
            ("auth", "status") => AuthCommands.StatusAsync(ctx),
            ("auth", "logout") => AuthCommands.LogoutAsync(ctx),
            ("config", "list") => ConfigCommands.ListAsync(ctx),
            ("config", "get") => ConfigCommands.GetAsync(ctx),
            ("config", "set") => ConfigCommands.SetAsync(ctx),
            ("config", "unset") => ConfigCommands.UnsetAsync(ctx),
            ("repo", "list") => RepoCommands.ListAsync(ctx),
            ("repo", "show") => RepoCommands.ShowAsync(ctx),
            ("repo", "clone") => RepoCommands.CloneAsync(ctx),
            ("repo", "status") => RepoCommands.StatusAsync(ctx),
            ("pr", "list") => PrCommands.ListAsync(ctx),
            ("pr", "show") => PrCommands.ShowAsync(ctx),
            ("pr", "context") => PrCommands.ContextAsync(ctx),
            ("pr", "diff") => PrCommands.DiffAsync(ctx),
            ("pr", "checkout") => PrCommands.CheckoutAsync(ctx),
            ("pr", "create") => PrCommands.CreateAsync(ctx),
            ("pr", "approve") => PrCommands.ApproveAsync(ctx),
            ("pr", "merge") => PrCommands.MergeAsync(ctx),
            ("workitem", "list") => WorkItemCommands.ListAsync(ctx),
            ("workitem", "show") => WorkItemCommands.ShowAsync(ctx),
            ("build", "list") => BuildCommands.ListAsync(ctx),
            ("build", "show") => BuildCommands.ShowAsync(ctx),
            ("build", "run") => BuildCommands.RunAsync(ctx),
            _ => throw AdoException.Usage($"unknown command '{string.Join(' ', a.Positional.Take(2))}'. Run 'ado --help'."),
        };
    }

    static Task<int> Print(string text)
    {
        Console.WriteLine(text);
        return Task.FromResult(0);
    }
}
