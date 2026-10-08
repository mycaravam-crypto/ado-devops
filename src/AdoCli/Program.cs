namespace AdoCli;

using System.Reflection;
using AdoCli.Api;
using AdoCli.Cli;

public static class Program
{
    /// <summary>The informational version, e.g. 0.4.2 or 0.4.2-dev.3, without the "+commit" suffix the SDK appends.</summary>
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

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
    public static Task<int> RunAsync(Args a)
    {
        if (a.Has("--version"))
            return Print($"ado {Version}");
        if (HelpPage(a) is { } page)
            return Print(page);

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
            ("workitem", "create") => WorkItemCommands.CreateAsync(ctx),
            ("workitem", "edit") => WorkItemCommands.EditAsync(ctx),
            ("testplan", "list") => TestPlanCommands.ListAsync(ctx),
            ("testplan", "export") => TestPlanCommands.ExportAsync(ctx),
            ("testplan", "import") => TestPlanCommands.ImportAsync(ctx),
            ("build", "list") => BuildCommands.ListAsync(ctx),
            ("build", "show") => BuildCommands.ShowAsync(ctx),
            ("build", "run") => BuildCommands.RunAsync(ctx),
            _ => throw AdoException.Usage($"unknown command '{string.Join(' ', a.Positional.Take(2))}'. Run '{(Help.For(a.At(0)) is null ? "ado" : $"ado {a.At(0)}")} --help'."),
        };
    }

    /// <summary>
    /// The help to show, or null to run the command: 'ado pr', 'ado pr --help', 'ado pr merge --help' and 'ado help pr'
    /// show the pr page; no command, 'ado help' and --help with an unknown command show the overview.
    /// </summary>
    public static string? HelpPage(Args a)
    {
        var command = a.At(0) == "help" ? a.At(1) : a.At(0);
        if (a.Has("--help") || a.Has("-h") || a.At(0) == "help" || a.Positional.Count == 0)
            return Help.For(command) ?? Help.Overview;
        return a.Positional.Count == 1 ? Help.For(command) : null;
    }

    static Task<int> Print(string text)
    {
        Console.WriteLine(text);
        return Task.FromResult(0);
    }
}
