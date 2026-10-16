namespace AdoCli.Tests;

using AdoCli.Cli;

public class TestPlanTraceExportTests
{
    [Fact]
    public void SnapshotOmitsTestSecretsAndUsesVersionedFormat()
    {
        var snapshot = new TestPlanTraceExport.Snapshot("ado.test-traceability/v1", "Platform", "Product\\ALPHA",
            DateTimeOffset.UtcNow, 12, "Release",
            [new TestPlanTraceExport.Case(101, "Login", "Product\\ALPHA", [3])],
            [new TestPlanTraceExport.Requirement(5, "Sign in", [101])]);
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        Assert.Contains("ado.test-traceability/v1", json);
        Assert.Contains("TestCaseIds", json);
        Assert.DoesNotContain("LocalDataSource", json);
        Assert.DoesNotContain("Steps", json);
    }
}
