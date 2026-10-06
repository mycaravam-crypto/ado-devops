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
            var c = Config.Load(path);

            Assert.Equal("https://tfs/tfs/DefaultCollection", c.Server);
            Assert.Equal("secret", c.Pat);
            Assert.Equal("FromEnv", c.Project);
            Assert.DoesNotContain("secret", c.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADO_PROJECT", null);
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
