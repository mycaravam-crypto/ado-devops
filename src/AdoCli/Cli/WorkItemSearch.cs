namespace AdoCli.Cli;

using System.Text.RegularExpressions;
using AdoCli.Api;

/// <summary>The JSON document of <c>ado workitem search</c>, schema <see cref="WorkItemCommands.SearchSchema"/>.</summary>
public sealed record WorkItemSearch(string Schema, string Query, SearchCandidates Candidates, List<SearchHit> Items);

/// <summary>How many work items the server returned for ranking; <see cref="Truncated"/>: it returned <see cref="Max"/>, so matches may be missing.</summary>
public sealed record SearchCandidates(int Count, int Max, bool Truncated);

/// <summary>A ranked work item. <see cref="Match"/> is "id" for a direct id match, else "candidate": similar text, not a verified link.</summary>
public sealed record SearchHit(int Id, string Title, string Type, string State, string CreatedBy, string AssignedTo, double Score, string WebUrl, string Match);

public static partial class WorkItemCommands
{
    /// <summary>The schema name and version of the <c>workitem search</c> document.</summary>
    public const string SearchSchema = "ado.workitem-search/v1";

    /// <summary>The most work items the server returns for ranking, most recently changed first.</summary>
    public const int MaxCandidates = 500;

    /// <summary>The most title conditions in the WIQL query; the query's own trigrams come first.</summary>
    public const int MaxTitleConditions = 50;

    static readonly string[] SearchFields =
        ["System.Title", "System.WorkItemType", "System.State", "System.CreatedBy", "System.AssignedTo", "System.TeamProject"];

    /// <summary>
    /// ado workitem search &lt;query&gt;: work items whose title resembles the query, despite typos, partial words or another
    /// word order. The server picks candidates (titles sharing a trigram with the query, so it checks permissions), ado ranks them.
    /// </summary>
    public static async Task<int> SearchAsync(Context ctx)
    {
        var query = string.Join(' ', ctx.Args.Positional.Skip(2)).Trim();
        // Without a letter or digit there is nothing to search for, and no condition would return every work item.
        if (Words(query).Count == 0)
            throw AdoException.Usage("usage: ado workitem search <query> [--type <t>] [--state <s>] [--limit <n>] [--json]");
        var limit = ctx.Limit ?? 10;

        var filter = new WorkItemFilter
        {
            Everyone = true,
            Types = Values(ctx.Args, "--type"),
            // A search also finds closed items unless --state says otherwise.
            States = Values(ctx.Args, "--state") is { Count: > 0 } states ? states : ["any"],
            Wiql = SearchCondition(query),
        };
        var ids = await ctx.Client.QueryWorkItemIdsAsync(ctx.Project, filter, MaxCandidates);
        var candidates = await ctx.Client.GetWorkItemsAsync(ids, SearchFields);
        var result = BuildSearch(ctx.Client.Server, query, candidates, ids.Count, limit);
        if (ctx.Json)
            return Output.WriteJson(result);

        Output.Table(["ID", "TYPE", "STATE", "ASSIGNED TO", "SCORE", "TITLE"],
            result.Items.Select(h => new[] { h.Id.ToString(), h.Type, h.State, h.AssignedTo, h.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), h.Title }));
        Console.WriteLine();
        Console.WriteLine($"{result.Items.Count} of {result.Candidates.Count} candidates");
        if (result.Candidates.Truncated)
            Console.WriteLine($"Candidate limit of {MaxCandidates} reached; matches may be missing. Make the query more specific or add --type/--state/--project.");
        return 0;
    }

    /// <summary>The <paramref name="limit"/> best hits among <paramref name="candidates"/>: an id match first, then by score, then newest id.</summary>
    public static WorkItemSearch BuildSearch(string server, string query, IEnumerable<WorkItem> candidates, int candidateCount, int limit)
    {
        int? id = int.TryParse(query, out var n) ? n : null;
        var hits = candidates
            .Select(w => (Item: w, IsId: w.Id == id, Score: w.Id == id ? 1 : Score(query, w.Field("System.Title"))))
            .Where(h => h.IsId || h.Score > 0)
            .OrderByDescending(h => h.IsId)
            .ThenByDescending(h => h.Score)
            .ThenByDescending(h => h.Item.Id)
            .Take(limit)
            .Select(h => new SearchHit(
                h.Item.Id,
                h.Item.Field("System.Title"),
                h.Item.Field("System.WorkItemType"),
                h.Item.Field("System.State"),
                h.Item.Field("System.CreatedBy"),
                h.Item.Field("System.AssignedTo"),
                h.Score,
                h.Item.Field("System.TeamProject") is { Length: > 0 } project
                    ? $"{server}/{Uri.EscapeDataString(project)}/_workitems/edit/{h.Item.Id}"
                    : $"{server}/_workitems/edit/{h.Item.Id}",
                h.IsId ? "id" : "candidate"))
            .ToList();
        return new(SearchSchema, query, new(candidateCount, MaxCandidates, candidateCount >= MaxCandidates), hits);
    }

    /// <summary>
    /// The WIQL condition for candidates: the title contains a trigram of a query word or of that word with two neighbouring
    /// letters swapped (a swap breaks every trigram it touches), or the id is the query. Words under 3 letters are only used
    /// when there is no longer one.
    /// </summary>
    public static string SearchCondition(string query)
    {
        var words = Words(query);
        if (words.Any(w => w.Length >= 3))
            words = words.Where(w => w.Length >= 3).ToList();

        var own = Interleave(words.Select(w => Trigrams(w).ToList()));
        var swapped = Interleave(words.Select(w => Swaps(w).SelectMany(Trigrams).ToList()));
        var conditions = own.Concat(swapped)
            .Distinct()
            .Take(MaxTitleConditions)
            .Select(t => $"[System.Title] CONTAINS {AdoClient.Literal(t)}")
            .ToList();
        if (int.TryParse(query, out var id))
            conditions.Insert(0, $"[System.Id] = {id}");
        return string.Join(" OR ", conditions);
    }

    /// <summary>
    /// How much <paramref name="title"/> resembles <paramref name="query"/>, from 0 to 1: mostly how well each query word
    /// matches a title word (exact, prefix, a typo or two, contained), partly the trigrams both share. Word order does not matter.
    /// </summary>
    public static double Score(string query, string title)
    {
        var q = Words(query);
        var t = Words(title);
        if (q.Count == 0 || t.Count == 0)
            return 0;
        var words = q.Average(w => t.Max(tw => WordScore(w, tw)));
        var qt = q.SelectMany(Trigrams).ToHashSet();
        var tt = t.SelectMany(Trigrams).ToHashSet();
        var shared = qt.Count == 0 ? 0 : (double)qt.Intersect(tt).Count() / qt.Union(tt).Count();
        return Math.Round(0.8 * words + 0.2 * shared, 3);
    }

    static double WordScore(string q, string t)
    {
        if (q == t)
            return 1;
        if (t.StartsWith(q))
            return 0.9;
        // Typos allowed: none up to 3 letters, one up to 5, two from 6.
        var typos = q.Length <= 3 ? 0 : q.Length < 6 ? 1 : 2;
        if (typos > 0 && Distance(q, t) <= typos)
            return 0.8;
        if (typos > 0 && t.Length > q.Length && Distance(q, t[..q.Length]) <= typos)
            return 0.7;
        if (q.Length >= 3 && t.Contains(q))
            return 0.6;
        return 0;
    }

    /// <summary>Lower-case words: runs of letters and digits.</summary>
    public static List<string> Words(string text) =>
        NonWord().Split(text.ToLowerInvariant()).Where(w => w.Length > 0).ToList();

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex NonWord();

    /// <summary>The trigrams of a word, in order; a shorter word is its own only one.</summary>
    static IEnumerable<string> Trigrams(string word) =>
        word.Length <= 3 ? [word] : Enumerable.Range(0, word.Length - 2).Select(i => word.Substring(i, 3));

    static IEnumerable<string> Swaps(string word) =>
        Enumerable.Range(0, Math.Max(0, word.Length - 1))
            .Where(i => word[i] != word[i + 1])
            .Select(i => string.Concat(word.AsSpan(0, i), word.AsSpan(i + 1, 1), word.AsSpan(i, 1), word.AsSpan(i + 2)));

    // The first item of each list, then the second of each, ...: a long query word does not crowd out the others.
    static IEnumerable<string> Interleave(IEnumerable<List<string>> lists)
    {
        var all = lists.ToList();
        for (var i = 0; all.Any(l => i < l.Count); i++)
            foreach (var list in all.Where(l => i < l.Count))
                yield return list[i];
    }

    /// <summary>Edits (insert, delete, replace, swap two neighbours) from <paramref name="a"/> to <paramref name="b"/>; optimal string alignment.</summary>
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }
}
