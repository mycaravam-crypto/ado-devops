namespace AdoCli.Api;

/// <summary>An error with a user-facing message and a process exit code.</summary>
public sealed class AdoException(string message, int exitCode = AdoException.General) : Exception(message)
{
    public const int General = 1;
    public const int InvalidUsage = 2;
    public const int Auth = 3;
    public const int Permission = 4;
    public const int NotFound = 5;
    public const int Conflict = 6;

    public int ExitCode { get; } = exitCode;

    public static AdoException Usage(string message) => new(message, InvalidUsage);
}
