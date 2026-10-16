namespace AdoCli.Tests;

using AdoCli.Cli;

public class TestPlanTraceExportTests
{
    [Fact]
    public void SnapshotExportsOptionalRequirementDetails()
    {
        var requirement = new TestPlanTraceExport.Requirement(5, "Sign in", [101],
            "<p>Users sign in</p>", "<p>Valid credentials are accepted</p>", "5", "2");
        var json = System.Text.Json.JsonSerializer.Serialize(requirement,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("description", json);
        Assert.Contains("Users sign in", json);
        Assert.Contains("acceptanceCriteria", json);
        Assert.Contains("storyPoints", json);
        Assert.Contains("priority", json);
    }

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
