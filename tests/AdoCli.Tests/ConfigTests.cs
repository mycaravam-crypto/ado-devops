namespace AdoCli.Tests;

using AdoCli.Api;

public class ConfigTests
{
    [Fact]
    public void LoadsFileAndEnvironmentOverrides()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{"server":"https://tfs/tfs/DefaultCollection","pat":"secret","project":"Platform"}""");
        try
        {
            Environment.SetEnvironmentVariable("ADO_PROJECT", "FromEnv");
            Environment.SetEnvironmentVariable("ADO_API_VERSION", "6.0");
            var c = Config.Load(path);

            Assert.Equal("https://tfs/tfs/DefaultCollection", c.Server);
            Assert.Equal("secret", c.Pat);
            Assert.Equal("FromEnv", c.Project);
            Assert.Equal("6.0", c.ApiVersion);
            Assert.DoesNotContain("secret", c.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADO_PROJECT", null);
            Environment.SetEnvironmentVariable("ADO_API_VERSION", null);
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadsTlsOptionsWithEnvironmentOverrides()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{"insecure":true,"caCert":"/etc/ado/ca.pem"}""");
        try
        {
            Assert.True(Config.Load(path).Insecure);
            Assert.Equal("/etc/ado/ca.pem", Config.Load(path).CaCert);
            Assert.Equal("config file", Config.Load(path).Sources["insecure"]);

            Environment.SetEnvironmentVariable("ADO_INSECURE", "0");
            Environment.SetEnvironmentVariable("ADO_CA_CERT", "/tmp/other.pem");
            Assert.False(Config.Load(path).Insecure);
            Assert.Equal("/tmp/other.pem", Config.Load(path).CaCert);
            Assert.Equal("ADO_CA_CERT", Config.Load(path).Sources["caCert"]);
            Assert.False(Config.Load(path).Sources.ContainsKey("server"));

            Environment.SetEnvironmentVariable("ADO_INSECURE", "maybe");
            Assert.Equal(AdoException.InvalidUsage, Assert.Throws<AdoException>(() => Config.Load(path)).ExitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADO_INSECURE", null);
            Environment.SetEnvironmentVariable("ADO_CA_CERT", null);
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidFileGivesCleanError()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{not json");
        try
        {
            var e = Assert.Throws<AdoException>(() => Config.ReadFile(path));
            Assert.Contains("invalid config file", e.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveRoundTripsWithOwnerOnlyPermissions()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "config.json");
        try
        {
            new Config { Server = "https://tfs", Pat = "secret" }.Save(path);

            Assert.Equal("secret", Config.ReadFile(path)!.Pat);
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
