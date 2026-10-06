namespace AdoCli;

using System.Text.Json;
using AdoCli.Api;
using AdoCli.Cli;

/// <summary>~/.ado/config.json, overridden by ADO_SERVER / ADO_PAT / ADO_PROJECT.</summary>
public sealed class Config
{
    public string? Server { get; init; }
    public string? Pat { get; init; }
    public string? Project { get; init; }

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ado", "config.json");

    public static Config Load(string? path = null)
    {
        var file = ReadFile(path ?? DefaultPath);
        return new Config
        {
            Server = Env("ADO_SERVER") ?? file?.Server,
            Pat = Env("ADO_PAT") ?? file?.Pat,
            Project = Env("ADO_PROJECT") ?? file?.Project,
        };
    }

    public static Config? ReadFile(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Json.Options);
        }
        catch (JsonException)
        {
            throw new AdoException($"invalid config file {path}");
        }
    }

    /// <summary>Writes the file readable by the current user only, since it holds the PAT.</summary>
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Delete(path); // UnixCreateMode only applies to new files
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(path, options);
        JsonSerializer.Serialize(file, this, Json.Options);
    }

    static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
