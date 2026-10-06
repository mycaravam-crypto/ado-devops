namespace AdoCli.Tests;

using AdoCli.Api;

public class ArgsTests
{
    [Fact]
    public void SplitsPositionalsFlagsAndOptions()
    {
        var a = Args.Parse(["pr", "list", "--debug", "--status", "all", "--project=Platform"]);

        Assert.Equal(["pr", "list"], a.Positional);
        Assert.True(a.Has("--debug"));
        Assert.Null(a.Get("--debug"));
        Assert.Equal("all", a.Get("--status"));
        Assert.Equal("Platform", a.Get("--project"));
        Assert.Null(a.At(2));
    }

    [Fact]
    public void OptionWithoutValueIsUsageError()
    {
        var e = Assert.Throws<AdoException>(() => Args.Parse(["pr", "list", "--status"]));
        Assert.Equal(AdoException.InvalidUsage, e.ExitCode);
    }
}
