# ado

A small command-line client for **Azure DevOps Server** (on-prem), in the spirit of `gh`.
It covers the everyday workflow — repositories, pull requests, work items, builds — not the whole API.

## Installation

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build and `git` on your `PATH`.

```bash
dotnet publish src/AdoCli -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o ~/.local/bin
```

Use `-r win-x64` or `-r osx-arm64` on other platforms. This puts a single `ado` executable in `~/.local/bin`.

## Authentication

Create a personal access token (PAT) in Azure DevOps Server (scopes: Code read & write, Work items read, Build read & execute), then:

```bash
ado auth login https://tfs.company.local/tfs/DefaultCollection   # prompts for the PAT
ado auth status
ado auth logout
```

In CI, pipe the token in: `echo "$PAT" | ado auth login <server-url>`, or skip login and set environment variables (below).

## Repositories

```bash
ado repo list                 # repositories of the current project (all, if unknown)
ado repo show [<repo>]
ado repo clone <repo> [dir]
ado repo status               # server/project/repo of the current clone (no network call)
```

## Pull requests

```bash
ado pr list [--mine] [--status active|completed|abandoned|all]
ado pr show 142
ado pr checkout 142           # local branch pr/142; refuses to run on a dirty tree
ado pr diff 142               # git diff target...source
ado pr approve 142
ado pr merge 142 [--squash] [--yes]
ado pr create                 # prompts; or --title t [--description d] [--source b] [--target b]
```

## Work items

```bash
ado workitem list             # open items assigned to you
ado workitem show 4711
```

## Builds

```bash
ado build list
ado build show 815
ado build run <definition-id> [--branch b]
```

## JSON

`--json` makes data commands print JSON on stdout. Messages, prompts and errors go to stderr, so stdout is always
either JSON or empty (`auth` commands print plain text only). `ado pr context <id>` always prints JSON.

```bash
ado pr list --json | jq '.[].pullRequestId'
```

`--debug` logs each HTTP request to stderr (method, URL, status, timing; never credentials).

## Exit codes

| Code | Meaning |
|---|---|
| 0 | success |
| 1 | general failure (including git failures and an aborted merge) |
| 2 | invalid command or arguments |
| 3 | authentication failure / not logged in |
| 4 | permission denied |
| 5 | not found |
| 6 | conflict |

`--json` does not change exit codes.

## Using `ado` as a local automation/AI tool interface

`ado` contains no AI. It is deterministic: the same server state gives the same output, and JSON lists are sorted
where the server does not fix the order. That makes it a safe tool for scripts and local agents, which can
work from JSON and exit codes alone:

```bash
ado repo status --json        # where am I?
ado pr context 142 --json     # PR, reviewers, commits, changed files, linked work items
ado pr diff 142 --json        # changed files; without --json, the full unified diff
ado workitem show 4711 --json
ado build show 815 --json
```

Commands that change state (`pr create`, `pr approve`, `pr merge`, `build run`) never prompt when given
`--title` / `--yes`. Credentials never appear in any output.

## Configuration

`ado auth login` writes `~/.ado/config.json` (readable only by you):

```json
{ "server": "https://tfs.company.local/tfs/DefaultCollection", "pat": "...", "project": "Platform" }
```

`project` and `apiVersion` are optional and set by hand. Environment variables override the file:
`ADO_SERVER`, `ADO_PAT`, `ADO_PROJECT`, `ADO_API_VERSION`.

### Supported servers

| Server | Highest REST API version |
|---|---|
| Azure DevOps Server 2019 | 5.0 (5.1 on Update 1) |
| Azure DevOps Server 2020 | 6.0 |
| Azure DevOps Server 2022 | 7.0 |

`ado` sends `api-version=5.0` by default, which all of them accept and every command works with.
To use a newer one, set `ADO_API_VERSION=6.0` (or `"apiVersion": "6.0"` in the config file). If the server does not
support the version, `ado` says so and exits with 1. TFS 2018 and older are not supported.

Inside a clone of an Azure DevOps repository, project and repository are taken from the `origin` remote,
so most commands need no arguments. Elsewhere, pass `--project <name>` and `--repo <name>`.

## Development

```bash
dotnet build
dotnet test
dotnet run --project src/AdoCli -- --help
```

The code is deliberately flat: commands in `src/AdoCli/Cli` call `Api/AdoClient` (one `HttpClient`) and `Git/GitClient`
(runs `git`) directly. The REST API version (default `5.0`) is added to every URL in one place, `AdoClient.Url`.
Tests use a stub HTTP handler; no real server is needed. See [PLAN.md](PLAN.md) for scope and non-goals.
