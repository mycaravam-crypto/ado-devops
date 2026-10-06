namespace AdoCli;

using System.Reflection;
using AdoCli.Api;

public static class Program
{
    const string Help = """
        ado - a small CLI for Azure DevOps Server

        Usage:
          ado <command> <subcommand> [arguments] [flags]

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

        throw AdoException.Usage($"unknown command '{string.Join(' ', a.Positional.Take(2))}'. Run 'ado --help'.");
    }

    static Task<int> Print(string text)
    {
        Console.WriteLine(text);
        return Task.FromResult(0);
    }
}
