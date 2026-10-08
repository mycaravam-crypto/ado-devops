namespace AdoCli.Cli;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdoCli.Api;

/// <summary>
/// The JSON file of <c>testplan export</c> and <c>testplan import</c>. <see cref="Plan"/> and <see cref="Suites"/> show
/// where the test cases are; import changes only <see cref="TestCases"/>, and only those listed.
/// </summary>
public sealed record TestPlanFile(string? Format, string? Project, PlanInfo? Plan, List<SuiteInfo>? Suites, List<TestCaseEntry>? TestCases)
{
    /// <summary>The value of "format"; a later, incompatible layout gets a new number.</summary>
    public const string CurrentFormat = "ado-testplan/1";

    // Readable for people editing the file: HTML and umlauts unescaped. Unknown properties are errors, so a typo such
    // as "expectedResults" is reported instead of silently ignored.
    static readonly JsonSerializerOptions Options = new(Json.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new LenientString() },
    };

    /// <summary>The file as indented JSON with a final newline.</summary>
    public string Write() => JsonSerializer.Serialize(this, Options).ReplaceLineEndings("\n") + "\n";

    /// <summary>Parses and validates a file; every problem is listed in one usage error naming <paramref name="name"/>.</summary>
    public static TestPlanFile Read(string json, string name)
    {
        TestPlanFile? file;
        try
        {
            file = JsonSerializer.Deserialize<TestPlanFile>(json, Options);
        }
        catch (JsonException e)
        {
            var where = e.LineNumber is { } line ? $" (line {line + 1}, {e.Path})" : "";
            // The serializer's message for an unknown property names .NET types; say it in the file's terms.
            var message = e.Message.Contains("could not be mapped") ? "unknown property; check its spelling" : e.Message.Split(" Path:")[0];
            throw AdoException.Usage($"{name} is not a valid test plan file{where}: {message}");
        }
        var errors = file is null ? ["the file is empty"] : file.Validate();
        if (errors.Count > 0)
            throw AdoException.Usage($"{name} is not a valid test plan file:\n  " + string.Join("\n  ", errors));
        return file!;
    }

    /// <summary>Everything wrong with the file, one message each, prefixed with where it is; empty when it is fine.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (Format != CurrentFormat)
            errors.Add($"format must be \"{CurrentFormat}\" (got {(Format is null ? "nothing" : $"\"{Format}\"")}); export the plan again with this version of ado");
        if (TestCases is null || TestCases.Count == 0)
        {
            errors.Add("testCases is missing or empty");
            return errors;
        }
        foreach (var (c, i) in TestCases.Select((c, i) => (c, i)))
        {
            var at = c.Id > 0 ? $"testCases[{i}] (#{c.Id})" : $"testCases[{i}]";
            if (c.Id <= 0)
                errors.Add($"{at}: id must be the id of an existing test case");
            else if (TestCases.Take(i).Any(o => o.Id == c.Id))
                errors.Add($"{at}: test case #{c.Id} is listed more than once");
            if (c.Rev <= 0)
                errors.Add($"{at}: rev must be the revision the test case was exported at");
            if (string.IsNullOrWhiteSpace(c.Title))
                errors.Add($"{at}: title must not be empty");
            foreach (var field in (c.Fields ?? []).Keys.Where(f => f is "System.Title" or "System.Rev" or "System.Id" or TestSteps.Field))
                errors.Add($"{at}: fields must not contain {field}; {(field == "System.Title" ? "use title" : field == TestSteps.Field ? "use steps" : "it cannot be changed")}");
            if (c.Steps is null)
                errors.Add($"{at}: steps is missing (use [] for a test case without steps)");
            else
                ValidateSteps(c.Steps, $"{at}: steps", new HashSet<int>(), nested: false, errors);
        }
        return errors;
    }

    static void ValidateSteps(List<TestStep> steps, string at, HashSet<int> ids, bool nested, List<string> errors)
    {
        foreach (var (s, i) in steps.Select((s, i) => (s, i)))
        {
            var here = $"{at}[{i}]";
            if (s is null)
            {
                errors.Add($"{here}: must be a step, not null");
                continue;
            }
            if (s.Id is { } id && (id <= 0 || !ids.Add(id)))
                errors.Add($"{here}: id {id} is {(id <= 0 ? "not a valid step id" : "used by another step")}; leave the id out for a new step");
            if (s.SharedStepsId is { } shared)
            {
                if (nested)
                    errors.Add($"{here}: shared steps cannot be nested in shared steps");
                if (shared <= 0)
                    errors.Add($"{here}: sharedStepsId must be the id of a Shared Steps work item");
                if (s.Action is not null || s.ExpectedResult is not null || s.Description is not null)
                    errors.Add($"{here}: a shared steps reference has no action, expectedResult or description; edit the Shared Steps work item #{shared} instead");
                ValidateSteps(s.Steps ?? [], $"{here}.steps", ids, nested: true, errors);
            }
            else if (s.Steps is not null)
                errors.Add($"{here}: only a shared steps reference (sharedStepsId) can contain steps");
            else if (s.Action is null)
                errors.Add($"{here}: action is missing");
        }
    }

    /// <summary>Reads strings, and numbers or booleans as their text, so "fields" may hold e.g. a priority as 2 or "2".</summary>
    sealed class LenientString : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.ValueSpan),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => throw new JsonException($"expected a string, got {reader.TokenType}"),
        };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}

/// <summary>The plan the file was exported from.</summary>
public sealed record PlanInfo(int Id, string Name, string? State, string? Iteration, int? RootSuiteId);

/// <summary>A suite and the test cases in it, in order; <see cref="ParentId"/> is null for the root suite.</summary>
public sealed record SuiteInfo(int Id, string Name, string? SuiteType, int? ParentId, List<int> TestCaseIds);

/// <summary>
/// A test case: its id, the revision it was exported at, its title, other fields by reference name (as text; identities as
/// "Name &lt;unique name&gt;") and its steps in order.
/// </summary>
public sealed record TestCaseEntry(int Id, int Rev, string? Title, Dictionary<string, string>? Fields, List<TestStep>? Steps);
