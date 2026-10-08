namespace AdoCli.Api;

using System.Net;
using System.Xml;
using System.Xml.Linq;

/// <summary>
/// A test step, or a reference to a shared steps work item (<see cref="SharedStepsId"/>, with the steps stored under it).
/// <see cref="Id"/> is the step's id inside the test case, which test results and attachments refer to; null for a new step.
/// <see cref="Action"/> and <see cref="ExpectedResult"/> are HTML, as Azure DevOps shows them.
/// </summary>
public sealed record TestStep(
    int? Id = null,
    string? Action = null,
    string? ExpectedResult = null,
    string? Description = null,
    int? SharedStepsId = null,
    List<TestStep>? Steps = null);

/// <summary>Reads and writes the XML of the Microsoft.VSTS.TCM.Steps field of a test case.</summary>
public static class TestSteps
{
    /// <summary>The reference name of the field that holds the steps.</summary>
    public const string Field = "Microsoft.VSTS.TCM.Steps";

    /// <summary>
    /// The steps in the field's XML, in order, and its "last" attribute (the highest step id ever given out). Plain-text
    /// strings (isformatted not true) are returned as HTML, so every step text has the same form.
    /// </summary>
    /// <exception cref="FormatException">The XML is malformed or holds an element other than step and compref.</exception>
    public static (List<TestStep> Steps, int Last) Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return ([], 0);
        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (XmlException e)
        {
            throw new FormatException($"the steps are not valid XML: {e.Message}");
        }
        return (Children(root), Int(root, "last") ?? 0);
    }

    static List<TestStep> Children(XElement parent) => parent.Elements().Select(Step).ToList();

    static TestStep Step(XElement e)
    {
        switch (e.Name.LocalName)
        {
            case "step":
                var texts = e.Elements("parameterizedString").Select(Text).ToList();
                var description = e.Element("description")?.Value;
                return new(Int(e, "id"), texts.ElementAtOrDefault(0) ?? "", texts.ElementAtOrDefault(1) ?? "",
                    string.IsNullOrEmpty(description) ? null : description);
            case "compref":
                var children = Children(e);
                return new(Int(e, "id"), SharedStepsId: Int(e, "ref"), Steps: children.Count > 0 ? children : null);
            default:
                // Rewriting the field would drop what we do not understand, so refuse instead.
                throw new FormatException($"the steps contain an unsupported element <{e.Name.LocalName}>");
        }
    }

    static string Text(XElement s) =>
        (string?)s.Attribute("isformatted") == "true" ? s.Value : WebUtility.HtmlEncode(s.Value).ReplaceLineEndings("<br>");

    static int? Int(XElement e, string attribute) => int.TryParse((string?)e.Attribute(attribute), out var n) ? n : null;

    /// <summary>
    /// The field's XML for <paramref name="steps"/>. Existing ids are kept; new steps get ids above both
    /// <paramref name="last"/> and every id in use, so an id is never reused. A step with an expected result is a
    /// ValidateStep, one without an ActionStep.
    /// </summary>
    public static string Build(IReadOnlyList<TestStep> steps, int last)
    {
        var next = Math.Max(last, MaxId(steps));
        XElement Element(TestStep s)
        {
            var id = s.Id ?? ++next;
            if (s.SharedStepsId is { } shared)
                return new XElement("compref", new XAttribute("id", id), new XAttribute("ref", shared), (s.Steps ?? []).Select(Element));
            return new XElement("step",
                new XAttribute("id", id),
                new XAttribute("type", string.IsNullOrEmpty(s.ExpectedResult) ? "ActionStep" : "ValidateStep"),
                new XElement("parameterizedString", new XAttribute("isformatted", "true"), s.Action ?? ""),
                new XElement("parameterizedString", new XAttribute("isformatted", "true"), s.ExpectedResult ?? ""),
                new XElement("description", s.Description ?? ""));
        }
        var elements = steps.Select(Element).ToList();
        return new XElement("steps", new XAttribute("id", 0), new XAttribute("last", next), elements).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>The highest step id among <paramref name="steps"/> and the steps nested in them; 0 if none.</summary>
    public static int MaxId(IEnumerable<TestStep> steps) =>
        steps.Select(s => Math.Max(s.Id ?? 0, MaxId(s.Steps ?? []))).DefaultIfEmpty(0).Max();

    /// <summary>Whether two step lists are the same: same order, ids, texts and shared steps references.</summary>
    public static bool Same(IReadOnlyList<TestStep> a, IReadOnlyList<TestStep> b) =>
        a.Count == b.Count && a.Zip(b).All(p => Same(p.First, p.Second));

    /// <summary>Whether two steps are the same, nested steps included; a missing text equals an empty one.</summary>
    public static bool Same(TestStep a, TestStep b) =>
        a.Id == b.Id && a.SharedStepsId == b.SharedStepsId
        && (a.Action ?? "") == (b.Action ?? "") && (a.ExpectedResult ?? "") == (b.ExpectedResult ?? "")
        && (a.Description ?? "") == (b.Description ?? "")
        && Same(a.Steps ?? [], b.Steps ?? []);
}
