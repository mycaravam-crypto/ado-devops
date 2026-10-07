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
ado auth status               # server, user, TLS mode and proxy in effect
ado auth logout               # deletes ~/.ado/config.json
```

`auth login` checks the token against the server before saving it, and keeps the project, API version, TLS and proxy
settings already in the config file. A server URL with embedded credentials is rejected; a plain `http://` URL works
but prints a warning, since the token then travels unencrypted.

In CI, pipe the token in: `echo "$PAT" | ado auth login <server-url>`, or skip login and set environment variables (below).

## Repositories

```bash
ado repo list                 # repositories of the current project (all, if unknown)
ado repo show [<repo>]
ado repo clone <repo> [dir]
ado repo status               # server/project/repo of the current clone (no network call)
```

`repo status` warns when the clone's server is not the one you are logged in to. `repo clone` passes the TLS and proxy
settings on to git (see [Configuration](#configuration)).

## Pull requests

```bash
ado pr list [--mine] [--status active|completed|abandoned|all]
ado pr show 142               # details, changed file count, description, reviewer votes
ado pr context 142            # PR, commits, changed files and linked work items as one JSON document
ado pr checkout 142           # local branch pr/142; refuses to run on a dirty tree
ado pr diff 142               # git diff target...source
ado pr approve 142
ado pr merge 142 [--squash] [--yes]
ado pr create                 # prompts; or --title t [--description d] [--source b] [--target b]
```

- `pr list` shows up to 50 pull requests of the current repository, or of the whole project outside a clone. Without
  `--status` it lists active ones.
- `pr checkout` and `pr diff` run git and must be run inside a clone of the PR's repository. If `pr/<id>` already
  exists, `pr checkout` only fast-forwards it, so local commits on it are never lost. `pr diff --json` lists the
  changed files from the server and works anywhere.
- `pr create` uses the current branch as source and the repository's default branch as target unless told otherwise.
  When stdin is not a terminal, `--title` is required.
- `pr merge` shows the PR and asks for confirmation (`--yes` / `-y` skips it). If branch policies still have to pass,
  the server only queues the completion, and `ado` says so.

## Work items

```bash
ado workitem list             # open items assigned to you (current project, if known), up to 50
ado workitem show 4711
```

Read-only: `ado` does not create or edit work items.

## Builds

```bash
ado build list                # the 20 most recently queued builds of the project
ado build show 815
ado build run <definition-id> [--branch b]   # default: the definition's default branch
```

## JSON

`--json` makes data commands print JSON on stdout. Messages, prompts and errors go to stderr, so stdout is always
either JSON or empty (`auth` commands print plain text only). `ado pr context <id>` always prints JSON.

```bash
ado pr list --json | jq '.[].pullRequestId'
```

`--debug` logs each HTTP request to stderr (method, URL, status, timing; never credentials) and shows stack traces
on errors.

Status values are colored only when stdout is a terminal and `NO_COLOR` is not set.

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

Each setting is taken from the first of these that has it: a command-line flag, an environment variable, the config file,
then the default.

| Key | Environment | Flag | Default |
|---|---|---|---|
| `server` | `ADO_SERVER` | | the `origin` remote's server |
| `pat` | `ADO_PAT` | | (set by `ado auth login`) |
| `project` | `ADO_PROJECT` | `--project` | the `origin` remote's project |
| `apiVersion` | `ADO_API_VERSION` | | `5.0` |
| `insecure` | `ADO_INSECURE` | `--insecure` | `false` |
| `caCert` | `ADO_CA_CERT` | `--ca-cert` | none |
| `proxy` | `ADO_PROXY` | `--proxy` | the system proxy |

Inside a clone, the `origin` remote's project wins over `ADO_PROJECT` and the file.

### Viewing and changing settings

```bash
ado config list               # every setting, its value and where it comes from (token masked)
ado config get insecure       # one value; exits with 1 if not set
ado config set caCert ~/company-ca.pem
ado config unset insecure     # back to the default
```

```
KEY         VALUE                                    SOURCE
server      https://tfs.company.local/tfs/Default    config file
pat         ********                                 config file
project     Platform                                 git remote
apiVersion  5.0                                      default
insecure    false                                    ADO_INSECURE
caCert      /home/me/company-ca.pem                  config file
proxy       http://svc-ado:****@proxy.company:8080   config file
```

`config set` checks the value before saving it: the server must be an http(s) URL, `insecure` must be a boolean, and
the CA file must exist and hold a PEM certificate. Relative paths are saved as absolute paths. If an environment
variable overrides the key you just set, `ado` says so. The token is only set by `ado auth login`. `--json` works with
`config list` and `config get`.

### TLS certificates

By default the server's certificate must be trusted by the operating system. For internal servers there are two options:

| Option | Flag | Environment | Config |
|---|---|---|---|
| Also trust a company CA (PEM file, may hold several certificates) | `--ca-cert <file>` | `ADO_CA_CERT=<file>` | `caCert` |
| Skip certificate checks entirely | `--insecure` | `ADO_INSECURE=1` | `insecure` |

Prefer the CA file: `ado` still checks the host name, so the token stays protected. `--insecure` accepts any
certificate. Use it only on a network you trust. While it is on, `ado` prints a warning that names the setting
responsible.

Both can be switched off for one command or one shell, whatever is saved:

```bash
ado pr list --insecure=false             # check certificates this time
ado pr list --ca-cert none               # ignore the saved CA file this time
export ADO_INSECURE=0                    # for this shell (accepts 1/0, true/false, yes/no)
```

`ado auth status` prints the TLS mode in effect and where it comes from:

```
Logged in to https://tfs.company.local/tfs/DefaultCollection as Jane Doe
TLS: system certificate store + CA file /home/me/company-ca.pem (caCert, from config file)
```

Given to `ado auth login`, `--insecure`, `--insecure=false` and `--ca-cert` are saved in the config file:

```bash
ado auth login https://tfs.company.local/tfs/DefaultCollection --ca-cert ~/company-ca.pem
```

The same setting is passed to git (`http.sslCAInfo` or `http.sslVerify=false`) for `repo clone`, `pr checkout` and
`pr diff`. `repo clone` also writes it into the new clone's git config, so a later `git pull` works too. For git the
CA file replaces the system certificates rather than adding to them. If the server's certificate is rejected, `ado`
says so, names both options and exits with 1.

### Proxy

Without a `proxy` setting, `ado` uses the system proxy: `HTTPS_PROXY` / `HTTP_PROXY` / `NO_PROXY` on Linux, and
the OS proxy settings on Windows and macOS. Set `proxy` to use a specific one, or `none` to connect directly:

```bash
ado config set proxy http://proxy.company.local:8080
ado config set proxy http://user:password@proxy.company.local:8080   # proxy with Basic authentication
ado config set proxy socks5://jumphost:1080
ado config set proxy none                                            # direct, ignore HTTPS_PROXY
ado pr list --proxy none                                             # just this once
export ADO_PROXY=http://proxy.company.local:8080                     # for this shell
```

Supported schemes are `http`, `https`, `socks4`, `socks4a` and `socks5`. Without credentials in the URL, a proxy
that asks for NTLM or Kerberos gets your signed-in Windows/domain credentials, as a browser would. Special characters
in the password must be URL-encoded (`@` is `%40`). The password is never printed: `config list`, `auth status` and
error messages show it as `****`. The config file holding it is readable only by you. A proxy that rejects the
credentials gives a clear error (exit code 1). `ado auth status` shows the proxy in effect:

```
Proxy: http://proxy.company.local:8080 (from ADO_PROXY)
```

`ado auth login <url> --proxy <url>` saves the proxy along with the login. git uses the same proxy for `repo clone`,
`pr checkout` and `pr diff` (as `remote.origin.proxy`, so other remotes are not affected). `repo clone` stores it in the
new clone without the password; git then asks for the password, or gets it from your credential helper.

### Supported servers

| Server | Highest REST API version |
|---|---|
| Azure DevOps Server 2019 | 5.0 (5.1 on Update 1) |
| Azure DevOps Server 2020 | 6.0 |
| Azure DevOps Server 2022 | 7.0 |

`ado` sends `api-version=5.0` by default, which all of them accept and every command works with.
To use a newer one, run `ado config set apiVersion 6.0` (or set `ADO_API_VERSION=6.0`). If the server does not
support the version, `ado` says so and exits with 1. TFS 2018 and older are not supported.

Inside a clone of an Azure DevOps repository, project and repository are taken from the `origin` remote,
so most commands need no arguments. Elsewhere, pass `--project <name>` and `--repo <name>`.

## Development

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
layering in [docwizz.yaml](docwizz.yaml) (`Cli` may use `Api` and `Git`, never the other way round) for problems the
change introduces. See [PLAN.md](PLAN.md) for scope and non-goals.
