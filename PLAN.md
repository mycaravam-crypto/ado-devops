# PLAN.md

# ADO CLI

A small, focused command-line client for Azure DevOps Server On-Prem,
inspired by GitHub CLI (`gh`).

The goal is not to implement the entire Azure DevOps API.

The goal is to make the most common developer workflows fast and
scriptable from the terminal.

---

## Current state (version 0.1.0)

All eight phases in section 32 are done, and so is the section 37 command set. The [README](README.md) describes
what the CLI does today. The plan below is the original brief and is kept as written. These parts have been
extended or changed since, each for a real requirement:

| Plan | Now |
|---|---|
| §5–7, §18 commands | Added `ado repo status`, `ado pr context <id>` (one JSON document for scripts and local agents), `pr diff --json` and `--json` on `pr approve` / `pr merge` |
| §15 work items | `ado workitem create` and `ado workitem edit <id>` set fields through named options or `--field Name=value`. `ado workitem list` filters by type, state, assignee (`--all` for everyone), area, iteration, tag, title or description text and raw `--wiql` in the query and shows priority, iteration and date last changed; `--ids` pipes into `ado workitem edit -`, which edits many items with a preview, confirmation (`--yes`), `--dry-run` and `--replace-title old --with new` |
| §6 configuration | `ado config list/get/set/unset` show each setting, its value and its source, and edit `~/.ado/config.json` with validation. Keys: `server`, `pat`, `project`, `apiVersion`, `insecure`, `caCert`, `proxy`, each with an `ADO_*` variable |
| §17 structure | No `Auth/` or `Models/` folders: auth lives in `Cli/AuthCommands.cs`, models in `Api/Models.cs`. `Api/Tls.cs` and `Api/Proxy.cs` were added |
| §18 API versioning | Instead of per-area constants, one version (default `5.0`, which works on Azure DevOps Server 2019–2022) is added in `AdoClient.Url`. It can be changed with `apiVersion` / `ADO_API_VERSION` |
| §30 TLS | Internal servers needed it, so `--ca-cert` / `caCert` (preferred: an additional trusted CA, host name still checked) was added, along with an explicit `--insecure` / `insecure` that prints a warning on every use. Both are passed to git |
| §30 security | Proxy support (`--proxy` / `proxy`, http(s) or SOCKS, `none` for direct). Passwords are masked in all output. Server URLs that contain credentials are rejected |
| §21 output | Data goes to stdout. Messages, prompts and errors go to stderr, so `--json` output is always clean |
| CI | GitHub workflows run build and tests (`test.yml`) and docwizz documentation and layering checks (`docwizz.yml`, rules in `docwizz.yaml`) on every PR |

---

## 1. Goals

### Primary goal

Provide a simple CLI for Azure DevOps Server that feels similar to `gh`:

```bash
ado repo list
ado repo clone <repo>

ado pr list
ado pr show <id>
ado pr diff <id>
ado pr checkout <id>
ado pr create
ado pr approve <id>
ado pr merge <id>

ado workitem show <id>
ado workitem list

ado build list
ado build run <id>
````

The exact initial command set should remain small.

### Design principles

1. YAGNI
2. KISS
3. CLI-first
4. Scriptable
5. Fast startup
6. Minimal dependencies
7. Clear error messages
8. Azure DevOps Server first
9. No unnecessary abstraction
10. Prefer standard .NET functionality over custom frameworks

The application should solve real developer workflows, not attempt to
reimplement Azure DevOps.

---

# 2. Non-Goals

Do NOT implement:

* Full Azure DevOps API coverage
* Azure DevOps UI
* TFS administration
* Extension marketplace
* Pipeline designer
* Work item designer
* Interactive terminal UI
* Plugin system
* Scripting language
* Configuration DSL
* Generic API proxy
* GraphQL layer
* Local database
* Telemetry
* Background daemon
* Cloud synchronization
* AI features
* Complex caching
* Dependency injection framework
* CQRS
* MediatR
* Repository pattern
* Unit-of-work pattern
* Event bus
* Microservices

If a feature does not directly improve a common CLI workflow,
do not implement it.

---

# 3. Technology

Target:

```text
.NET 10
C# 14
```

Project type:

```text
dotnet new console
```

Recommended packages should be kept to a minimum.

Prefer:

```text
System.Net.Http
System.Text.Json
System.CommandLine
```

If `System.CommandLine` introduces unnecessary complexity for the
required command set, use a small custom argument parser instead.

Do not add a dependency merely because it is convenient.

---

# 4. CLI Name

Use:

```text
ado
```

Example:

```bash
ado pr list
```

The executable should be:

```text
ado
```

---

# 5. Initial Command Surface

Keep the first release deliberately small.

## Authentication

```bash
ado auth login
ado auth status
ado auth logout
```

Login should support Azure DevOps Server.

Example:

```bash
ado auth login https://tfs.company.local/tfs/DefaultCollection
```

The user should be able to provide a PAT securely.

Do not implement OAuth initially.

Do not store credentials in the repository.

Use the operating system's normal user configuration location.

If secure OS credential storage requires a large dependency,
initially store the PAT in the user's configuration with restrictive
permissions where supported.

---

# 6. Configuration

Configuration should be minimal.

Example:

```text
~/.ado/config.json
```

Example:

```json
{
  "server": "https://tfs.company.local/tfs/DefaultCollection",
  "pat": "..."
}
```

Do not create a configuration framework.

Required configuration:

* Server URL
* PAT
* Optional default project

Environment variables should be supported for CI:

```text
ADO_SERVER
ADO_PAT
ADO_PROJECT
```

Environment variables override configuration.

Never print PAT values.

Never include credentials in error messages.

---

# 7. Repository Commands

Initial commands:

```bash
ado repo list
ado repo show <repo>
ado repo clone <repo>
```

Repository discovery should use the Azure DevOps REST API.

Example:

```bash
ado repo list
```

Output:

```text
NAME             PROJECT       DEFAULT BRANCH
my-api           Platform      main
frontend         Platform      main
documentation    Products      develop
```

Keep output human-readable.

Provide machine-readable output:

```bash
ado repo list --json
```

Do not build a sophisticated output framework.

A simple JSON serializer is sufficient.

---

# 8. Pull Request Commands

Pull requests are the most important feature.

Initial command set:

```bash
ado pr list
ado pr show <id>
ado pr diff <id>
ado pr checkout <id>
ado pr create
ado pr approve <id>
ado pr merge <id>
```

## `ado pr list`

Examples:

```bash
ado pr list
ado pr list --mine
ado pr list --status active
```

Default output:

```text
ID     TITLE                         AUTHOR       STATUS
142    Fix import validation        hannovb      Active
139    Improve error handling       max          Active
131    Update documentation         anna         Completed
```

Keep filtering minimal.

Do not implement an expression language.

---

# 9. `ado pr show`

Example:

```bash
ado pr show 142
```

Output:

```text
PR #142
Fix import validation

Author:     hannovb
Status:     Active
Repository: connector
Target:     main
Source:     feature/import-validation

Description:
...

Changes:
  8 files
  +124
  -31
```

Potential additional information:

```text
Reviewers:
  ✓ Alice
  ? Bob
```

Only implement this if the API makes it simple.

---

# 10. `ado pr diff`

Example:

```bash
ado pr diff 142
```

The command should retrieve the PR changes and render a useful
unified diff.

Do not build a custom diff renderer.

Use the existing git executable when possible.

Potential implementation:

1. Retrieve PR source/target information.
2. Fetch the relevant refs.
3. Execute:

```bash
git diff <target>...<source>
```

This keeps the CLI small.

---

# 11. `ado pr checkout`

Example:

```bash
ado pr checkout 142
```

Expected behavior:

1. Resolve repository.
2. Resolve source branch.
3. Fetch branch.
4. Checkout a local branch.

Suggested local branch:

```text
pr/142
```

Do not implement complicated branch naming logic.

If the working tree is dirty, abort with a clear message.

Never silently destroy user changes.

---

# 12. `ado pr create`

Initial implementation should support:

```bash
ado pr create
```

Interactive prompts:

```text
Title:
Description:
Source branch:
Target branch:
```

Also support automation:

```bash
ado pr create \
  --title "Fix import validation" \
  --source feature/import-validation \
  --target main
```

Do not implement a full interactive form framework.

Simple console prompts are sufficient.

---

# 13. `ado pr approve`

Example:

```bash
ado pr approve 142
```

Use the current authenticated user.

Do not initially implement:

```text
--reviewer
--vote-level
--comment
```

unless required by the Azure DevOps Server API behavior.

Keep approval semantics simple.

---

# 14. `ado pr merge`

Example:

```bash
ado pr merge 142
```

Initially support the merge methods exposed by the API.

If required:

```bash
ado pr merge 142 --squash
```

Do not create an abstraction around every possible Azure DevOps merge
strategy until there is an actual need.

Before merging, display:

```text
PR #142
Fix import validation

Target: main
Source: feature/import-validation

Merge? [y/N]
```

Never make destructive operations implicit.

CI usage may bypass the prompt:

```bash
ado pr merge 142 --yes
```

---

# 15. Work Items

Work items are secondary to PRs.

Initial commands:

```bash
ado workitem show <id>
ado workitem list
```

Example:

```bash
ado workitem show 4711
```

Display:

```text
#4711
Improve import validation

Type:     User Story
State:    Active
Assigned: hannovb

Description:
...
```

Do not implement a complete Boards client.

No editing initially.

---

# 16. Builds

Build support should remain minimal.

Initial commands:

```bash
ado build list
ado build show <id>
ado build run <id>
```

The purpose is to quickly inspect and trigger builds.

Do not implement:

* Pipeline editor
* YAML editor
* Build log UI
* Artifact management

Build logs can be opened through the browser in a future version.

---

# 17. Azure DevOps API Client

Create one small API client.

Suggested structure:

```text
src/
  AdoCli/
    Program.cs
    Cli/
    Api/
    Auth/
    Git/
    Models/
```

Avoid excessive layering.

Example:

```text
Api/AdoClient.cs
Api/AdoException.cs
Api/Models/
```

`AdoClient` should wrap `HttpClient`.

Example conceptual API:

```csharp
public sealed class AdoClient
{
    public Task<IReadOnlyList<Repository>> GetRepositoriesAsync(...);

    public Task<PullRequest> GetPullRequestAsync(...);

    public Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(...);

    public Task CreatePullRequestAsync(...);

    public Task UpdatePullRequestAsync(...);

    public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(...);

    public Task<WorkItem> GetWorkItemAsync(...);

    public Task<IReadOnlyList<Build>> GetBuildsAsync(...);

    public Task<Build> QueueBuildAsync(...);
}
```

Do not create an interface for every class.

Do not create repositories around the API client.

---

# 18. API Versioning

Azure DevOps Server versions differ.

Do not hard-code assumptions throughout the application.

Keep API version constants in one place:

```csharp
internal static class ApiVersions
{
    public const string Core = "...";
    public const string Git = "...";
    public const string Build = "...";
    public const string WorkItem = "...";
}
```

The API client should construct URLs centrally.

Example:

```text
{server}/{project}/_apis/git/repositories
```

Avoid duplicating URL construction throughout commands.

---

# 19. HTTP Behavior

Use a single `HttpClient`.

Configure:

```text
Authorization
Accept
User-Agent
Timeout
```

Handle common failures:

```text
401 -> authentication error
403 -> permission error
404 -> resource not found
409 -> conflict
429 -> rate limiting
5xx -> Azure DevOps Server error
```

Errors must be translated into useful CLI messages.

Example:

```text
Error: authentication failed.

Run:
  ado auth login
```

Not:

```text
HttpRequestException: Response status code...
```

unless `--debug` is enabled.

---

# 20. Debugging

Support:

```bash
ado --debug ...
```

Debug output should contain:

```text
HTTP method
URL
status code
timing
```

Never print:

```text
Authorization
PAT
cookies
```

---

# 21. Output

Human-readable output is the default.

Example:

```bash
ado pr list
```

Machine-readable output:

```bash
ado pr list --json
```

Avoid supporting multiple output formats initially.

Do NOT implement:

```text
--yaml
--csv
--xml
--table
--template
--jq
```

JSON is enough.

---

# 22. Colors

Use colors sparingly.

Examples:

```text
Active
Completed
Abandoned
```

Colors should improve readability but never be required.

Automatically disable colors when:

```text
stdout is not a terminal
```

This makes scripting reliable.

---

# 23. Git Integration

The CLI should use the locally installed Git executable for Git
operations.

Do not implement Git functionality ourselves.

Examples:

```text
git fetch
git checkout
git diff
git status
```

Create one tiny wrapper:

```text
Git/GitClient.cs
```

It should execute Git and return:

```text
exit code
stdout
stderr
```

No Git abstraction framework.

---

# 24. Project Detection

The CLI should try to infer context from the current Git repository.

Example:

```bash
cd connector
ado pr list
```

It should attempt to determine:

```text
Azure DevOps server
Project
Repository
```

from:

```text
.git/config
```

If detection fails, require explicit configuration.

Do not create a complex workspace configuration system.

---

# 25. URL Handling

Azure DevOps Server installations can use different URL layouts.

Examples:

```text
https://server/tfs/DefaultCollection
https://server/DefaultCollection
https://server/tfs
```

The server URL must be treated as configurable.

Do not assume Azure DevOps Services URL structure.

This is one of the primary reasons for creating this CLI.

---

# 26. Testing

Keep testing proportional to the application.

Required:

### Unit tests

Test:

* argument parsing
* configuration loading
* API URL generation
* API error mapping
* JSON serialization
* Git command construction

### Integration tests

Use a mock HTTP server.

Do NOT require a real Azure DevOps Server for normal tests.

Do not introduce Testcontainers unless there is an actual need.

---

# 27. Architecture

Keep the architecture deliberately flat:

```text
CLI
 │
 ├── Auth
 │
 ├── AdoClient
 │
 └── GitClient
```

Commands call the clients directly.

Avoid:

```text
CLI
 ↓
Application
 ↓
Domain
 ↓
Infrastructure
 ↓
Repository
 ↓
Service
 ↓
Factory
 ↓
Adapter
 ↓
Provider
```

This is a CLI, not a multinational bank.

---

# 28. Error Handling

Commands should return meaningful exit codes.

Suggested:

```text
0 = success
1 = general failure
2 = invalid command/arguments
3 = authentication failure
4 = permission failure
5 = resource not found
6 = conflict
```

Do not expose stack traces unless:

```bash
--debug
```

---

# 29. Performance

The application should start quickly.

Avoid:

* Reflection-heavy frameworks
* Dependency injection containers
* Large third-party UI libraries
* Database initialization
* Network calls during startup
* Configuration discovery beyond what is necessary

Only perform network calls after a command requires them.

---

# 30. Security

Never:

* Print PATs
* Store PATs in Git
* Log Authorization headers
* Include PATs in URLs
* Send credentials to third-party services

HTTPS should be the default.

For internal/self-signed certificates, do not blindly disable TLS
validation.

If users need custom CA handling, add it only after a real requirement
exists.

Do not implement insecure:

```text
--ignore-certificate-errors
```

as a convenience feature.

---

# 31. Documentation

README should contain only:

1. What it is
2. Installation
3. Authentication
4. Common commands
5. Configuration
6. Examples
7. Development

Example:

```bash
ado auth login https://tfs.company.local/tfs/DefaultCollection

ado repo list

ado pr list

ado pr show 142

ado pr checkout 142

ado pr approve 142

ado pr merge 142
```

Do not create a huge documentation site.

---

# 32. Implementation Order

Implement in this order.

## Phase 1: Skeleton

* Create .NET 10 console application
* Add command parsing
* Add configuration
* Add HTTP client
* Add basic error handling
* Add `--help`
* Add `--version`

Acceptance:

```bash
ado --help
ado --version
```

works.

---

## Phase 2: Authentication

Implement:

```bash
ado auth login
ado auth status
ado auth logout
```

Acceptance:

```bash
ado auth status
```

correctly reports authenticated/unauthenticated state.

---

## Phase 3: Repository

Implement:

```bash
ado repo list
ado repo show
```

Add Git repository context detection.

---

## Phase 4: Pull Requests

Implement:

```bash
ado pr list
ado pr show
ado pr diff
```

This is the first major usable milestone.

---

## Phase 5: Git Integration

Implement:

```bash
ado pr checkout
```

Use system Git.

---

## Phase 6: PR Mutation

Implement:

```bash
ado pr create
ado pr approve
ado pr merge
```

Require explicit confirmation for destructive operations.

---

## Phase 7: Work Items

Implement:

```bash
ado workitem list
ado workitem show
```

Only after PR functionality is stable.

---

## Phase 8: Builds

Implement:

```bash
ado build list
ado build show
ado build run
```

Only after the core CLI is useful.

---

# 33. Definition of Done

Version 1.0 should allow a developer to perform this workflow without
opening the Azure DevOps web UI:

```bash
ado auth login ...

ado repo list

cd my-repository

ado pr list
ado pr show 123
ado pr checkout 123
ado pr diff 123

ado pr approve 123
ado pr merge 123
```

The CLI should also work in scripts:

```bash
ado pr list --json
```

and return appropriate exit codes.

---

# 34. YAGNI Rules for Future Development

Before implementing any new feature, ask:

1. Is this required by a real workflow?
2. Does it remove friction?
3. Can it be implemented simply?
4. Can an existing command solve the problem?
5. Does it add a new abstraction?
6. Does it add a dependency?
7. Does it increase maintenance cost?

If the answer to 1 or 2 is no:

DO NOT IMPLEMENT IT.

If a feature requires a large framework, first look for a simpler
implementation.

If a feature can be implemented in 30 lines instead of introducing
another architectural layer, use the 30 lines.

---

# 35. Explicit Anti-Bloat Rules

Do not add:

* Dependency injection unless actually necessary
* Interfaces without multiple implementations
* Generic repositories
* Generic result types
* MediatR
* AutoMapper
* FluentValidation
* Entity Framework
* Serilog unless logging requirements justify it
* Spectre.Console unless its value clearly outweighs the dependency
* Polly unless retry requirements justify it
* Configuration frameworks
* Plugin architecture
* Event-driven architecture
* Domain-driven design
* CQRS

The codebase should remain understandable by one developer.

---

# 36. Claude Code Implementation Instructions

Claude Code must implement incrementally.

Before writing code:

1. Inspect the repository.
2. Confirm the current .NET SDK.
3. Create/inspect the solution structure.
4. Keep the initial implementation minimal.
5. Do not introduce dependencies without justification.

For every implementation phase:

1. Implement the smallest working version.
2. Build.
3. Run tests.
4. Run the CLI manually.
5. Fix errors.
6. Only then continue.

Do not implement future phases early.

Do not create placeholder abstractions for future functionality.

Do not add "flexibility" that is not currently required.

After each phase, review the code for unnecessary complexity.

Delete code that became unnecessary.

---

# 37. Final Quality Gate

Before declaring version 1.0 complete, verify:

```bash
dotnet build
dotnet test

ado --help
ado --version
ado auth status
ado repo list
ado pr list
ado pr show <id>
ado pr diff <id>
ado pr checkout <id>
ado pr create
ado pr approve <id>
ado pr merge <id>
ado workitem show <id>
ado build list
```

Also verify:

```bash
ado pr list --json
```

produces valid JSON.

Verify that:

* credentials never appear in output
* failed API requests produce useful errors
* non-zero exit codes are correct
* Git failures are propagated
* commands work from a Git repository
* commands work without a Git repository where appropriate
* the application does not require Azure DevOps Services
* Azure DevOps Server URLs work correctly

---

# 38. Success Criterion

The project succeeds if a developer can replace common browser-based
Azure DevOps interactions with:

```bash
ado pr list
ado pr show 123
ado pr checkout 123
ado pr diff 123
ado pr approve 123
ado pr merge 123
```

The project fails if it becomes a miniature Azure DevOps clone.

Keep it small.
Keep it useful.
Keep it boring.

```

