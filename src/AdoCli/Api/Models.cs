namespace AdoCli.Api;

public sealed record ConnectionData(Identity AuthenticatedUser);

public sealed record Identity(Guid Id, string ProviderDisplayName);
