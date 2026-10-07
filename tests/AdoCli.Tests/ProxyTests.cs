namespace AdoCli.Tests;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AdoCli.Api;
using AdoCli.Cli;

public class ProxyTests
{
    [Fact]
    public async Task SendsRequestsThroughProxyWithCredentialsFromUrl()
    {
        await using var proxy = FakeProxy.Start();
        var handler = new Tls().CreateHandler();
        Proxy.Apply(handler, $"http://me:p%40ss@127.0.0.1:{proxy.Port}");

        var user = await new AdoClient("http://ado.invalid/tfs/DefaultCollection", "pat", handler: handler).GetConnectionDataAsync();

        Assert.Equal("Tester", user.AuthenticatedUser.ProviderDisplayName);
        Assert.StartsWith("GET http://ado.invalid/tfs/DefaultCollection/_apis/connectionData", proxy.Requests.Last());
        Assert.Contains("Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("me:p@ss")), proxy.Requests.Last());
    }

    [Fact]
    public async Task MissingProxyCredentialsGiveClearError()
    {
        await using var proxy = FakeProxy.Start();
        var handler = new Tls().CreateHandler();
        Proxy.Apply(handler, $"http://127.0.0.1:{proxy.Port}"); // default credentials are never sent for Basic, so the fake proxy keeps answering 407

        var e = await Assert.ThrowsAsync<AdoException>(() => new AdoClient("http://ado.invalid", "pat", handler: handler).GetConnectionDataAsync());

        Assert.Equal(Proxy.AuthRequired, e.Message);
    }

    [Fact]
    public void NoneConnectsDirectlyAndDefaultUsesSystemProxy()
    {
        var direct = new SocketsHttpHandler();
        Proxy.Apply(direct, "NONE");
        Assert.False(direct.UseProxy);

        var system = new SocketsHttpHandler();
        Proxy.Apply(system, null);
        Assert.True(system.UseProxy);
        Assert.Null(system.Proxy);
        Assert.Same(CredentialCache.DefaultCredentials, system.DefaultProxyCredentials);
    }

    [Theory]
    [InlineData("http://proxy:8080/", "http://proxy:8080")]
    [InlineData("socks5://proxy:1080", "socks5://proxy:1080")]
    [InlineData("None", "none")]
    public void ChecksProxyUrls(string input, string stored) => Assert.Equal(stored, Proxy.Check(input));

    [Theory]
    [InlineData("proxy:8080")]
    [InlineData("ftp://proxy:21")]
    [InlineData("http://proxy:8080/some/path")]
    public void RejectsInvalidProxyUrls(string input) =>
        Assert.Equal(AdoException.InvalidUsage, Assert.Throws<AdoException>(() => Proxy.Check(input)).ExitCode);

    [Fact]
    public void NeverShowsThePassword()
    {
        Assert.Equal("http://me:****@proxy:8080", Proxy.Mask("http://me:secret@proxy:8080"));
        Assert.Equal("http://me@proxy:8080", Proxy.WithoutPassword("http://me:secret@proxy:8080"));
        Assert.Equal("http://proxy:8080", Proxy.Mask("http://proxy:8080"));
        Assert.DoesNotContain("secret", Assert.Throws<AdoException>(() => Proxy.Check("http://me:secret@proxy:8080/x")).Message);

        var ctx = new Context(Args.Parse(["config", "list"]), new Config { Proxy = "http://me:secret@proxy:8080" });
        Assert.Equal(new Setting("proxy", "http://me:****@proxy:8080", "config file"), ctx.Setting("proxy"));
        Assert.Equal("http://me:****@proxy:8080 (from config file)", AuthCommands.DescribeProxy(ctx));
    }

    [Fact]
    public void FlagOverridesConfigAndIsPassedToGit()
    {
        var ctx = new Context(Args.Parse(["pr", "checkout", "1", "--proxy", "none"]), new Config { Proxy = "http://proxy:8080", Insecure = true });

        Assert.Equal(new Setting("proxy", "none", "--proxy"), ctx.Setting("proxy"));
        Assert.Equal("none, direct connection (from --proxy)", AuthCommands.DescribeProxy(ctx));
        Assert.Equal(["-c", "remote.origin.proxy=", "-c", "http.sslVerify=false"], ctx.GitConfig());

        var unset = new Context(Args.Parse(["pr", "list"]), new Config());
        Assert.Equal(new Setting("proxy", null, "system default"), unset.Setting("proxy"));
        Assert.Empty(unset.GitConfig());
    }

    [Fact]
    public void ConfigSetValidatesProxy()
    {
        Assert.Equal("http://proxy:8080", ConfigCommands.Apply(new Config(), "proxy", "http://proxy:8080/").Proxy);
        Assert.Null(ConfigCommands.Apply(new Config { Proxy = "none" }, "proxy", null).Proxy);
        Assert.Throws<AdoException>(() => ConfigCommands.Apply(new Config(), "proxy", "proxy:8080"));
    }

    /// <summary>A plain HTTP proxy on localhost: answers 407 until a Proxy-Authorization header arrives, then a connectionData response.</summary>
    sealed class FakeProxy : IAsyncDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly Task _loop;

        FakeProxy()
        {
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        public ConcurrentQueue<string> Requests { get; } = new();

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static FakeProxy Start() => new();

        async Task AcceptAsync()
        {
            while (true)
                _ = ServeAsync(await _listener.AcceptTcpClientAsync()); // one task per connection: the client may open a new one after a 407
        }

        async Task ServeAsync(TcpClient client)
        {
            using var _ = client;
            var stream = client.GetStream();
            try
            {
                while (await ReadHeadAsync(stream) is { } head)
                {
                    Requests.Enqueue(head);
                    const string body = """{"authenticatedUser":{"id":"11111111-1111-1111-1111-111111111111","providerDisplayName":"Tester"}}""";
                    var response = head.Contains("Proxy-Authorization:")
                        ? $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n{body}"
                        : "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"test\"\r\nContent-Length: 0\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
                }
            }
            catch (IOException)
            {
                // client closed the connection
            }
        }

        /// <summary>Request line and headers up to the blank line; the client sends no body for GET.</summary>
        static async Task<string?> ReadHeadAsync(NetworkStream stream)
        {
            var head = new StringBuilder();
            var b = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n"))
            {
                if (await stream.ReadAsync(b) == 0)
                    return null;
                head.Append((char)b[0]);
            }
            return head.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try { await _loop; } catch (Exception) { /* listener stopped */ }
        }
    }
}
