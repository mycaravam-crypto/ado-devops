namespace AdoCli.Cli;

using System.Text.Json;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
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

    /// <summary>Left-aligned columns separated by two spaces; the last column is not padded.</summary>
    public static void Table(string[] headers, IEnumerable<string[]> rows)
    {
        var all = rows.Prepend(headers).ToList();
        var widths = headers.Select((_, i) => all.Max(r => r[i].Length)).ToArray();
        foreach (var row in all)
            Console.WriteLine(string.Concat(row.Select((cell, i) => i == row.Length - 1 ? cell : cell.PadRight(widths[i] + 2))));
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

    static bool UseColor => !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public static string Branch(string? refName) => refName?.Replace("refs/heads/", "") ?? "";
}
