namespace AdoCli.Cli;

using System.Text.Json;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

public static class Term
{
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
