namespace AdoCli;

using System.Text.Json;
using AdoCli.Api;
using AdoCli.Cli;

/// <summary>~/.ado/config.json, overridden by ADO_SERVER / ADO_PAT / ADO_PROJECT / ADO_API_VERSION / ADO_INSECURE / ADO_CA_CERT.</summary>
public sealed class Config
{
    public string? Server { get; init; }
    public string? Pat { get; init; }
    public string? Project { get; init; }
    public string? ApiVersion { get; init; }
    /// <summary>Skip TLS certificate checks; for internal servers with self-signed certificates.</summary>
    public bool? Insecure { get; init; }
    /// <summary>PEM file with the CA certificate(s) the server's certificate is issued by, trusted in addition to the system's.</summary>
    public string? CaCert { get; init; }

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ado", "config.json");

    /// <summary>The config file merged with the environment; each set environment variable wins over its file value.</summary>
    public static Config Load(string? path = null)
    {
        var file = ReadFile(path ?? DefaultPath);
        return new Config
        {
            Server = Env("ADO_SERVER") ?? file?.Server,
            Pat = Env("ADO_PAT") ?? file?.Pat,
            Project = Env("ADO_PROJECT") ?? file?.Project,
            ApiVersion = Env("ADO_API_VERSION") ?? file?.ApiVersion,
            Insecure = Env("ADO_INSECURE") is { } insecure ? Bool("ADO_INSECURE", insecure) : file?.Insecure,
            CaCert = Env("ADO_CA_CERT") ?? file?.CaCert,
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

    static bool Bool(string name, string value) => value.ToLowerInvariant() switch
    {
        "1" or "true" or "yes" => true,
        "0" or "false" or "no" => false,
        _ => throw AdoException.Usage($"{name} must be 1/true/yes or 0/false/no, not '{value}'"),
    };

    static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
