# ado

A small command-line client for **Azure DevOps Server** (on-prem), in the spirit of `gh`. It covers the everyday
workflow (repositories, pull requests, work items, test plans, builds), not the whole API. Output is deterministic,
with `--json` and documented exit codes, so scripts and local agents can use it safely.

## Install

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build and `git` on your `PATH` to run.
`dotnet publish` creates a single `ado` executable; pick your runtime (`linux-x64`, `linux-arm64`, `osx-arm64`,
`osx-x64`, `win-x64`).

```bash
# Linux, macOS, WSL, Git Bash: publish into a folder on your PATH
dotnet publish src/AdoCli -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o ~/.local/bin
ado --version
```

```powershell
# Windows: publish into a folder of your own, then add it to the user PATH once
$bin = "$env:LOCALAPPDATA\Programs\ado"
dotnet publish src/AdoCli -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $bin
[Environment]::SetEnvironmentVariable('Path', [Environment]::GetEnvironmentVariable('Path', 'User') + ";$bin", 'User')
```

`--self-contained true` gives a larger binary that runs without .NET installed. To update, pull and publish again.
Step by step for bash and PowerShell: [docs/install.md](docs/install.md).

Then log in with a personal access token (scopes: Code read & write, Work items read & write, Build read & execute,
Test management read):

```bash
ado auth login https://tfs.company.local/tfs/DefaultCollection   # prompts for the PAT; in CI: echo "$PAT" | ado auth login <url>
```

The token is stored in `~/.ado/config.json`, readable only by you. Don't put `ADO_PAT` in your shell profile.

## Commands

Inside a clone, server, project and repository come from the `origin` remote, so most commands need no arguments;
elsewhere pass `--project` and `--repo`. `ado <command> --help` lists every option with examples.

| Command | What it does |
|---|---|
| `auth login\|status\|logout` | Save and check the token; `status` shows server, user, TLS mode and proxy |
| `config list\|get\|set\|unset` | Show or change settings and where each value comes from |
| `repo list\|show\|clone\|status` | Repositories; `status` shows server/project/repo of the current clone |
| `pr list\|show\|diff\|checkout` | Pull requests; `checkout` creates or fast-forwards `pr/<id>` |
| `pr create\|approve\|merge` | Create, approve, complete (`--squash`, `--yes` skips the prompt) |
| `pr context <id>` | PR, commits, changed files and linked work items as one JSON document |
| `commit context <commit>` | A commit and the work items the server links to it, as JSON (`ado.commit-context/v1`) |
| `workitem list` | Your open items; filters `--all`, `--type`, `--state`, `--tag`, `--area`, `--contains`, `--wiql`, `--ids` |
| `workitem show\|create\|edit` | One item; `edit` takes several ids or `-` (stdin) for bulk changes, `--dry-run` previews |
| `workitem context <id>` | Description, acceptance criteria and parent/child/related links as JSON (`--revision n`) |
| `workitem search <query>` | Typo-tolerant title search, ranked |
| `testplan list\|show` | Test plans, suite hierarchy, steps (`--with details`) and run outcomes (`--with results`) |
| `testplan export\|import` | Round-trip a plan as editable JSON (`--dry-run` previews, conflicts abort); `--format html` for a report |
| `testplan coverage\|progress` | Requirement coverage by Area Path; test-point progress and trends (text, JSON, HTML) |
| `testplan trace-export` | Read-only test traceability snapshot for docwizz (`ado.test-traceability/v2`) |
| `build list\|show\|run` | Builds; `run <definition-id> [--branch b]` queues one |

```bash
ado pr list --mine
ado workitem list --all --type Bug --tag xyz --ids | ado workitem edit - --state Closed --yes
ado testplan export 12 --output plan.json && ado testplan import plan.json --dry-run
ado pr context 142 | jq '.workItems'
```

Details and examples: [pull requests and commits](docs/pull-requests.md), [work items](docs/work-items.md) (filters,
bulk edits, requirement context, search), [test plans](docs/test-plans.md) (the JSON file, import rules, coverage,
progress).

Global flags: `--json` (data on stdout, messages on stderr), `--debug` (HTTP log, never credentials), `--insecure`,
`--ca-cert`, `--proxy`. Test plan commands always use REST API 5.0, which Azure DevOps Server 2020 needs for them.

## Configuration

Each setting comes from the first of: flag, environment variable, `~/.ado/config.json`, default.

| Key | Environment | Flag | Default |
|---|---|---|---|
| `server` | `ADO_SERVER` | | the `origin` remote's server |
| `pat` | `ADO_PAT` | | set by `ado auth login` |
| `project` | `ADO_PROJECT` | `--project` | the `origin` remote's project |
| `apiVersion` | `ADO_API_VERSION` | | `5.0` (works on Server 2019, 2020 and 2022) |
| `insecure` | `ADO_INSECURE` | `--insecure` | `false` |
| `caCert` | `ADO_CA_CERT` | `--ca-cert` | none |
| `proxy` | `ADO_PROXY` | `--proxy` | the system proxy (`none` connects directly) |

For an internal CA, prefer `ado config set caCert company-ca.pem` over `insecure`, which skips certificate checks.
TLS and proxy settings are passed on to git for `repo clone`, `pr checkout` and `pr diff`. TFS 2018 and older are not
supported. More on settings, TLS, proxy, shell defaults and scripting: [docs/configuration.md](docs/configuration.md).

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

## Development

```bash
dotnet build
dotnet test
dotnet run --project src/AdoCli -- --help
```

No dependencies beyond .NET (tests use xUnit and a stub HTTP handler, no server needed). Commands live in
`src/AdoCli/Cli/`, the REST client in `src/AdoCli/Api/`, git in `src/AdoCli/Git/`; [docwizz.yaml](docwizz.yaml)
enforces that layering on every PR. Code layout and versioning: [docs/development.md](docs/development.md). Every merge to `main` is released as the next SemVer tag; a PR label picks the
bump: `release:patch` (default), `release:minor`, `release:major` or `release:none`. Scope and non-goals:
[PLAN.md](PLAN.md).
