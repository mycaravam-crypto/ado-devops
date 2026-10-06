namespace AdoCli.Git;

using System.ComponentModel;
using System.Diagnostics;
using AdoCli.Api;

public sealed record GitResult(int ExitCode, string Stdout, string Stderr);

/// <summary>Server, project and repository as encoded in an Azure DevOps git remote URL.</summary>
public sealed record Remote(string? Server, string Project, string Repo);

/// <summary>Runs the locally installed git.</summary>
public static class GitClient
{
    /// <summary>Runs git; with <paramref name="capture"/> false, git writes straight to the terminal.</summary>
    public static GitResult Run(IEnumerable<string> args, bool capture = true)
    {
        var psi = new ProcessStartInfo("git", args) { RedirectStandardOutput = capture, RedirectStandardError = capture };
        try
        {
            using var p = Process.Start(psi)!;
            var stderr = capture ? p.StandardError.ReadToEndAsync() : Task.FromResult("");
            var stdout = capture ? p.StandardOutput.ReadToEnd() : "";
            p.WaitForExit();
            return new GitResult(p.ExitCode, stdout, stderr.Result);
        }
        catch (Win32Exception)
        {
            throw new AdoException("git is not installed or not on PATH");
        }
    }

    /// <summary>Runs git and returns trimmed stdout, or throws with git's own error message.</summary>
    public static string Check(params string[] args)
    {
        var r = Run(args);
        return r.ExitCode == 0 ? r.Stdout.Trim() : throw new AdoException(r.Stderr.Trim() is { Length: > 0 } e ? e : $"git {args[0]} failed");
    }

    /// <summary>The origin remote of the current directory's repository, if it is an Azure DevOps repo.</summary>
    public static Remote? DetectRemote(string? configuredServer)
    {
        var r = Run(["remote", "get-url", "origin"]);
        return r.ExitCode == 0 ? ParseRemote(r.Stdout.Trim(), configuredServer) : null;
    }

    /// <summary>
    /// Parses {collection}/{project}/_git/{repo}. A remote of the form {collection}/_git/{repo} is
    /// recognised when {collection} matches the configured server; the project then has the repo's name.
    /// </summary>
    public static Remote? ParseRemote(string url, string? configuredServer)
    {
        var i = url.IndexOf("/_git/", StringComparison.Ordinal);
        if (i < 0 || !Uri.TryCreate(url[..i], UriKind.Absolute, out var prefix))
            return null;

        var repo = Uri.UnescapeDataString(url[(i + 6)..].TrimEnd('/'));
        var collection = Normalize(prefix);
        if (configuredServer is not null && Uri.TryCreate(configuredServer, UriKind.Absolute, out var server) && SameCollection(prefix, server))
            return new Remote(collection, repo, repo);

        var cut = collection.LastIndexOf('/');
        var project = Uri.UnescapeDataString(collection[(cut + 1)..]);
        var isHttp = prefix.Scheme is "http" or "https";
        return new Remote(isHttp ? collection[..cut] : null, project, repo);
    }

    static string Normalize(Uri u) => $"{u.Scheme}://{u.Authority}{u.AbsolutePath}".TrimEnd('/');

    static bool SameCollection(Uri a, Uri b) =>
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.AbsolutePath.TrimEnd('/'), b.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
