namespace AdoCli.Tests;

using AdoCli.Api;
using AdoCli.Cli;

public class SettingsTests
{
    static readonly Config Saved = new()
    {
        Server = "https://tfs",
        Pat = "secret-pat",
        Insecure = true,
        CaCert = "/etc/ado/ca.pem",
        Sources = new Dictionary<string, string> { ["server"] = "config file", ["pat"] = "ADO_PAT", ["insecure"] = "config file", ["caCert"] = "ADO_CA_CERT" },
    };

    static Context Ctx(params string[] argv) => new(Args.Parse(argv), Saved);

    [Fact]
    public void ReportsValueAndSourceOfEachSetting()
    {
        var settings = Ctx("config", "list").Settings().ToDictionary(s => s.Key);

        Assert.Equal(new Setting("server", "https://tfs", "config file"), settings["server"]);
        Assert.Equal(new Setting("pat", "********", "ADO_PAT"), settings["pat"]);
        Assert.Equal(new Setting("apiVersion", AdoClient.DefaultApiVersion, "default"), settings["apiVersion"]);
        Assert.Equal(new Setting("insecure", "true", "config file"), settings["insecure"]);
        Assert.Equal(new Setting("caCert", "/etc/ado/ca.pem", "ADO_CA_CERT"), settings["caCert"]);
    }

    [Fact]
    public void FlagsOverrideSavedSettingsInBothDirections()
    {
        var off = Ctx("repo", "list", "--insecure=false", "--ca-cert", "none");
        Assert.Equal(new Tls(), off.Tls);
        Assert.Equal(new Setting("insecure", "false", "--insecure"), off.Setting("insecure"));
        Assert.Equal("system certificate store (caCert switched off by --ca-cert)", AuthCommands.DescribeTls(off));

        var on = new Context(Args.Parse(["repo", "list", "--insecure"]), new Config());
        Assert.True(on.Tls.Insecure);
        Assert.Equal("certificate verification DISABLED (insecure, from --insecure)", AuthCommands.DescribeTls(on));
    }

    [Fact]
    public void DescribesCaCertAndItsSource()
    {
        var ctx = Ctx("auth", "status", "--insecure=no");

        Assert.Equal(new Tls(CaCert: "/etc/ado/ca.pem"), ctx.Tls);
        Assert.Equal("system certificate store + CA file /etc/ado/ca.pem (caCert, from ADO_CA_CERT)", AuthCommands.DescribeTls(ctx));
    }

    [Fact]
    public void InvalidInsecureFlagIsUsageError()
    {
        var e = Assert.Throws<AdoException>(() => Ctx("repo", "list", "--insecure=sometimes").Tls);
        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
    }

    [Fact]
    public void SetValidatesAndNormalizes()
    {
        var c = ConfigCommands.Apply(Saved, "server", "https://tfs2/tfs/DefaultCollection/");
        c = ConfigCommands.Apply(c, "insecure", "NO");
        c = ConfigCommands.Apply(c, "caCert", "None");

        Assert.Equal("https://tfs2/tfs/DefaultCollection", c.Server);
        Assert.False(c.Insecure);
        Assert.Equal("none", c.CaCert);
        Assert.Equal("secret-pat", c.Pat);

        Assert.Throws<AdoException>(() => ConfigCommands.Apply(Saved, "server", "tfs2"));
        Assert.Throws<AdoException>(() => ConfigCommands.Apply(Saved, "insecure", "maybe"));
        Assert.Throws<AdoException>(() => ConfigCommands.Apply(Saved, "caCert", "/no/such/ca.pem"));
        Assert.Contains("ado auth login", Assert.Throws<AdoException>(() => ConfigCommands.Apply(Saved, "pat", "x")).Message);
    }

    [Fact]
    public void UnsetRemovesOnlyThatSetting()
    {
        var c = ConfigCommands.Apply(Saved, "insecure", null);

        Assert.Null(c.Insecure);
        Assert.Equal("/etc/ado/ca.pem", c.CaCert);
        Assert.Equal("https://tfs", c.Server);
    }
}
