namespace AdoCli.Cli;

using AdoCli.Api;

/// <summary>ado config: shows the settings in effect and edits ~/.ado/config.json without hand-editing JSON.</summary>
public static class ConfigCommands
{
    /// <summary>ado config list: every setting with its value and where it came from; the token is masked.</summary>
    public static Task<int> ListAsync(Context ctx)
    {
        var settings = ctx.Settings();
        if (ctx.Json)
            return Task.FromResult(Output.WriteJson(settings));

        Output.Table(["KEY", "VALUE", "SOURCE"], settings.Select(s => new[] { s.Key, s.Value ?? "-", s.Source }));
        return Task.FromResult(0);
    }

    /// <summary>ado config get &lt;key&gt;: the value in effect; exits with 1 when the setting is not set, like git config --get.</summary>
    public static Task<int> GetAsync(Context ctx)
    {
        var setting = ctx.Setting(Key(ctx, "config get <key>"));
        if (ctx.Json)
            Output.WriteJson(setting);
        else if (setting.Value is not null)
            Console.WriteLine(setting.Value);
        return Task.FromResult(setting.Value is null ? AdoException.General : 0);
    }

    /// <summary>ado config set &lt;key&gt; &lt;value&gt;: validates the value and writes it to the config file.</summary>
    public static Task<int> SetAsync(Context ctx)
    {
        var key = Key(ctx, "config set <key> <value>");
        var value = ctx.Args.At(3) ?? throw AdoException.Usage("usage: ado config set <key> <value>");
        return Write(Apply(Config.ReadFile(Config.DefaultPath) ?? new Config(), key, value), key, $"Set {key} = {value}");
    }

    /// <summary>ado config unset &lt;key&gt;: removes the setting from the config file, so its default applies again.</summary>
    public static Task<int> UnsetAsync(Context ctx)
    {
        var key = Key(ctx, "config unset <key>");
        return Write(Apply(Config.ReadFile(Config.DefaultPath) ?? new Config(), key, null), key, $"Unset {key}");
    }

    /// <summary>A copy of <paramref name="file"/> with <paramref name="key"/> set to the checked <paramref name="value"/>, or removed when null.</summary>
    public static Config Apply(Config file, string key, string? value)
    {
        if (key == "pat")
            throw AdoException.Usage("the token is set with 'ado auth login' and removed with 'ado auth logout'");
        if (key == "apiVersion" && value is { Length: 0 })
            throw AdoException.Usage("apiVersion must not be empty");
        return new Config
        {
            Server = key == "server" ? (value is null ? null : AuthCommands.CheckServer(value)) : file.Server,
            Pat = file.Pat,
            Project = key == "project" ? value : file.Project,
            ApiVersion = key == "apiVersion" ? value : file.ApiVersion,
            Insecure = key == "insecure" ? (value is null ? null : Config.ParseBool("insecure", value)) : file.Insecure,
            CaCert = key == "caCert" ? (value is null ? null : CheckCaCert(value)) : file.CaCert,
        };
    }

    /// <summary>"none", or the absolute path of a readable PEM file, so the setting works from any directory.</summary>
    public static string CheckCaCert(string path)
    {
        if (path.Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        var full = Path.GetFullPath(path);
        Tls.LoadCaCert(full);
        return full;
    }

    static string Key(Context ctx, string usage)
    {
        var name = ctx.Args.At(2) ?? throw AdoException.Usage($"usage: ado {usage}\n\nKeys: {string.Join(", ", Config.Keys)}");
        return Config.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw AdoException.Usage($"unknown setting '{name}'; known: {string.Join(", ", Config.Keys)}");
    }

    /// <summary>Saves and says so on stderr, warning when an environment variable still overrides the file.</summary>
    static Task<int> Write(Config config, string key, string message)
    {
        config.Save();
        Console.Error.WriteLine($"{message} in {Config.DefaultPath}");
        if (Config.Load().Sources.GetValueOrDefault(key) is { } source && source != "config file")
            Console.Error.WriteLine($"Note: {source} is set and overrides the config file.");
        return Task.FromResult(0);
    }
}
