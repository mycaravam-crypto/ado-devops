namespace AdoCli.Tests;

using AdoCli.Cli;
using Xunit;

public class TestPlanParametersTests
{
    [Fact]
    public void ReadsDatasetRowsAndPreservesValues()
    {
        var xml = "<NewDataSet><xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'/>" +
                  "<Table1><Name>A&amp;B</Name><Result>OK</Result></Table1>" +
                  "<Table1><Name>C</Name><Result>Fail</Result></Table1></NewDataSet>";
        var rows = TestPlanParameters.Rows(xml);
        Assert.Equal(2, rows.Count);
        Assert.Equal("A&B", rows[0]["Name"]);
        Assert.Equal("Fail", rows[1]["Result"]);
        Assert.Empty(TestPlanParameters.Rows("<invalid"));
    }
}
