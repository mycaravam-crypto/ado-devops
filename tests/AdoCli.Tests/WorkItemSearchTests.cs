namespace AdoCli.Tests;

using System.Text.Json.Nodes;
using AdoCli.Api;
using AdoCli.Cli;

[Collection("Console output tests")]
public class WorkItemSearchTests
{
    [Theory]
    [InlineData("sevrer", "Server crashes on start")]          // swapped letters
    [InlineData("login fehler", "Fehler beim Login")]         // other word order
    [InlineData("auth", "Authentication for API")]            // partial word
    [InlineData("atuh token", "Authentication token expires")] // typo in a partial word
    [InlineData("Datenbnk", "Datenbank Migration")]           // missing letter
    public void TolerantQueriesRankTheIntendedTitleFirst(string query, string intended)
    {
        string[] others = ["Release notes for 2.0", "Fix layout of the settings page", "Upgrade build agents"];
        var best = others.Append(intended).MaxBy(t => WorkItemCommands.Score(query, t));

        Assert.Equal(intended, best);
        Assert.True(WorkItemCommands.Score(query, intended) > 0.5);
    }

    [Fact]
    public void UnrelatedTitlesScoreZero()
    {
        Assert.Equal(0, WorkItemCommands.Score("server", "Release notes"));
        Assert.Equal(0, WorkItemCommands.Score("bug", "big change"));   // short words need an exact or prefix match
        Assert.Equal(0, WorkItemCommands.Score("", "anything"));
    }

    [Fact]
    public void ExactMatchesScoreHigherThanTypos()
    {
        Assert.True(WorkItemCommands.Score("server", "Server") > WorkItemCommands.Score("sevrer", "Server"));
        Assert.Equal(1, WorkItemCommands.Score("Login Fehler", "fehler: login"));
    }

    [Theory]
    [InlineData("server", "server", 0)]
    [InlineData("sevrer", "server", 1)]
    [InlineData("servr", "server", 1)]
    [InlineData("srever", "server", 1)]
    [InlineData("kitten", "sitting", 3)]
    public void DistanceCountsSwapsAsOneEdit(string a, string b, int expected) =>
        Assert.Equal(expected, WorkItemCommands.Distance(a, b));

    [Fact]
    public void ConditionHasTheQueryTrigramsThenThoseOfSwappedLetters()
    {
        var condition = WorkItemCommands.SearchCondition("sevrer");

        Assert.StartsWith("[System.Title] CONTAINS 'sev' OR [System.Title] CONTAINS 'evr' OR [System.Title] CONTAINS 'vre' OR [System.Title] CONTAINS 'rer' OR ", condition);
        // "sevrer" shares no trigram with "server"; its swapped variant "server" does.
        Assert.Contains("[System.Title] CONTAINS 'ser'", condition);
        Assert.DoesNotContain("[System.Id]", condition);
    }

    [Fact]
    public void ConditionInterleavesWordsDropsShortOnesAndIsCapped()
    {
        var condition = WorkItemCommands.SearchCondition("a don't Authentication Configuration");
        var parts = condition.Split(" OR ");

        // "a" and the "t" of "don't" are left out; each word gets its first trigram before any gets its second.
        Assert.Equal(["[System.Title] CONTAINS 'don'", "[System.Title] CONTAINS 'aut'", "[System.Title] CONTAINS 'con'", "[System.Title] CONTAINS 'uth'"], parts[..4]);
        Assert.Equal(WorkItemCommands.MaxTitleConditions, parts.Length);
        Assert.Equal(parts.Length, parts.Distinct().Count());
    }

    [Fact]
    public void NumericQueryAlsoMatchesTheId() =>
        Assert.StartsWith("[System.Id] = 4711 OR [System.Title] CONTAINS '471'", WorkItemCommands.SearchCondition("4711"));

    const string Ids = """{"workItems":[{"id":1},{"id":2},{"id":3},{"id":4711}]}""";

    const string Items = """
        {"count":4,"value":[
          {"id":1,"fields":{"System.Title":"Server crashes on start","System.WorkItemType":"Bug","System.State":"Closed","System.TeamProject":"Platform",
            "System.CreatedBy":{"displayName":"Ann Lee","uniqueName":"ann"},"System.AssignedTo":{"displayName":"Bo Chen","uniqueName":"bo"}}},
          {"id":2,"fields":{"System.Title":"Server crashes on start","System.WorkItemType":"Bug","System.State":"Active","System.TeamProject":"Platform"}},
          {"id":3,"fields":{"System.Title":"Release notes","System.WorkItemType":"Task","System.State":"New","System.TeamProject":"Platform"}},
          {"id":4711,"fields":{"System.Title":"Unrelated","System.WorkItemType":"Task","System.State":"New","System.TeamProject":"Platform"}}]}
        """;

    [Fact]
    public async Task JsonRanksVisibleCandidatesAndAsksTheServerForThem()
    {
        var (output, stub) = await RunAsync(Ids, Items, "workitem", "search", "sevrer", "crash", "--project", "Platform", "--type", "Bug", "--json");
        var json = JsonNode.Parse(output)!;

        Assert.Equal("https://tfs/Platform/_apis/wit/wiql?$top=500&api-version=5.0", stub.Requests[0].Url);
        var wiql = (string?)JsonNode.Parse(stub.Requests[0].Body!)!["query"];
        Assert.Contains("[System.WorkItemType] IN ('Bug') AND ([System.Title] CONTAINS 'sev' OR ", wiql);
        Assert.DoesNotContain("[System.State]", wiql);       // a search finds closed items too
        Assert.DoesNotContain("[System.AssignedTo]", wiql);  // and everyone's
        Assert.Contains("ids=1,2,3,4711&fields=System.Title,System.WorkItemType,System.State,System.CreatedBy,System.AssignedTo,System.TeamProject", stub.Requests[1].Url);

        Assert.Equal(WorkItemCommands.SearchSchema, (string?)json["schema"]);
        Assert.Equal("sevrer crash", (string?)json["query"]);
        Assert.Equal(4, (int)json["candidates"]!["count"]!);
        Assert.False((bool)json["candidates"]!["truncated"]!);

        var items = json["items"]!.AsArray();
        // Same score: the newer id first; titles without a matching word are left out.
        Assert.Equal([2, 1], items.Select(i => (int)i!["id"]!));
        Assert.Equal("Ann Lee", (string?)items[1]!["createdBy"]);
        Assert.Equal("Bo Chen", (string?)items[1]!["assignedTo"]);
        Assert.Equal("Closed", (string?)items[1]!["state"]);
        Assert.Equal("https://tfs/Platform/_workitems/edit/1", (string?)items[1]!["webUrl"]);
        Assert.Equal("candidate", (string?)items[0]!["match"]);
        Assert.InRange((double)items[0]!["score"]!, 0.5, 1);
    }

    [Fact]
    public async Task IdMatchComesFirstAndLimitApplies()
    {
        var (output, _) = await RunAsync(Ids, Items, "workitem", "search", "4711", "--limit", "1", "--json");

        var item = Assert.Single(JsonNode.Parse(output)!["items"]!.AsArray());
        Assert.Equal(4711, (int)item!["id"]!);
        Assert.Equal("id", (string?)item["match"]);
    }

    [Fact]
    public async Task TableShowsScoresAndWhenTheCandidateLimitWasReached()
    {
        var ids = "{\"workItems\":[" + string.Join(',', Enumerable.Range(1, WorkItemCommands.MaxCandidates).Select(i => $"{{\"id\":{i}}}")) + "]}";
        var (output, _) = await RunAsync(ids, Items, "workitem", "search", "server");

        Assert.Contains("SCORE", output);
        Assert.Contains("Server crashes on start", output);
        Assert.Contains("2 of 500 candidates", output);
        Assert.Contains("Candidate limit of 500 reached", output);
    }

    [Theory]
    [InlineData]
    [InlineData("!!!")]
    public async Task QueryNeedsALetterOrDigit(params string[] query)
    {
        var stub = new StubHandler(System.Net.HttpStatusCode.OK);
        var e = await Assert.ThrowsAsync<AdoException>(() => WorkItemCommands.SearchAsync(Ctx(stub, ["workitem", "search", .. query])));
        Assert.Contains("usage: ado workitem search", e.Message);
        Assert.Empty(stub.Requests);
    }

    static async Task<(string Output, StubHandler Stub)> RunAsync(string ids, string items, params string[] argv)
    {
        // The work items request is split into chunks of 200 ids; only the first chunk returns items.
        var stub = new StubHandler(n => StubHandler.Json(n switch { 0 => ids, 1 => items, _ => """{"count":0,"value":[]}""" }));
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            Assert.Equal(0, await WorkItemCommands.SearchAsync(Ctx(stub, argv)));
        }
        finally { Console.SetOut(original); }
        return (writer.ToString(), stub);
    }

    static Context Ctx(StubHandler stub, params string[] argv) =>
        new(Args.Parse(argv), new Config(), new AdoClient("https://tfs", "pat", handler: stub));
}
