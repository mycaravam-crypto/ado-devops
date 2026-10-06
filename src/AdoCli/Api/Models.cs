namespace AdoCli.Api;

public sealed record ConnectionData(Identity AuthenticatedUser);

public sealed record Identity(Guid Id, string ProviderDisplayName);

public sealed record ListResponse<T>(List<T> Value);

public sealed record ProjectRef(string Name);

public sealed record Repository(Guid Id, string Name, string? DefaultBranch, string RemoteUrl, string WebUrl, ProjectRef Project);
