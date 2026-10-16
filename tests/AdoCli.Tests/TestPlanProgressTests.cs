namespace AdoCli.Tests;

using AdoCli.Cli;
using Xunit;

/// <summary>Regression checks for testpoint outcome, area isolation and safe progress HTML.</summary>
public class TestPlanProgressTests
{
    [Theory]
    [InlineData("Passed", "Passed")]
    [InlineData("failed", "Failed")]
    [InlineData("Blocked", "Blocked")]
    [InlineData("NotExecuted", "NotRun")]
    [InlineData(null, "NotRun")]
    public void NormalizesPointOutcomes(string? input, string expected) =>
        Assert.Equal(expected, TestPlanProgress.Outcome(input));

    [Fact]
    public void AreaFilterDoesNotIncludeSimilarlyPrefixedProjects()
    {
        Assert.True(TestPlanProgress.InsideArea("Product\\ALPHA", "Product\\ALPHA"));
        Assert.True(TestPlanProgress.InsideArea("Product\\ALPHA\\Web", "Product\\ALPHA"));
        Assert.False(TestPlanProgress.InsideArea("Product\\ALPHA2", "Product\\ALPHA"));
    }

    [Fact]
    public void HtmlEscapesPlanAreaAndSuiteNames()
    {
        var report = new TestPlanProgress.Report(5, "<script>", "Product\\<ALPHA>", 14, 2,
            1, 0, 0, 1, [new TestPlanProgress.SuiteProgress(1, "<img src=x>", 2, 1, 0, 0, 1)], []);
        var html = TestPlanProgress.Html(report);
        Assert.DoesNotContain("<img src=x>", html);
        Assert.Contains("&lt;img src=x&gt;", html);
        Assert.Contains("Test progress", html);
        Assert.Equal(50, report.ExecutionPercent);
    }
}
