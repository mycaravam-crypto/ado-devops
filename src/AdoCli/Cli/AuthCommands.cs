namespace AdoCli.Cli;

using AdoCli.Api;

public static class AuthCommands
{
    /// <summary>ado auth login: reads the PAT, checks it against the server and only then saves it with --insecure / --ca-cert, keeping the configured project, API version and TLS options.</summary>
    public static async Task<int> LoginAsync(Context ctx)
    {
        var server = CheckServer(ctx.Args.At(2) ?? throw AdoException.Usage("usage: ado auth login <server-url>"));

        var pat = Term.ReadSecret("Personal access token: ");
        if (pat.Length == 0)
            throw AdoException.Usage("no token given");

        var user = await new AdoClient(server, pat, ctx.Args.Has("--debug"), ctx.CreateHandler()).GetConnectionDataAsync();
        var old = Config.ReadFile(Config.DefaultPath);
        // TLS options given at login belong to this server, so they are kept for later commands.
        var insecure = ctx.InsecureFlag ?? old?.Insecure;
        var caCert = ctx.Args.Get("--ca-cert") is { } ca ? ConfigCommands.CheckCaCert(ca) : old?.CaCert;
        new Config { Server = server, Pat = pat, Project = old?.Project, ApiVersion = old?.ApiVersion, Insecure = insecure, CaCert = caCert }.Save();
        Console.WriteLine($"Logged in to {server} as {user.AuthenticatedUser.ProviderDisplayName}");
        if (insecure == true)
            Console.WriteLine("TLS certificate verification stays disabled for this server; 'ado config unset insecure' turns it back on.");
        return 0;
    }

    /// <summary>ado auth status: shows the server, user and TLS mode; exits with <see cref="AdoException.Auth"/> when not logged in.</summary>
    public static async Task<int> StatusAsync(Context ctx)
    {
        if (ctx.Config.Server is null || ctx.Config.Pat is null)
        {
            Console.Error.WriteLine("Not logged in.\n\nRun:\n  ado auth login <server-url>");
            return AdoException.Auth;
        }

        var user = await ctx.Client.GetConnectionDataAsync();
        var source = Environment.GetEnvironmentVariable("ADO_PAT") is { Length: > 0 } ? " (token from ADO_PAT)" : "";
        Console.WriteLine($"Logged in to {ctx.Config.Server} as {user.AuthenticatedUser.ProviderDisplayName}{source}");
        Console.WriteLine($"TLS: {DescribeTls(ctx)}");
        return 0;
    }

    /// <summary>One line saying how the server certificate is checked and which setting decided it.</summary>
    public static string DescribeTls(Context ctx)
    {
        var tls = ctx.Tls;
        if (tls.Insecure)
            return $"certificate verification DISABLED (insecure, from {ctx.Setting("insecure").Source})";
        var ca = ctx.Setting("caCert");
        return tls.CaCert is not null
            ? $"system certificate store + CA file {tls.CaCert} (caCert, from {ca.Source})"
            : "system certificate store" + (ca.Value is null ? "" : $" (caCert switched off by {ca.Source})");
    }

    /// <summary>An absolute http(s) URL without credentials, trailing slash removed; warns about plain HTTP.</summary>
    public static string CheckServer(string server)
    {
        if (Uri.TryCreate(server, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0)
            throw AdoException.Usage(AdoClient.CredentialsInUrl);
        if (uri is null || uri.Scheme is not ("https" or "http"))
            throw AdoException.Usage($"invalid server URL '{server}'; expected e.g. https://tfs.company.local/tfs/DefaultCollection");
        if (uri.Scheme == "http")
            Console.Error.WriteLine("Warning: using unencrypted HTTP; your token will be sent in clear text.");
        return server.TrimEnd('/');
    }

    public static Task<int> LogoutAsync(Context ctx)
    {
        if (!File.Exists(Config.DefaultPath))
        {
            Console.WriteLine("Not logged in.");
            return Task.FromResult(0);
        }

        File.Delete(Config.DefaultPath);
        Console.WriteLine("Logged out.");
        return Task.FromResult(0);
    }
}
