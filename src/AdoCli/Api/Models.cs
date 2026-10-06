namespace AdoCli.Api;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record ConnectionData(Identity AuthenticatedUser);

public sealed record Identity(Guid Id, string ProviderDisplayName);

public sealed record ListResponse<T>(List<T> Value);

public sealed record ProjectRef(string Name);

public sealed record Repository(Guid Id, string Name, string? DefaultBranch, string? RemoteUrl, string? WebUrl, ProjectRef Project);

public sealed record IdentityRef(string DisplayName);

/// <summary>Vote: 10 approved, 5 approved with suggestions, 0 none, -5 waiting for author, -10 rejected.</summary>
public sealed record Reviewer(string DisplayName, int Vote);

public sealed record CommitRef(string CommitId);

public sealed record GitUser(string Name, DateTime Date);

public sealed record Commit(string CommitId, string? Comment, GitUser? Author);

public sealed record GitItem(string Path, bool IsFolder);

public sealed record GitChange(GitItem Item, string ChangeType, string? OriginalPath);

public sealed record CommitDiffs(List<GitChange> Changes);

/// <summary>A changed file; <see cref="OriginalPath"/> is set for renames.</summary>
public sealed record Change(string Path, string ChangeType, string? OriginalPath);

public sealed record ResourceRef(string Id);

public sealed record PullRequest(
    int PullRequestId,
    string Title,
    string? Description,
    string Status,
    IdentityRef CreatedBy,
    Repository Repository,
    string SourceRefName,
    string TargetRefName,
    CommitRef? LastMergeSourceCommit,
    CommitRef? LastMergeTargetCommit,
    List<Reviewer>? Reviewers);

/// <summary>Work item fields are kept raw: their set and shape depend on the process template.</summary>
public sealed record WorkItem(int Id, Dictionary<string, JsonElement> Fields)
{
    /// <summary>A field as text; identity fields (objects since Server 2019) yield their display name.</summary>
    public string Field(string name) => Fields.GetValueOrDefault(name) switch
    {
        { ValueKind: JsonValueKind.Object } o when o.TryGetProperty("displayName", out var n) => n.GetString() ?? "",
        { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => "",
        var v => v.ToString(),
    };
}

public sealed record WorkItemRef(int Id);

public sealed record WiqlResult(List<WorkItemRef> WorkItems);

public sealed record DefinitionRef(int Id, string Name);

public sealed record Link(string Href);

public sealed record BuildLinks(Link? Web);

public sealed record Build(
    int Id,
    string BuildNumber,
    string Status,
    string? Result,
    DefinitionRef Definition,
    string? SourceBranch,
    IdentityRef? RequestedFor,
    [property: JsonPropertyName("_links")] BuildLinks? Links);
