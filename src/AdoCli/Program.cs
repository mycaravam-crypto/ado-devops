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

        Global flags:
          --debug     log HTTP requests and show stack traces
          --help      show this help
          --version   show version
        """;

    public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);

    public static async Task<int> Main(string[] argv)
    {
        var debug = argv.Contains("--debug");
        try
        {
            return await RunAsync(Args.Parse(argv));
        }
        catch (AdoException e)
        {
            Console.Error.WriteLine(debug ? e.ToString() : $"Error: {e.Message}");
            return e.ExitCode;
        }
        catch (HttpRequestException e) when (e.StatusCode is null && !debug)
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
            return AdoException.General;
        }
    }

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
            _ => throw AdoException.Usage($"unknown command '{string.Join(' ', a.Positional.Take(2))}'. Run 'ado --help'."),
        };
    }

    static Task<int> Print(string text)
    {
        Console.WriteLine(text);
        return Task.FromResult(0);
    }
}
