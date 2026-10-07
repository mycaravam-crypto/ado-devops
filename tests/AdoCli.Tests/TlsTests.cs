namespace AdoCli.Tests;

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AdoCli.Api;

public class TlsTests
{
    [Fact]
    public async Task RejectsCertificateFromUnknownCa()
    {
        using var ca = CreateCa();
        await using var server = TlsServer.Start(CreateLeaf(ca, "localhost"));

        var e = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync(server, new Tls()));

        Assert.True(Tls.IsUntrusted(e));
    }

    [Fact]
    public async Task InsecureAcceptsAnyCertificate()
    {
        using var ca = CreateCa();
        await using var server = TlsServer.Start(CreateLeaf(ca, "some-other-host"));

        Assert.Equal("Tester", (await GetAsync(server, new Tls(Insecure: true))).AuthenticatedUser.ProviderDisplayName);
    }

    [Fact]
    public async Task CaCertTrustsServerIssuedByIt()
    {
        using var ca = CreateCa();
        await using var server = TlsServer.Start(CreateLeaf(ca, "localhost"));
        var pem = WritePem(ca);
        try
        {
            Assert.Equal("Tester", (await GetAsync(server, new Tls(CaCert: pem))).AuthenticatedUser.ProviderDisplayName);
        }
        finally
        {
            File.Delete(pem);
        }
    }

    [Fact]
    public async Task CaCertStillChecksHostName()
    {
        using var ca = CreateCa();
        await using var server = TlsServer.Start(CreateLeaf(ca, "some-other-host"));
        var pem = WritePem(ca);
        try
        {
            var e = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync(server, new Tls(CaCert: pem)));
            Assert.True(Tls.IsUntrusted(e));
        }
        finally
        {
            File.Delete(pem);
        }
    }

    [Fact]
    public void MissingOrEmptyCaCertGivesCleanError()
    {
        Assert.Contains("could not read CA certificate", Assert.Throws<AdoException>(() => new Tls(CaCert: "/no/such/ca.pem").CreateHandler()).Message);

        var empty = Path.GetTempFileName();
        try
        {
            Assert.Contains("no PEM certificate", Assert.Throws<AdoException>(() => Tls.LoadCaCert(empty)).Message);
        }
        finally
        {
            File.Delete(empty);
        }
    }

    [Fact]
    public void PassesTheSameSettingsToGit()
    {
        Assert.Empty(new Tls().GitConfig());
        Assert.Equal(["-c", "http.sslVerify=false"], new Tls(Insecure: true, CaCert: "ca.pem").GitConfig());
        Assert.Equal(["-c", $"http.sslCAInfo={Path.GetFullPath("ca.pem")}"], new Tls(CaCert: "ca.pem").GitConfig());
    }

    static Task<ConnectionData> GetAsync(TlsServer server, Tls tls) =>
        new AdoClient($"https://localhost:{server.Port}", "pat", handler: tls.CreateHandler()).GetConnectionDataAsync();

    static X509Certificate2 CreateCa()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=ado test CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    static X509Certificate2 CreateLeaf(X509Certificate2 ca, string host)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var cert = request.Create(ca, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), Guid.NewGuid().ToByteArray());
        using var withKey = cert.CopyWithPrivateKey(key);
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null); // SslStream needs a persisted key on some platforms
    }

    static string WritePem(X509Certificate2 cert)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, cert.ExportCertificatePem());
        return path;
    }

    /// <summary>Answers every TLS connection on localhost with one connectionData response.</summary>
    sealed class TlsServer : IAsyncDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly X509Certificate2 _cert;
        readonly Task _loop;

        TlsServer(X509Certificate2 cert)
        {
            _cert = cert;
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static TlsServer Start(X509Certificate2 cert) => new(cert);

        async Task AcceptAsync()
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync();
                try
                {
                    await using var ssl = new SslStream(client.GetStream());
                    await ssl.AuthenticateAsServerAsync(_cert);
                    var buffer = new byte[4096];
                    await ssl.ReadAsync(buffer);
                    const string body = """{"authenticatedUser":{"id":"11111111-1111-1111-1111-111111111111","providerDisplayName":"Tester"}}""";
                    await ssl.WriteAsync(Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));
                }
                catch (Exception e) when (e is IOException or System.Security.Authentication.AuthenticationException)
                {
                    // the client rejected our certificate
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try { await _loop; } catch (Exception) { /* listener stopped */ }
            _cert.Dispose();
        }
    }
}
