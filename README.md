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

## Common commands

```text
ado repo list | show [<repo>] | clone <repo> [dir] | status
ado pr list [--mine] [--status active|completed|abandoned|all]
ado pr show | diff | checkout | approve <id>
ado pr create [--title t --description d --source branch --target branch]
ado pr merge <id> [--squash] [--yes]
ado workitem list | show <id>
ado build list | show <id> | run <definition-id> [--branch b]
```

Every list/show command accepts `--json`. `--debug` logs each HTTP request (method, URL, status, timing — never credentials).
Run `ado --help` for the full list.

Exit codes: `0` success, `1` failure, `2` invalid usage, `3` authentication, `4` permission, `5` not found, `6` conflict.

## Configuration

`ado auth login` writes `~/.ado/config.json` (readable only by you):

```json
{ "server": "https://tfs.company.local/tfs/DefaultCollection", "pat": "...", "project": "Platform" }
```

`project` is optional and set by hand. Environment variables override the file:
`ADO_SERVER`, `ADO_PAT`, `ADO_PROJECT`.

Inside a clone of an Azure DevOps repository, project and repository are taken from the `origin` remote,
so most commands need no arguments. Elsewhere, pass `--project <name>` and `--repo <name>`.

## Examples

```bash
ado auth login https://tfs.company.local/tfs/DefaultCollection
ado repo list
ado repo clone connector && cd connector

ado pr list
ado pr show 142
ado pr checkout 142        # local branch pr/142
ado pr diff 142
ado pr approve 142
ado pr merge 142

ado pr create --title "Fix import validation" --target main
ado pr list --json | jq '.[].pullRequestId'
```

## Development

```bash
dotnet build
dotnet test
dotnet run --project src/AdoCli -- --help
```

The code is deliberately flat: commands in `src/AdoCli/Cli` call `Api/AdoClient` (one `HttpClient`) and `Git/GitClient`
(runs `git`) directly. REST API version `5.0` (Azure DevOps Server 2019+) is set in one place in `AdoClient`.
Tests use a stub HTTP handler; no real server is needed. See [PLAN.md](PLAN.md) for scope and non-goals.
