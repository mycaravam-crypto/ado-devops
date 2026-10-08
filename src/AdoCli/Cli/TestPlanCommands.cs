namespace AdoCli.Cli;

using AdoCli.Api;

/// <summary>ado testplan list / export / import: test plans as one JSON file to edit outside Azure DevOps and import again.</summary>
public static class TestPlanCommands
{
    /// <summary>The fields besides title and steps that export writes; a file may add any other field to change it.</summary>
    public static readonly string[] MetadataFields =
    [
        "System.State", "System.AssignedTo", "System.AreaPath", "System.IterationPath", "System.Tags",
        "Microsoft.VSTS.Common.Priority", "System.Description",
    ];

    const string Title = "System.Title";
    const string Rev = "System.Rev";
    const string Type = "System.WorkItemType";

    /// <summary>ado testplan list: id, name, state and iteration of each test plan of the project.</summary>
    public static async Task<int> ListAsync(Context ctx)
    {
        var plans = await ctx.Client.GetTestPlansAsync(ctx.RequireProject(), ctx.Limit);
        if (ctx.Json)
            return Output.WriteJson(plans);
        Output.Table(["ID", "STATE", "ITERATION", "NAME"], plans.Select(p => new[] { p.Id.ToString(), p.State ?? "", p.Iteration ?? "", p.Name }));
        return 0;
    }

    /// <summary>Summarizes a test plan without downloading work-item fields or steps.</summary>
    public static async Task<TestPlanOverview> GetOverviewAsync(AdoClient client, string project, int id)
    {
        var plan = await client.GetTestPlanAsync(project, id);
        var suites = (await client.GetTestSuitesAsync(project, id))
            .OrderBy(s => s.Id).ToList();
        var views = new List<TestSuiteOverview>();
        var allCases = new HashSet<int>();
        foreach (var suite in suites)
        {
            var ids = await client.GetSuiteTestCaseIdsAsync(project, id, suite.Id);
            allCases.UnionWith(ids);
            views.Add(new(suite.Id, suite.Name, suite.SuiteType, suite.Parent?.Id,
                ids.Distinct().Count()));
        }
        return new(plan.Id, plan.Name, plan.State, plan.Iteration, plan.RootSuite?.Id,
            views, allCases.Count);
    }

    public sealed record TestSuiteOverview(int Id, string Name, string? SuiteType, int? ParentId, int TestCaseCount);
    public sealed record TestPlanOverview(int Id, string Name, string? State, string? Iteration,
        int? RootSuiteId, List<TestSuiteOverview> Suites, int UniqueTestCaseCount);

    /// <summary>ado testplan show &lt;id&gt;: plan metadata, hierarchy and distinct test-case count.</summary>
    public static async Task<int> ShowAsync(Context ctx)
    {
        var plan = await GetOverviewAsync(ctx.Client, ctx.RequireProject(),
            ctx.Id("testplan show <id>"));
        if (ctx.Json)
            return Output.WriteJson(plan);

        Console.WriteLine($"Test Plan #{plan.Id}: {plan.Name}");
        Console.WriteLine($"State:      {plan.State ?? ""}");
        Console.WriteLine($"Iteration:  {plan.Iteration ?? ""}");
        Console.WriteLine($"Root suite: {plan.RootSuiteId?.ToString() ?? "-"}");
        Console.WriteLine();
        Console.WriteLine("Suites:");
        var byParent = plan.Suites.GroupBy(s => s.ParentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).ToList());
        var visited = new HashSet<int>();
        void Print(TestSuiteOverview suite, int depth)
        {
            if (!visited.Add(suite.Id)) return;
            Console.WriteLine($"  {new string(' ', depth * 2)}{suite.Id}  {suite.Name} ({suite.TestCaseCount} cases)");
            if (byParent.TryGetValue(suite.Id, out var children))
                foreach (var child in children) Print(child, depth + 1);
        }
        foreach (var suite in plan.Suites.Where(s => s.ParentId is null || !plan.Suites.Any(p => p.Id == s.ParentId)))
            Print(suite, 0);
        foreach (var suite in plan.Suites)
            Print(suite, 0);
        Console.WriteLine();
        Console.WriteLine($"Suites: {plan.Suites.Count}; unique test cases: {plan.UniqueTestCaseCount}");
        Console.WriteLine($"Next: ado testplan export {plan.Id} --output plan.json");
        return 0;
    }

    /// <summary>ado testplan export &lt;id&gt; [--output file]: the plan as a <see cref="TestPlanFile"/>, on stdout without --output.</summary>
    public static async Task<int> ExportAsync(Context ctx)
    {
        var file = await ExportAsync(ctx.Client, ctx.RequireProject(), ctx.Id("testplan export <id> [--output plan.json]"));
        var json = file.Write();
        if (ctx.Args.Get("--output") is { } path)
        {
            File.WriteAllText(path, json);
            Console.Error.WriteLine($"Exported test plan #{file.Plan!.Id} ({file.Suites!.Count} suites, {file.TestCases!.Count} test cases) to {path}.");
        }
        else
            Console.Write(json);
        return 0;
    }

    /// <summary>Reads the plan, its suites, the test cases of each suite and every test case once, with its steps.</summary>
    public static async Task<TestPlanFile> ExportAsync(AdoClient client, string project, int planId)
    {
        var plan = await client.GetTestPlanAsync(project, planId);
        var suites = new List<SuiteInfo>();
        foreach (var s in await client.GetTestSuitesAsync(project, planId))
            suites.Add(new(s.Id, s.Name, s.SuiteType, s.Parent?.Id, await client.GetSuiteTestCaseIdsAsync(project, planId, s.Id)));

        // A test case can be in several suites; it is written once.
        var ids = suites.SelectMany(s => s.TestCaseIds).Distinct().ToList();
        var items = await client.GetWorkItemsAsync(ids, [Title, Rev, TestSteps.Field, .. MetadataFields]);
        var cases = items.Select(w => new TestCaseEntry(
            w.Id, RevOf(w), w.Field(Title),
            MetadataFields.ToDictionary(f => f, w.Value),
            StepsOf(w).Steps)).ToList();
        return new(TestPlanFile.CurrentFormat, project, new(plan.Id, plan.Name, plan.State, plan.Iteration, plan.RootSuite?.Id), suites, cases);
    }

    static int RevOf(WorkItem w) => int.TryParse(w.Field(Rev), out var rev) ? rev : 0;

    static (List<TestStep> Steps, int Last) StepsOf(WorkItem w)
    {
        try
        {
            return TestSteps.Parse(w.Field(TestSteps.Field));
        }
        catch (FormatException e)
        {
            throw new AdoException($"test case #{w.Id}: cannot read {TestSteps.Field}: {e.Message}");
        }
    }

    /// <summary>
    /// ado testplan import &lt;file&gt; [--dry-run] [--yes]: updates the test cases whose title, fields or steps were
    /// edited in the file. Nothing is written when a test case is missing or was changed on the server since the export.
    /// </summary>
    public static async Task<int> ImportAsync(Context ctx)
    {
        var a = ctx.Args;
        var path = a.At(2) ?? throw AdoException.Usage("usage: ado testplan import <file> [--dry-run] [--yes]");
        if (!File.Exists(path))
            throw AdoException.Usage($"file not found: {path}");
        var file = TestPlanFile.Read(File.ReadAllText(path), path);
        var import = await PlanImportAsync(ctx.Client, file);
        var dryRun = a.Has("--dry-run");

        if (dryRun && ctx.Json)
        {
            Output.WriteJson(new
            {
                updates = import.Updates.Select(u => new { u.Id, u.Rev, u.Title, u.Changes }),
                import.Unchanged,
                import.Conflicts,
                import.Errors,
            });
            return ExitCode(import);
        }

        var to = dryRun ? Console.Out : Console.Error;
        Summary(to, import);
        if (import.Errors.Count > 0 || import.Conflicts.Count > 0)
        {
            Console.Error.WriteLine(import.Conflicts.Count > 0
                ? "Nothing was imported. Export the plan again and redo these edits, or remove these test cases from the file."
                : "Nothing was imported.");
            return ExitCode(import);
        }
        if (dryRun)
        {
            to.WriteLine("Dry run: nothing was changed.");
            return 0;
        }
        if (import.Updates.Count == 0)
            return 0;

        if (import.Updates.Count > 1 && !a.Has("--yes") && !a.Has("-y"))
        {
            if (Console.IsInputRedirected)
                throw AdoException.Usage($"pass --yes to update {import.Updates.Count} test cases; there is no terminal to confirm on");
            if (!Term.Confirm($"Update {import.Updates.Count} test cases? [y/N] "))
            {
                Console.Error.WriteLine("Aborted.");
                return AdoException.General;
            }
        }

        var failed = 0;
        foreach (var u in import.Updates)
        {
            try
            {
                // The rev test makes the server refuse the update if someone changed the test case since we looked.
                var w = await ctx.Client.UpdateWorkItemAsync(u.Id, u.Fields, u.Rev);
                Console.Error.WriteLine($"Updated test case #{u.Id} (rev {u.Rev} -> {RevOf(w)}): {u.Title}");
            }
            catch (AdoException e)
            {
                // One rejected test case (an invalid state, say) should not stop the rest.
                Console.Error.WriteLine($"#{u.Id}: {e.Message}");
                failed++;
            }
        }
        Console.Error.WriteLine(failed > 0
            ? $"{failed} of {import.Updates.Count} test cases were not updated."
            : "Export the plan again before editing further, so the file has the new revisions.");
        return failed > 0 ? AdoException.General : 0;
    }

    static int ExitCode(Import import) =>
        import.Errors.Count > 0 ? AdoException.General : import.Conflicts.Count > 0 ? AdoException.Conflict : 0;

    static void Summary(TextWriter to, Import import)
    {
        foreach (var error in import.Errors)
            to.WriteLine($"Error: {error}");
        foreach (var conflict in import.Conflicts)
            to.WriteLine($"Conflict: {conflict}");
        var n = import.Updates.Count;
        to.WriteLine($"{(n == 1 ? "1 test case" : $"{n} test cases")} to update, {import.Unchanged} unchanged.");
        foreach (var u in import.Updates)
        {
            to.WriteLine($"#{u.Id} (rev {u.Rev}) {u.Title}");
            foreach (var change in u.Changes)
                to.WriteLine($"  {change}");
        }
    }

    /// <summary>What import would do: updates per test case, the number of unchanged ones, conflicts and errors.</summary>
    public sealed record Import(List<Update> Updates, int Unchanged, List<string> Conflicts, List<string> Errors);

    /// <summary>The fields to set on a test case at revision <see cref="Rev"/>, and a readable line per change.</summary>
    public sealed record Update(int Id, int Rev, string Title, Dictionary<string, string> Fields, List<string> Changes);

    /// <summary>
    /// Compares each test case in the file with the revision it was exported at, so only what was edited in the file
    /// counts as a change; edits to a test case that has a newer revision on the server are conflicts. Writes nothing.
    /// </summary>
    public static async Task<Import> PlanImportAsync(AdoClient client, TestPlanFile file)
    {
        var entries = file.TestCases!;
        var fields = new[] { Title, Rev, Type, TestSteps.Field }.Concat(MetadataFields)
            .Concat(entries.SelectMany(e => (IEnumerable<string>?)e.Fields?.Keys ?? [])).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var current = (await client.GetWorkItemsAsync(entries.Select(e => e.Id), fields)).ToDictionary(w => w.Id);

        var updates = new List<Update>();
        var conflicts = new List<string>();
        var errors = new List<string>();
        var unchanged = 0;
        foreach (var entry in entries)
        {
            if (!current.TryGetValue(entry.Id, out var now))
            {
                errors.Add($"#{entry.Id}: not found (deleted, or you may not see it)");
                continue;
            }
            if (now.Field(Type) is var type && type != "Test Case")
            {
                errors.Add($"#{entry.Id}: is a {type}, not a Test Case");
                continue;
            }
            var currentRev = RevOf(now);
            if (entry.Rev > currentRev)
            {
                errors.Add($"#{entry.Id}: rev {entry.Rev} does not exist yet (the test case is at rev {currentRev})");
                continue;
            }
            var exported = entry.Rev == currentRev ? now : await client.GetWorkItemRevisionAsync(entry.Id, entry.Rev);

            (Dictionary<string, string> Fields, List<string> Changes) diff;
            try
            {
                diff = Diff(entry, exported);
            }
            catch (AdoException e)
            {
                errors.Add(e.Message);
                continue;
            }
            if (diff.Changes.Count == 0)
                unchanged++;
            else if (currentRev != entry.Rev)
                conflicts.Add($"#{entry.Id} {entry.Title}: changed on the server since the export (rev {entry.Rev} -> {currentRev}); edited in the file: {string.Join("; ", diff.Changes)}");
            else
                updates.Add(new(entry.Id, entry.Rev, entry.Title!, diff.Fields, diff.Changes));
        }
        return new(updates, unchanged, conflicts, errors);
    }

    /// <summary>The fields that differ between the file's test case and the work item as exported, and a line per change.</summary>
    public static (Dictionary<string, string> Fields, List<string> Changes) Diff(TestCaseEntry entry, WorkItem exported)
    {
        var fields = new Dictionary<string, string>();
        var changes = new List<string>();
        void Compare(string name, string label, string value)
        {
            var old = exported.Value(name);
            if (value == old)
                return;
            fields[name] = value;
            changes.Add($"{label}: \"{Short(old)}\" -> \"{Short(value)}\"");
        }

        Compare(Title, "title", entry.Title!);
        foreach (var (name, value) in entry.Fields ?? [])
            Compare(name, name, value ?? "");

        var (steps, last) = StepsOf(exported);
        var stepChanges = StepChanges(entry.Id, steps, entry.Steps!);
        if (stepChanges.Count > 0)
        {
            fields[TestSteps.Field] = TestSteps.Build(entry.Steps!, last);
            changes.AddRange(stepChanges);
        }
        return (fields, changes);
    }

    /// <summary>A line per added, changed, moved or removed step, numbered as Azure DevOps shows them (from 1).</summary>
    public static List<string> StepChanges(int testCaseId, List<TestStep> before, List<TestStep> after)
    {
        if (TestSteps.Same(before, after))
            return [];
        var known = Flatten(before).Where(s => s.Id is not null).ToDictionary(s => s.Id!.Value);
        foreach (var s in Flatten(after))
            if (s.Id is { } id && !known.ContainsKey(id))
                throw new AdoException($"#{testCaseId}: step id {id} does not exist in this test case; leave the id out to add a new step");

        var changes = new List<string>();
        var position = before.Select((s, i) => (s.Id, i)).Where(p => p.Id is not null).ToDictionary(p => p.Id!.Value, p => p.i);
        foreach (var (s, i) in after.Select((s, i) => (s, i)))
        {
            var label = s.SharedStepsId is { } shared ? $"shared steps #{shared} (step {i + 1})" : $"step {i + 1}";
            if (s.Id is not { } id)
                changes.Add($"{label}: added");
            else if (!TestSteps.Same(known[id], s))
                changes.Add($"{label}: {What(known[id], s)} changed" + (position.GetValueOrDefault(id, -1) is var was && was >= 0 && was != i ? $" (was step {was + 1})" : ""));
            else if (position.GetValueOrDefault(id, -1) is var was && was != i)
                changes.Add(was < 0 ? $"{label}: moved out of shared steps" : $"{label}: moved (was step {was + 1})");
        }
        var kept = Flatten(after).Select(s => s.Id).OfType<int>().ToHashSet();
        foreach (var (s, i) in before.Select((s, i) => (s, i)).Where(p => p.s.Id is { } id && !kept.Contains(id)))
            changes.Add($"removed (was step {i + 1}): {Short(s.Action ?? $"shared steps #{s.SharedStepsId}")}");
        return changes;
    }

    static IEnumerable<TestStep> Flatten(IEnumerable<TestStep> steps) => steps.SelectMany(s => Flatten(s.Steps ?? []).Prepend(s));

    static string What(TestStep a, TestStep b) => string.Join(", ", new[]
    {
        (a.Action ?? "") != (b.Action ?? "") ? "action" : null,
        (a.ExpectedResult ?? "") != (b.ExpectedResult ?? "") ? "expected result" : null,
        (a.Description ?? "") != (b.Description ?? "") ? "description" : null,
        a.SharedStepsId != b.SharedStepsId || !TestSteps.Same(a.Steps ?? [], b.Steps ?? []) ? "shared steps" : null,
    }.OfType<string>());

    static string Short(string text) => Output.Truncate(text.ReplaceLineEndings(" "), 60);
}
