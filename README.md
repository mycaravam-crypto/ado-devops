# ado

A small command-line client for **Azure DevOps Server** (on-prem), in the spirit of `gh`.
It covers the everyday workflow — repositories, pull requests, work items, builds — not the whole API.

## Installation

Building needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). Running needs `git` on your `PATH`.

### 1. Compile

`dotnet publish` creates a single `ado` executable (`ado.exe` on Windows). Choose the runtime identifier for your platform:

| Platform | `-r` |
|---|---|
| Linux x64 | `linux-x64` |
| Linux ARM64 | `linux-arm64` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Windows x64 | `win-x64` |

```bash
# Needs the .NET 10 runtime on the machine (small binary)
dotnet publish src/AdoCli -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o out

# Runs without .NET installed (larger binary, can be copied to other machines with the same platform)
dotnet publish src/AdoCli -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o out
```

The executable ends up in `out/`. To update later, pull and run the same command again.

### 2. Use it from bash (Linux, macOS, WSL, Git Bash)

Publish straight into a directory on your `PATH`, for example `~/.local/bin`:

```bash
dotnet publish src/AdoCli -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o ~/.local/bin
```

If `~/.local/bin` is not on your `PATH` yet, add it to `~/.bashrc` (on macOS with zsh: `~/.zshrc`) and reload:

```bash
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc
ado --version
```

Optionally set defaults in `~/.bashrc` so you don't need to pass them:

```bash
export ADO_SERVER="https://tfs.company.local/tfs/DefaultCollection"
export ADO_PROJECT="Platform"
```

In scripts, use the exit code and the JSON output:

```bash
if ado pr show 142 --json > pr.json; then
  jq -r '.title' pr.json
else
  echo "ado failed with exit code $?" >&2
fi
```

### 3. Use it from PowerShell (Windows)

Publish into a folder of your own and add that folder to your user `PATH` once:

```powershell
$bin = "$env:LOCALAPPDATA\Programs\ado"
dotnet publish src/AdoCli -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $bin

# Add the folder to the user PATH permanently (only needed once)
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $bin) {
    [Environment]::SetEnvironmentVariable('Path', "$userPath;$bin", 'User')
}
$env:Path += ";$bin"   # current session; new terminals pick it up automatically

ado --version
```

Optionally set defaults in your PowerShell profile (`notepad $PROFILE`; create it first with
`New-Item -Force $PROFILE` if it doesn't exist):

```powershell
$env:ADO_SERVER  = 'https://tfs.company.local/tfs/DefaultCollection'
$env:ADO_PROJECT = 'Platform'
```

In scripts, check `$LASTEXITCODE` and parse the JSON with `ConvertFrom-Json`:

```powershell
$pr = ado pr show 142 --json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "ado failed with exit code $LASTEXITCODE" }
$pr.title
```

**PowerShell 7 on Linux or macOS:** install the binary as in the bash section. Then add the folder to `$env:PATH`
in `$PROFILE`: `$env:PATH = "$HOME/.local/bin:$env:PATH"`.

Don't put `ADO_PAT` in `~/.bashrc` or `$PROFILE`. Run `ado auth login` once instead (see below). It stores the token in
`~/.ado/config.json`. On Linux and macOS only you can read that file. On Windows it is `%USERPROFILE%\.ado\config.json`, protected by your user profile's permissions.

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
ado pr context <id>          # PR, commits, changed files, work items as JSON
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
