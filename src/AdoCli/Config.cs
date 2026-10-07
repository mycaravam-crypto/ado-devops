namespace AdoCli;

using System.Text.Json;
using System.Text.Json.Serialization;
using AdoCli.Api;
using AdoCli.Cli;

/// <summary>~/.ado/config.json, overridden by ADO_SERVER / ADO_PAT / ADO_PROJECT / ADO_API_VERSION / ADO_INSECURE / ADO_CA_CERT / ADO_PROXY.</summary>
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
    /// <summary>Proxy URL (credentials allowed), "none" for a direct connection; unset uses the system proxy.</summary>
    public string? Proxy { get; init; }

    /// <summary>Where each loaded value came from: the environment variable's name, or "config file".</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Sources { get; init; } = new Dictionary<string, string>();

    /// <summary>The keys of the config file, as <c>ado config</c> names them.</summary>
    public static readonly string[] Keys = ["server", "pat", "project", "apiVersion", "insecure", "caCert", "proxy"];

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ado", "config.json");

    /// <summary>The config file merged with the environment; each set environment variable wins over its file value.</summary>
    public static Config Load(string? path = null)
    {
        var file = ReadFile(path ?? DefaultPath);
        var sources = new Dictionary<string, string>();
        string? Pick(string key, string env, string? fromFile)
        {
            var value = Env(env) ?? fromFile;
            if (value is not null)
                sources[key] = Env(env) is null ? "config file" : env;
            return value;
        }

        // A file value is a JSON bool, so only the environment variable can fail to parse.
        var insecure = Pick("insecure", "ADO_INSECURE", file?.Insecure?.ToString());
        return new Config
        {
            Server = Pick("server", "ADO_SERVER", file?.Server),
            Pat = Pick("pat", "ADO_PAT", file?.Pat),
            Project = Pick("project", "ADO_PROJECT", file?.Project),
            ApiVersion = Pick("apiVersion", "ADO_API_VERSION", file?.ApiVersion),
            Insecure = insecure is null ? null : ParseBool("ADO_INSECURE", insecure),
            CaCert = Pick("caCert", "ADO_CA_CERT", file?.CaCert),
            Proxy = Pick("proxy", "ADO_PROXY", file?.Proxy),
            Sources = sources,
        };
    }

    /// <summary>The config file alone, without environment overrides; null when it does not exist.</summary>
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

    /// <summary>1/true/yes or 0/false/no, any case; <paramref name="name"/> says where the value came from in the error.</summary>
    public static bool ParseBool(string name, string value) => value.ToLowerInvariant() switch
    {
        "1" or "true" or "yes" => true,
        "0" or "false" or "no" => false,
        _ => throw AdoException.Usage($"{name} must be 1/true/yes or 0/false/no, not '{value}'"),
    };

    static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
