namespace AdoCli.Api;

using System.Net;

/// <summary>
/// The proxy setting: a URL such as http://user:password@proxy:8080, "none" for a direct connection,
/// or null for the system default (HTTPS_PROXY / HTTP_PROXY / NO_PROXY, or the OS settings on Windows and macOS).
/// </summary>
public static class Proxy
{
    /// <summary>The value that means: connect directly, ignoring the system proxy.</summary>
    public const string None = "none";

    /// <summary>The error for a 407 answer from the proxy.</summary>
    public const string AuthRequired =
        "the proxy requires authentication; put the credentials in the proxy URL, e.g. ado config set proxy http://user:password@proxy:8080";

    static readonly string[] Schemes = ["http", "https", "socks4", "socks4a", "socks5"];

    /// <summary>True for "none" in any case.</summary>
    public static bool IsNone(string? proxy) => string.Equals(proxy, None, StringComparison.OrdinalIgnoreCase);

    /// <summary>"none" or a proxy URL with a supported scheme and no path; throws a usage error otherwise.</summary>
    public static string Check(string proxy)
    {
        if (IsNone(proxy))
            return None;
        if (!Uri.TryCreate(proxy, UriKind.Absolute, out var uri) || !Schemes.Contains(uri.Scheme) || uri.AbsolutePath != "/")
            throw AdoException.Usage($"invalid proxy '{Mask(proxy)}'; expected e.g. http://proxy.company.local:8080, or 'none' for a direct connection");
        return proxy.TrimEnd('/');
    }

    /// <summary>
    /// Points <paramref name="handler"/> at the proxy. Credentials come from the URL; without them the
    /// signed-in user's credentials answer an NTLM/Kerberos challenge, as browsers do on company networks.
    /// </summary>
    public static void Apply(SocketsHttpHandler handler, string? proxy)
    {
        if (IsNone(proxy))
        {
            handler.UseProxy = false;
            return;
        }
        if (proxy is null)
        {
            handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
            return;
        }

        var uri = new Uri(Check(proxy));
        // The address carries no credentials, so they never show up in exception messages.
        var web = new WebProxy(new UriBuilder(uri) { UserName = "", Password = "" }.Uri);
        if (uri.UserInfo.Length > 0)
        {
            var (user, password) = Split(uri.UserInfo);
            web.Credentials = new NetworkCredential(user, password);
        }
        else
            web.UseDefaultCredentials = true;
        handler.Proxy = web;
        handler.UseProxy = true;
    }

    /// <summary>The URL with its password replaced by ****, for output.</summary>
    public static string Mask(string proxy) =>
        Uri.TryCreate(proxy, UriKind.Absolute, out var uri) && Split(uri.UserInfo).Password is { Length: > 0 }
            ? new UriBuilder(uri) { Password = "****" }.Uri.ToString().TrimEnd('/')
            : proxy;

    /// <summary>The URL without its password; git then asks its credential helper for it.</summary>
    public static string WithoutPassword(string proxy) =>
        Uri.TryCreate(proxy, UriKind.Absolute, out var uri) && Split(uri.UserInfo).Password is { Length: > 0 }
            ? new UriBuilder(uri) { Password = "" }.Uri.ToString().TrimEnd('/')
            : proxy;

    /// <summary>git -c options with the same effect for the origin remote; an empty proxy makes git connect directly.</summary>
    public static string[] GitConfig(string? proxy) =>
        proxy is null ? [] : ["-c", $"remote.origin.proxy={(IsNone(proxy) ? "" : proxy)}"];

    static (string User, string Password) Split(string userInfo)
    {
        var colon = userInfo.IndexOf(':');
        return colon < 0
            ? (Uri.UnescapeDataString(userInfo), "")
            : (Uri.UnescapeDataString(userInfo[..colon]), Uri.UnescapeDataString(userInfo[(colon + 1)..]));
    }
}
