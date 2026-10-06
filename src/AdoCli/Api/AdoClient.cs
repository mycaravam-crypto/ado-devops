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
    /// <summary>Supported by Azure DevOps Server 2019 and later.</summary>
    public const string ApiVersion = "5.0";

    readonly HttpClient _http;
    readonly string _server;
    readonly bool _debug;

    public AdoClient(string server, string pat, bool debug = false, HttpMessageHandler? handler = null)
    {
        _server = server.TrimEnd('/');
        _debug = debug;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ado-cli", Program.Version));
    }

    /// <summary>{server}[/{project}]/_apis/{path}?{query}&amp;api-version=...</summary>
    public string Url(string? project, string path, string query = "")
    {
        var scope = project is null ? "" : "/" + Uri.EscapeDataString(project);
        var q = query.Length > 0 ? query + "&" : "";
        return $"{_server}{scope}/_apis/{path}?{q}api-version={ApiVersion}";
    }

    // connectionData only exists as a preview API, so it is called without api-version.
    public Task<ConnectionData> GetConnectionDataAsync() =>
        SendAsync<ConnectionData>(HttpMethod.Get, $"{_server}/_apis/connectionData");

    public async Task<List<Repository>> GetRepositoriesAsync(string? project) =>
        (await SendAsync<ListResponse<Repository>>(HttpMethod.Get, Url(project, "git/repositories"))).Value;

    public Task<Repository> GetRepositoryAsync(string project, string repo) =>
        SendAsync<Repository>(HttpMethod.Get, Url(project, $"git/repositories/{Uri.EscapeDataString(repo)}"));

    /// <summary>Pull requests of a repository, or of a project/collection when <paramref name="repo"/> is null.</summary>
    public async Task<List<PullRequest>> GetPullRequestsAsync(string? project, string? repo, string status, Guid? creatorId)
    {
        var path = repo is null ? "git/pullrequests" : $"git/repositories/{Uri.EscapeDataString(repo)}/pullrequests";
        var query = $"searchCriteria.status={Uri.EscapeDataString(status)}&$top=50";
        if (creatorId is { } id)
            query += $"&searchCriteria.creatorId={id}";
        return (await SendAsync<ListResponse<PullRequest>>(HttpMethod.Get, Url(project, path, query))).Value;
    }

    public Task<PullRequest> GetPullRequestAsync(int id) =>
        SendAsync<PullRequest>(HttpMethod.Get, Url(null, $"git/pullrequests/{id}"));

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

    string PullRequestUrl(PullRequest pr, string suffix = "") =>
        Url(pr.Repository.Project.Name, $"git/repositories/{pr.Repository.Id}/pullrequests/{pr.PullRequestId}{suffix}");

    public Task<WorkItem> GetWorkItemAsync(int id) =>
        SendAsync<WorkItem>(HttpMethod.Get, Url(null, $"wit/workitems/{id}"));

    /// <summary>Open work items assigned to the current user, most recently changed first.</summary>
    public async Task<List<WorkItem>> GetMyWorkItemsAsync(string? project)
    {
        var inProject = project is null ? "" : " AND [System.TeamProject] = @project";
        var wiql = "SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = @Me AND [System.State] NOT IN ('Closed', 'Done', 'Removed')"
            + inProject + " ORDER BY [System.ChangedDate] DESC";
        var result = await SendAsync<WiqlResult>(HttpMethod.Post, Url(project, "wit/wiql", "$top=50"), new { query = wiql });
        if (result.WorkItems.Count == 0)
            return [];

        var ids = string.Join(',', result.WorkItems.Select(w => w.Id));
        var query = $"ids={ids}&fields=System.Title,System.WorkItemType,System.State";
        return (await SendAsync<ListResponse<WorkItem>>(HttpMethod.Get, Url(null, "wit/workitems", query))).Value;
    }

    public async Task<List<Build>> GetBuildsAsync(string project) =>
        (await SendAsync<ListResponse<Build>>(HttpMethod.Get, Url(project, "build/builds", "queryOrder=queueTimeDescending&$top=20"))).Value;

    public Task<Build> GetBuildAsync(string project, int id) =>
        SendAsync<Build>(HttpMethod.Get, Url(project, $"build/builds/{id}"));

    /// <summary>Queues a build of a definition; without a branch the definition's default branch is built.</summary>
    public Task<Build> QueueBuildAsync(string project, int definitionId, string? sourceBranch) =>
        SendAsync<Build>(HttpMethod.Post, Url(project, "build/builds"), new { definition = new { id = definitionId }, sourceBranch });

    public async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json.Options), Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(request);
        if (_debug)
            Console.Error.WriteLine($"{method} {url} -> {(int)response.StatusCode} ({sw.ElapsedMilliseconds} ms)");

        if (!response.IsSuccessStatusCode)
            throw await ErrorAsync(response);
        // Azure DevOps answers a rejected credential with a 203 HTML sign-in page.
        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
            throw AuthFailed();
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    static AdoException AuthFailed() => new("authentication failed.\n\nRun:\n  ado auth login", AdoException.Auth);

    static async Task<AdoException> ErrorAsync(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        var detail = await DetailAsync(response);
        return code switch
        {
            401 => AuthFailed(),
            403 => new($"permission denied{detail}", AdoException.Permission),
            404 => new($"not found{detail}", AdoException.NotFound),
            409 => new($"conflict{detail}", AdoException.Conflict),
            429 => new("rate limited by Azure DevOps Server; try again later"),
            >= 500 => new($"Azure DevOps Server error ({code}){detail}"),
            _ => new($"request failed ({code}){detail}"),
        };
    }

    /// <summary>Azure DevOps error bodies look like {"message": "..."}.</summary>
    static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("message", out var m) ? ": " + m.GetString() : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
