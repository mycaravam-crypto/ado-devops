namespace AdoCli.Cli;

/// <summary>Reports bounded CLI work on stderr, leaving stdout suitable for JSON and pipes.</summary>
public sealed class CliProgress(string operation)
{
    readonly bool enabled = !Console.IsErrorRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;
    int last = -1;

    /// <summary>Displays completed items and a percentage when a total is known.</summary>
    public void Report(string stage, int done, int total)
    {
        if (!enabled || total <= 0) return;
        var percent = Math.Clamp(done * 100 / total, 0, 100);
        if (percent == last && done != total) return;
        last = percent;
        var filled = percent / 5;
        Console.Error.Write($"\r{operation}: {stage} [{new string('#', filled)}{new string('-', 20 - filled)}] {done}/{total} ({percent}%)");
        if (done == total) Console.Error.WriteLine();
    }

    /// <summary>Displays an indeterminate step before its item count is known.</summary>
    public void Stage(string stage)
    {
        if (!enabled) return;
        Console.Error.WriteLine($"{operation}: {stage}...");
        last = -1;
    }
}
