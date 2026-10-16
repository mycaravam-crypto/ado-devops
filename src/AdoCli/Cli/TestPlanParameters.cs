namespace AdoCli.Cli;

using System.Xml.Linq;

/// <summary>Reads local test-case parameter values without modifying their original XML.</summary>
public static class TestPlanParameters
{
    /// <summary>Parses LocalDataSource dataset rows into name/value dictionaries; unsupported XML yields no rows.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return [];
            return root.Elements().Where(e => e.Name.LocalName != "schema" && e.HasElements)
                .Select(row => (IReadOnlyDictionary<string, string>)row.Elements()
                    .GroupBy(e => e.Name.LocalName)
                    .ToDictionary(g => g.Key, g => g.First().Value))
                .Where(row => row.Count > 0).ToList();
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }
}
