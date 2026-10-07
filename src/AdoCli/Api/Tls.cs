namespace AdoCli.Api;

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

/// <summary>How the server's TLS certificate is checked: by the system (default), additionally against a CA file, or not at all.</summary>
public sealed record Tls(bool Insecure = false, string? CaCert = null)
{
    public const string UntrustedHint =
        "the server's TLS certificate is not trusted.\n\n" +
        "If your server uses a company CA, trust its PEM file: ado config set caCert <file> (or --ca-cert / ADO_CA_CERT).\n" +
        "To skip certificate checks entirely (internal servers only): ado config set insecure true (or --insecure / ADO_INSECURE=1).";

    /// <summary>The HTTP handler for <see cref="AdoClient"/>; the CA file is read here so a bad path fails before any request.</summary>
    public HttpMessageHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler();
        if (Insecure)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        else if (CaCert is not null)
        {
            var roots = LoadCaCert(CaCert);
            handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) => Validate(cert, chain, errors, roots);
        }
        return handler;
    }

    /// <summary>Accepts what the system trusts, or a chain ending in one of <paramref name="roots"/>; a wrong host name is never accepted.</summary>
    public static bool Validate(X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors, X509Certificate2Collection roots)
    {
        if (errors == SslPolicyErrors.None)
            return true;
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || cert is null)
            return false;

        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.AddRange(roots);
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // internal CAs rarely publish revocation lists
        if (chain is not null)
            foreach (var element in chain.ChainElements)
                custom.ChainPolicy.ExtraStore.Add(element.Certificate);
        return custom.Build(cert as X509Certificate2 ?? new X509Certificate2(cert));
    }

    /// <summary>All certificates of a PEM file (a single CA or a bundle).</summary>
    public static X509Certificate2Collection LoadCaCert(string path)
    {
        var certs = new X509Certificate2Collection();
        try
        {
            certs.ImportFromPemFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new AdoException($"could not read CA certificate {path}: {e.Message}");
        }
        return certs.Count > 0 ? certs : throw new AdoException($"no PEM certificate found in {path}");
    }

    /// <summary>git -c options with the same effect, for commands that talk to the server.</summary>
    public string[] GitConfig() =>
        Insecure ? ["-c", "http.sslVerify=false"]
        : CaCert is not null ? ["-c", $"http.sslCAInfo={Path.GetFullPath(CaCert)}"]
        : [];

    /// <summary>True when a request failed because the server's certificate was rejected.</summary>
    public static bool IsUntrusted(HttpRequestException e) => e.InnerException is AuthenticationException;
}
