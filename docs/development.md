# Development

```bash
dotnet build
dotnet test
dotnet run --project src/AdoCli -- --help
```

The code is deliberately flat and has no dependencies beyond .NET (tests use xUnit):

| Path | Contents |
|---|---|
| `src/AdoCli/Program.cs` | help text, command dispatch, error → exit code mapping |
| `src/AdoCli/Args.cs`, `Config.cs` | argument parser; `~/.ado/config.json` plus `ADO_*` overrides |
| `src/AdoCli/Cli/` | one file per command group; `Context` resolves settings, git remote and the client |
| `src/AdoCli/Api/AdoClient.cs` | the one REST client (one `HttpClient`); every URL is built in `AdoClient.Url`, which adds the API version |
| `src/AdoCli/Api/Tls.cs`, `Proxy.cs` | CA file / insecure mode and proxy, for both `HttpClient` and git |
| `src/AdoCli/Git/GitClient.cs` | runs `git`; detects server, project and repository from `origin` |
| `tests/AdoCli.Tests/` | unit tests with a stub HTTP handler; no real server is needed |

Commands call `AdoClient` and `GitClient` directly. Every pull request runs two GitHub workflows:
`test` (`dotnet build` and `dotnet test`, also on `main`) and `docwizz`, which checks documentation coverage and the
layering in [docwizz.yaml](../docwizz.yaml) (`Cli` may use `Api` and `Git`, never the other way round) for problems the
change introduces. See [PLAN.md](../PLAN.md) for scope and non-goals.

## Versions and releases

Versions are bumped automatically. Every merge to `main` becomes a release: the `release` workflow builds and tests
the commit, tags it `vX.Y.Z` and publishes a GitHub release with notes generated from the merged pull requests.
A label on the pull request chooses the bump (at most one):

| Label | Bump | Example |
|---|---|---|
| (none) or `release:patch` | patch | 0.4.2 → 0.4.3 |
| `release:minor` | minor | 0.4.2 → 0.5.0 |
| `release:major` | major | 0.4.2 → 1.0.0 |
| `release:none` | no new version | |

The first release is `v0.1.0`. There is no version number to edit by hand: the git tag is the only place it lives.

Builds read the version from the latest tag, so `ado --version` always says what you are running:

| Build | `ado --version` |
|---|---|
| release, or a build of the tagged commit | `ado 0.4.2` |
| 3 commits after `v0.4.2` | `ado 0.4.2-dev.3` |
| without git or tags (e.g. a source archive) | `ado 0.0.0-dev` |

To build a specific version yourself, pass it: `dotnet publish src/AdoCli -c Release -p:Version=1.2.3 ...`.
