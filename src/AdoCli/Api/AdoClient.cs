namespace AdoCli.Api;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AdoCli.Cli;

/// <summary>The one Azure DevOps Server REST client. All URLs are built by <see cref="Url"/>.</summary>
public sealed class AdoClient
{
    /// <summary>Supported by Azure DevOps Server 2019 and later; every command works with it.</summary>
    public const string DefaultApiVersion = "5.0";

    readonly HttpClient _http;
    readonly string _server;
    readonly string _apiVersion;
    readonly bool _debug;

    public const string CredentialsInUrl = "the server URL must not contain credentials; log in with a personal access token instead";

    public AdoClient(string server, string pat, bool debug = false, HttpMessageHandler? handler = null, string apiVersion = DefaultApiVersion)
    {
        // Every URL is built from the server and printed by --debug, so it must not carry a secret.
        if (Uri.TryCreate(server, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0)
            throw AdoException.Usage(CredentialsInUrl);
        _server = server.TrimEnd('/');
        _apiVersion = apiVersion;
        _debug = debug;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ado-cli", Program.Version));
    }

    /// <summary>{server}[/{project}]/_apis/{path}?{query}&amp;api-version=...</summary>
    public string Url(string? project, string path, string query = "", string? apiVersion = null)
    {
        var scope = project is null ? "" : "/" + Uri.EscapeDataString(project);
        var q = query.Length > 0 ? query + "&" : "";
        return $"{_server}{scope}/_apis/{path}?{q}api-version={apiVersion ?? _apiVersion}";
    }

    // connectionData only exists as a preview API, so it is called without api-version.
    public Task<ConnectionData> GetConnectionDataAsync() =>
        SendAsync<ConnectionData>(HttpMethod.Get, $"{_server}/_apis/connectionData");

    public async Task<List<Repository>> GetRepositoriesAsync(string? project) =>
        (await SendAsync<ListResponse<Repository>>(HttpMethod.Get, Url(project, "git/repositories"))).Value;

    public Task<Repository> GetRepositoryAsync(string project, string repo) =>
        SendAsync<Repository>(HttpMethod.Get, Url(project, $"git/repositories/{Uri.EscapeDataString(repo)}"));

    /// <summary>Pull requests of a repository, or of the whole project when <paramref name="repo"/> is null; all of them unless <paramref name="limit"/> is set.</summary>
    public Task<List<PullRequest>> GetPullRequestsAsync(string project, string? repo, string status, Guid? creatorId, int? limit = null)
    {
        var path = repo is null ? "git/pullrequests" : $"git/repositories/{Uri.EscapeDataString(repo)}/pullrequests";
        var query = $"searchCriteria.status={Uri.EscapeDataString(status)}";
        if (creatorId is { } id)
            query += $"&searchCriteria.creatorId={id}";
        return PageAsync<PullRequest>(project, path, query, limit);
    }

    /// <summary>A pull request by id; ids are unique per collection, so no project or repository is needed.</summary>
    public Task<PullRequest> GetPullRequestAsync(int id) =>
        SendAsync<PullRequest>(HttpMethod.Get, Url(null, $"git/pullrequests/{id}"));

    /// <summary>Creates a pull request; <paramref name="source"/> and <paramref name="target"/> are full refs (refs/heads/...).</summary>
    public Task<PullRequest> CreatePullRequestAsync(string project, string repo, string source, string target, string title, string description) =>
        SendAsync<PullRequest>(HttpMethod.Post, Url(project, $"git/repositories/{Uri.EscapeDataString(repo)}/pullrequests"),
            new { sourceRefName = source, targetRefName = target, title, description });

    /// <summary>Sets the vote of <paramref name="reviewerId"/>; 10 = approve.</summary>
    public Task<Reviewer> VoteAsync(PullRequest pr, Guid reviewerId, int vote) =>
        SendAsync<Reviewer>(HttpMethod.Put, PullRequestUrl(pr, $"/reviewers/{reviewerId}"), new { vote });

    /// <summary>Completes (merges) the PR at the source commit the caller saw.</summary>
    public Task<PullRequest> CompletePullRequestAsync(PullRequest pr, bool squash) =>
        SendAsync<PullRequest>(HttpMethod.Patch, PullRequestUrl(pr), new
        {
            status = "completed",
            lastMergeSourceCommit = pr.LastMergeSourceCommit,
            completionOptions = new { squashMerge = squash },
        });

    string PullRequestUrl(PullRequest pr, string suffix = "", string query = "") =>
        Url(pr.Repository.Project.Name, $"git/repositories/{pr.Repository.Id}/pullrequests/{pr.PullRequestId}{suffix}", query);

    /// <summary>All commits of the PR; newer servers return them in pages linked by a continuation token.</summary>
    public async Task<List<Commit>> GetPullRequestCommitsAsync(PullRequest pr)
    {
        var all = new List<Commit>();
        string? token = null;
        do
        {
            var query = token is null ? "" : $"continuationToken={Uri.EscapeDataString(token)}";
            (var page, token) = await SendPageAsync<ListResponse<Commit>>(PullRequestUrl(pr, "/commits", query));
            all.AddRange(page.Value);
        } while (token is not null);
        return all;
    }

    public async Task<List<int>> GetPullRequestWorkItemIdsAsync(PullRequest pr) =>
        (await SendAsync<ListResponse<ResourceRef>>(HttpMethod.Get, PullRequestUrl(pr, "/workitems"))).Value.Select(r => int.Parse(r.Id)).Order().ToList();

    /// <summary>Files changed between the merge base of target and source, like git diff target...source; empty until the server computed merge commits.</summary>
    public async Task<List<Change>> GetPullRequestChangesAsync(PullRequest pr)
    {
        if (pr.LastMergeSourceCommit is null || pr.LastMergeTargetCommit is null)
            return [];
        const int pageSize = 2000;
        var query = $"baseVersion={pr.LastMergeTargetCommit.CommitId}&baseVersionType=commit" +
            $"&targetVersion={pr.LastMergeSourceCommit.CommitId}&targetVersionType=commit&$top={pageSize}";
        var all = new List<GitChange>();
        List<GitChange> page;
        do
        {
            var url = Url(pr.Repository.Project.Name, $"git/repositories/{pr.Repository.Id}/diffs/commits", $"{query}&$skip={all.Count}");
            page = (await SendAsync<CommitDiffs>(HttpMethod.Get, url)).Changes;
            all.AddRange(page);
        } while (page.Count == pageSize);
        return all.Where(c => !c.Item.IsFolder).Select(c => new Change(c.Item.Path, c.ChangeType, c.OriginalPath)).OrderBy(c => c.Path, StringComparer.Ordinal).ToList();
    }

    public Task<WorkItem> GetWorkItemAsync(int id) =>
        SendAsync<WorkItem>(HttpMethod.Get, Url(null, $"wit/workitems/{id}"));

    /// <summary>
    /// Work items matching <paramref name="filter"/> (default: open ones assigned to the current user), most recently changed
    /// first; all of them (the server caps a query at 20000) unless <paramref name="limit"/> is set.
    /// </summary>
    public async Task<List<WorkItem>> QueryWorkItemsAsync(string? project, WorkItemFilter? filter = null, int? limit = null) =>
        await GetWorkItemsAsync(await QueryWorkItemIdsAsync(project, filter, limit), [.. SummaryFields, .. ListFields]);

    /// <summary>Only the ids of <see cref="QueryWorkItemsAsync"/>: one request, no fields.</summary>
    public async Task<List<int>> QueryWorkItemIdsAsync(string? project, WorkItemFilter? filter = null, int? limit = null)
    {
        var wiql = Wiql(filter ?? new(), project is not null);
        var result = await SendAsync<WiqlResult>(HttpMethod.Post, Url(project, "wit/wiql", limit is { } top ? $"$top={top}" : ""), new { query = wiql });
        return result.WorkItems.Select(w => w.Id).ToList();
    }

    /// <summary>The WIQL query for <paramref name="filter"/>; every condition is ANDed, values are quoted.</summary>
    public static string Wiql(WorkItemFilter filter, bool inProject)
    {
        var where = new List<string>();
        if (filter.AssignedTo is { } who)
            where.Add(who.Equals("@me", StringComparison.OrdinalIgnoreCase) ? "[System.AssignedTo] = @Me" : $"[System.AssignedTo] = {Literal(who)}");
        else if (!filter.Everyone)
            where.Add("[System.AssignedTo] = @Me");
        // --state any: no condition on the state at all.
        if (!filter.States.Any(s => s.Equals("any", StringComparison.OrdinalIgnoreCase)))
            where.Add(filter.States.Count > 0 ? $"[System.State] IN ({WiqlList(filter.States)})" : "[System.State] NOT IN ('Closed', 'Done', 'Removed')");
        if (filter.Types.Count > 0)
            where.Add($"[System.WorkItemType] IN ({WiqlList(filter.Types)})");
        if (filter.Area is { } area)
            where.Add($"[System.AreaPath] UNDER {Literal(area)}");
        if (filter.Iteration is { } iteration)
            where.Add($"[System.IterationPath] UNDER {Literal(iteration)}");
        foreach (var tag in filter.Tags)
            where.Add($"[System.Tags] CONTAINS {Literal(tag)}");
        if (filter.TitleContains is { } title)
            where.Add($"[System.Title] CONTAINS {Literal(title)}");
        if (filter.Contains is { } text)
            where.Add($"([System.Title] CONTAINS {Literal(text)} OR [System.Description] CONTAINS {Literal(text)})");
        if (filter.Wiql is { Length: > 0 } raw)
            where.Add($"({raw})");
        if (inProject)
            where.Add("[System.TeamProject] = @project");
        return "SELECT [System.Id] FROM WorkItems"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY [System.ChangedDate] DESC";
    }

    // Enough to name a work item: what callers get unless they ask for more.
    static readonly string[] SummaryFields = ["System.Title", "System.WorkItemType", "System.State"];

    // What `workitem list` shows besides the summary fields.
    static readonly string[] ListFields = ["Microsoft.VSTS.Common.Priority", "System.IterationPath", "System.ChangedDate"];

    // WIQL string literals are single-quoted; a quote inside one is doubled.
    static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

    static string WiqlList(IEnumerable<string> values) => string.Join(", ", values.Select(Literal));

    /// <summary>
    /// The given work items, in the given order, with <paramref name="fields"/> (default: title, type and state);
    /// ones that are deleted or not visible are left out.
    /// </summary>
    public async Task<List<WorkItem>> GetWorkItemsAsync(IEnumerable<int> ids, IEnumerable<string>? fields = null)
    {
        var fieldList = string.Join(',', fields ?? SummaryFields);
        // The server accepts at most 200 ids per request.
        var all = new List<WorkItem>();
        foreach (var chunk in ids.Chunk(200))
        {
            var query = $"ids={string.Join(',', chunk)}&fields={fieldList}&errorPolicy=omit";
            all.AddRange((await SendAsync<ListResponse<WorkItem?>>(HttpMethod.Get, Url(null, "wit/workitems", query))).Value.OfType<WorkItem>());
        }
        return all;
    }

    /// <summary>Creates a work item of <paramref name="type"/> (e.g. Bug, Task, "User Story") with the given fields, keyed by reference name.</summary>
    public Task<WorkItem> CreateWorkItemAsync(string project, string type, IReadOnlyDictionary<string, string> fields) =>
        SendAsync<WorkItem>(HttpMethod.Post, Url(project, $"wit/workitems/${Uri.EscapeDataString(type)}"), FieldPatch(fields), JsonPatch);

    /// <summary>
    /// Sets the given fields of a work item; fields not listed stay as they are. With <paramref name="expectedRev"/> the
    /// server refuses the update if the work item is no longer at that revision.
    /// </summary>
    public Task<WorkItem> UpdateWorkItemAsync(int id, IReadOnlyDictionary<string, string> fields, int? expectedRev = null)
    {
        object[] patch = FieldPatch(fields);
        if (expectedRev is { } rev)
            patch = [new { op = "test", path = "/rev", value = rev }, .. patch];
        return SendAsync<WorkItem>(HttpMethod.Patch, Url(null, $"wit/workitems/{id}"), patch, JsonPatch);
    }

    /// <summary>A work item as it was at revision <paramref name="rev"/>, with every field it had then.</summary>
    public Task<WorkItem> GetWorkItemRevisionAsync(int id, int rev) =>
        SendAsync<WorkItem>(HttpMethod.Get, Url(null, $"wit/workitems/{id}/revisions/{rev}"));

    const string JsonPatch = "application/json-patch+json";

    // "add" on a field sets it, whether or not it has a value yet.
    static object[] FieldPatch(IReadOnlyDictionary<string, string> fields) =>
        fields.Select(f => (object)new { op = "add", path = "/fields/" + f.Key, value = f.Value }).ToArray();

    // The legacy "test" area uses REST API 5.0 on Azure DevOps Server 2020 (Dev18.M170).
    // Keep this independent of the global version used by Git, WIT and Build endpoints.
    const string LegacyTestApiVersion = "5.0";

    /// <summary>Test plans of the project; all of them unless <paramref name="limit"/> is set.</summary>
    public Task<List<TestPlan>> GetTestPlansAsync(string project, int? limit = null) =>
        PageAsync<TestPlan>(project, "test/plans", "", limit, apiVersion: LegacyTestApiVersion);

    public Task<TestPlan> GetTestPlanAsync(string project, int planId) =>
        SendAsync<TestPlan>(HttpMethod.Get, Url(project, $"test/plans/{planId}", apiVersion: LegacyTestApiVersion));

    /// <summary>Every suite of the plan, the root suite included, flat; each names its parent.</summary>
    public Task<List<TestSuite>> GetTestSuitesAsync(string project, int planId) =>
        PageAsync<TestSuite>(project, $"test/plans/{planId}/suites", "", null, apiVersion: LegacyTestApiVersion);

    /// <summary>The ids of the test cases in a suite, in the order the server keeps them.</summary>
    public async Task<List<int>> GetSuiteTestCaseIdsAsync(string project, int planId, int suiteId) =>
        (await SendAsync<ListResponse<SuiteTestCase>>(HttpMethod.Get, Url(project, $"test/plans/{planId}/suites/{suiteId}/testcases", apiVersion: LegacyTestApiVersion)))
            .Value.Select(c => c.TestCase.Id).ToList();

    /// <summary>Builds of the project, most recently queued first; all of them unless <paramref name="limit"/> is set.</summary>
    public async Task<List<Build>> GetBuildsAsync(string project, int? limit = null)
    {
        // Builds are paged with a continuation token, not $skip.
        const int pageSize = 1000;
        var all = new List<Build>();
        string? token = null;
        do
        {
            var query = $"queryOrder=queueTimeDescending&$top={Math.Min(pageSize, limit - all.Count ?? pageSize)}";
            if (token is not null)
                query += $"&continuationToken={Uri.EscapeDataString(token)}";
            (var page, token) = await SendPageAsync<ListResponse<Build>>(Url(project, "build/builds", query));
            all.AddRange(page.Value);
        } while (token is not null && (limit is null || all.Count < limit));
        return all;
    }

    public Task<Build> GetBuildAsync(string project, int id) =>
        SendAsync<Build>(HttpMethod.Get, Url(project, $"build/builds/{id}"));

    /// <summary>Queues a build of a definition; without a branch the definition's default branch is built.</summary>
    public Task<Build> QueueBuildAsync(string project, int definitionId, string? sourceBranch) =>
        SendAsync<Build>(HttpMethod.Post, Url(project, "build/builds"), new { definition = new { id = definitionId }, sourceBranch });

    /// <summary>Pages through a list with $top/$skip until the server returns a short page or <paramref name="limit"/> items are read.</summary>
    async Task<List<T>> PageAsync<T>(string project, string path, string query, int? limit, int pageSize = 100, string? apiVersion = null)
    {
        var all = new List<T>();
        while (limit is null || all.Count < limit)
        {
            var top = Math.Min(pageSize, limit - all.Count ?? pageSize);
            var paging = $"$top={top}&$skip={all.Count}";
            var page = (await SendAsync<ListResponse<T>>(HttpMethod.Get, Url(project, path, query.Length > 0 ? $"{query}&{paging}" : paging, apiVersion))).Value;
            all.AddRange(page);
            if (page.Count < top)
                break;
        }
        return all;
    }

    /// <summary>A GET of one page and the x-ms-continuationtoken header that points at the next one, null on the last page.</summary>
    async Task<(T Page, string? Next)> SendPageAsync<T>(string url)
    {
        string? next = null;
        var page = await SendAsync<T>(HttpMethod.Get, url, onResponse: r =>
            next = r.Headers.TryGetValues("x-ms-continuationtoken", out var v) ? v.FirstOrDefault() is { Length: > 0 } t ? t : null : null);
        return (page, next);
    }

    /// <summary>Sends one request, with <paramref name="body"/> serialized as JSON under <paramref name="contentType"/> (JSON Patch for work items), and deserializes the JSON answer; HTTP errors and rejected credentials become an <see cref="AdoException"/> with a matching exit code. <paramref name="onResponse"/> sees the successful response first, e.g. to read paging headers.</summary>
    public async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null, string contentType = "application/json", Action<HttpResponseMessage>? onResponse = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json.Options), Encoding.UTF8, contentType);

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(request);
        if (_debug)
            Console.Error.WriteLine($"{method} {url} -> {(int)response.StatusCode} ({sw.ElapsedMilliseconds} ms)");

        if (!response.IsSuccessStatusCode)
            throw await ErrorAsync(response);
        // Azure DevOps answers a rejected credential with a 203 HTML sign-in page.
        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
            throw AuthFailed();
        onResponse?.Invoke(response);
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    static AdoException AuthFailed() => new("authentication failed.\n\nRun:\n  ado auth login", AdoException.Auth);

    async Task<AdoException> ErrorAsync(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync();
        var detail = Detail(body);
        return code switch
        {
            400 when body.Contains("VssVersionOutOfRangeException") =>
                new($"the server does not support REST API version {_apiVersion}{detail}\n\nSet ADO_API_VERSION (or \"apiVersion\" in ~/.ado/config.json) to a version it supports, e.g. {DefaultApiVersion}."),
            401 => AuthFailed(),
            403 => new($"permission denied{detail}", AdoException.Permission),
            404 => new($"not found{detail}", AdoException.NotFound),
            407 => new(Proxy.AuthRequired),
            // 412: a JSON Patch "test" operation failed, e.g. the work item is no longer at the expected revision.
            409 or 412 => new($"conflict{detail}", AdoException.Conflict),
            429 => new("rate limited by Azure DevOps Server; try again later"),
            >= 500 => new($"Azure DevOps Server error ({code}){detail}"),
            _ => new($"request failed ({code}){detail}"),
        };
    }

    /// <summary>Azure DevOps error bodies look like {"message": "..."}.</summary>
    static string Detail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var m) ? ": " + m.GetString() : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
