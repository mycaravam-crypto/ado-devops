# Configuration and scripting

## Authentication

Create a personal access token (PAT) in Azure DevOps Server (scopes: Code read & write, Work items read & write, Build read & execute, and Test management read for `ado testplan`), then:

```bash
ado auth login https://tfs.company.local/tfs/DefaultCollection   # prompts for the PAT
ado auth status               # server, user, TLS mode and proxy in effect
ado auth logout               # deletes ~/.ado/config.json
```

`auth login` checks the token against the server before saving it, and keeps the project, API version, TLS and proxy
settings already in the config file. A server URL with embedded credentials is rejected; a plain `http://` URL works
but prints a warning, since the token then travels unencrypted.

In CI, pipe the token in: `echo "$PAT" | ado auth login <server-url>`, or skip login and set environment variables (below).

## Settings

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

## Shell defaults and scripts

Optionally set defaults in `~/.bashrc` (on macOS with zsh: `~/.zshrc`) so you don't need to pass them:

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

In PowerShell, set them in your profile (`notepad $PROFILE`; create it first with
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

**PowerShell 7 on Linux or macOS:** install the binary as for bash in the [README](../README.md#install). Then add the folder to `$env:PATH`
in `$PROFILE`: `$env:PATH = "$HOME/.local/bin:$env:PATH"`.

Don't put `ADO_PAT` in `~/.bashrc` or `$PROFILE`. Run `ado auth login` once instead (see [Authentication](#authentication)). It stores the token in
`~/.ado/config.json`. On Linux and macOS only you can read that file. On Windows it is `%USERPROFILE%\.ado\config.json`, protected by your user profile's permissions.

## JSON output and debugging

`--json` makes data commands print JSON on stdout. Messages, prompts and errors go to stderr, so stdout is always
either JSON or empty (`auth` commands print plain text only). `ado pr context <id>` and `ado commit context <commit>`
always print JSON. A response from the server that is not the expected JSON is reported as an error (exit code 1).

```bash
ado pr list --json | jq '.[].pullRequestId'
```

`--debug` logs each HTTP request to stderr (method, URL, status, timing; never credentials) and shows stack traces
on errors.

Status values are colored only when stdout is a terminal and `NO_COLOR` is not set.

`--json` does not change exit codes.

## Using `ado` as a local automation/AI tool interface

`ado` contains no AI. It is deterministic: the same server state gives the same output, and JSON lists are sorted
where the server does not fix the order. That makes it a safe tool for scripts and local agents, which can
work from JSON and exit codes alone:

```bash
ado repo status --json        # where am I?
ado pr context 142 --json     # PR, reviewers, commits, changed files, linked work items
ado pr diff 142 --json        # changed files; without --json, the full unified diff
ado commit context HEAD       # a commit and its explicitly linked work items
ado workitem show 4711 --json
ado workitem context 4711     # title, description, acceptance criteria, parent/child/related links
ado workitem search "login timeout" --json   # ranked candidates; then `ado workitem context <id>` for the chosen one
ado workitem list --all --tag xyz --ids   # ids only, one per line, for `ado workitem edit -`
ado build show 815 --json
```

Commands that change state (`pr create`, `pr approve`, `pr merge`, `workitem create`, `workitem edit`,
`testplan import`, `build run`) never prompt when given `--title` / `--yes`; `workitem edit` and `testplan import`
on several items fail instead of prompting when stdin is not a terminal and `--yes` is missing. Credentials never appear in any output.
