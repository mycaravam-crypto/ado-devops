namespace AdoCli.Cli;

using System.Text.Json;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}

public static class Term
{
    /// <summary>Asks for a line on stderr; an empty answer takes <paramref name="defaultValue"/>.</summary>
    public static string Prompt(string label, string? defaultValue = null)
    {
        Console.Error.Write(defaultValue is { Length: > 0 } ? $"{label} [{defaultValue}]: " : $"{label}: ");
        var answer = Console.In.ReadLine()?.Trim();
        return string.IsNullOrEmpty(answer) ? defaultValue ?? "" : answer;
    }

    public static bool Confirm(string question)
    {
        Console.Error.Write(question);
        return Console.In.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes";
    }

    /// <summary>Reads a secret without echo; reads a plain line when stdin is piped (e.g. in CI).</summary>
    public static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
            return Console.In.ReadLine()?.Trim() ?? "";

        Console.Error.Write(prompt);
        var secret = new System.Text.StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
                secret.Length = Math.Max(0, secret.Length - 1);
            else if (!char.IsControl(key.KeyChar))
                secret.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return secret.ToString().Trim();
    }
}

public static class Output
{
    public static int WriteJson(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, Json.Options));
        return 0;
    }

    /// <summary>Left-aligned columns separated by two spaces; the last column is not padded. Written to <paramref name="to"/>, by default stdout.</summary>
    public static void Table(string[] headers, IEnumerable<string[]> rows, TextWriter? to = null)
    {
        to ??= Console.Out;
        var all = rows.Prepend(headers).ToList();
        var widths = headers.Select((_, i) => all.Max(r => r[i].Length)).ToArray();
        foreach (var row in all)
            to.WriteLine(string.Concat(row.Select((cell, i) => i == row.Length - 1 ? cell : cell.PadRight(widths[i] + 2))));
    }

    /// <summary>"active" → "Active", colored only when writing to a terminal.</summary>
    public static string Status(string status)
    {
        var text = status.Length == 0 ? status : char.ToUpperInvariant(status[0]) + status[1..];
        var color = status switch
        {
            "active" or "succeeded" => "32",
            "completed" => "35",
            "abandoned" or "failed" => "31",
            _ => null,
        };
        return color is null || !UseColor ? text : $"\e[{color}m{text}\e[0m";
    }

    /// <summary>
    /// The line after a list's table: how many items were found, the <paramref name="filters"/> in effect, whether
    /// <c>--limit</c> may have cut the list short and how long it took. E.g. "450 work items · open only · all matching · 1.8s".
    /// </summary>
    public static string Summary(int count, string noun, int? limit, IEnumerable<string> filters, TimeSpan elapsed)
    {
        var parts = new List<string> { $"{count} {noun}{(count == 1 ? "" : "s")}" };
        parts.AddRange(filters);
        parts.Add(limit is { } n && count >= n ? $"limited by --limit {n}" : "all matching");
        parts.Add($"{elapsed.TotalSeconds:0.0}s");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Writes <see cref="Summary"/> to stderr after a blank line, only when stdout is a terminal: piped tables,
    /// <c>--ids</c> and <c>--json</c> output stay exactly as they are.
    /// </summary>
    public static void WriteSummary(int count, string noun, int? limit, IEnumerable<string> filters, TimeSpan elapsed)
    {
        if (!Console.IsOutputRedirected)
            Console.Error.WriteLine("\n" + Summary(count, noun, limit, filters, elapsed));
    }

    static bool UseColor =>!Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public static string Branch(string? refName) => refName?.Replace("refs/heads/", "") ?? "";

    public static string Ref(string branch) => branch.StartsWith("refs/") ? branch : "refs/heads/" + branch;
}
