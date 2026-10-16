# ado

A small command-line client for **Azure DevOps Server** (on-prem), in the spirit of `gh`.
It covers the everyday workflow — repositories, pull requests, work items, test plans, builds — not the whole API.

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

## Getting help

```bash
ado --help                    # overview: commands, global flags, environment variables, exit codes
ado workitem --help           # one command's subcommands, flags and examples (also: ado workitem, ado help workitem)
ado pr merge --help           # same as ado pr --help
```

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
ado pr list [--mine] [--status active|completed|abandoned|all] [--limit n]
ado pr show 142               # details, changed file count, description, reviewer votes
ado pr context 142            # PR, commits, changed files and linked work items as one JSON document
ado pr checkout 142           # local branch pr/142; refuses to run on a dirty tree
ado pr diff 142               # git diff target...source
ado pr approve 142
ado pr merge 142 [--squash] [--yes]
ado pr create                 # prompts; or --title t [--description d] [--source b] [--target b]
```

- `pr list` shows all pull requests of the current repository, or of the whole project outside a clone, with their
  ID, title, author, target branch and status; `--limit n` stops after n. Without `--status` it lists active ones.
- `pr checkout` and `pr diff` run git and must be run inside a clone of the PR's repository. If `pr/<id>` already
  exists, `pr checkout` only fast-forwards it, so local commits on it are never lost. `pr diff --json` lists the
  changed files from the server and works anywhere.
- `pr create` uses the current branch as source and the repository's default branch as target unless told otherwise.
  When stdin is not a terminal, `--title` is required.
- `pr merge` shows the PR and asks for confirmation (`--yes` / `-y` skips it). If branch policies still have to pass,
  the server only queues the completion, and `ado` says so.

## Commits

```bash
ado commit context HEAD       # inside a clone: the commit and the work items explicitly linked to it, as JSON
ado commit context 4b825dc642cb6eb9a060e54bf8d69288fbee4904 --project Platform --repo backend
ado commit context HEAD | jq '.workItems.items[].id'
```

`commit context` is read-only (GET requests only) and always prints JSON. Outside a clone of the repository the
commit must be a full 40-character SHA; inside one, a short SHA, `HEAD` or a branch is resolved with `git rev-parse`.
The repository comes from the clone or `--project`/`--repo`; `ado` never searches other repositories for a commit.
Links come from the server's commit↔work item links (`searchCriteria.includeWorkItems`), never from commit messages.

```json
{
  "schema": "ado.commit-context/v1",
  "server": "https://tfs.company.local/tfs/DefaultCollection",
  "project": "Platform",
  "repository": { "id": "2222…", "name": "backend" },
  "commit": {
    "commitId": "4b825dc642cb6eb9a060e54bf8d69288fbee4904",
    "parents": ["…"],
    "author": { "name": "Jane", "date": "2026-10-01T08:00:00Z", "email": "jane@company.local" },
    "committer": { "name": "Jane", "date": "2026-10-01T08:05:00Z", "email": "jane@company.local" },
    "comment": "Fix login timeout"
  },
  "workItems": {
    "status": "verified",
    "items": [
      { "id": 4711, "source": "commit-workitem", "url": "https://tfs.company.local/tfs/DefaultCollection/_apis/wit/workItems/4711",
        "webUrl": "https://tfs.company.local/tfs/DefaultCollection/_workitems/edit/4711" }
    ]
  },
  "warnings": []
}
```

- `workItems.status` is `verified` when the server reported the commit's links; `items` is then complete and `[]`
  means the commit has no linked work items. It is `unavailable` when the server did not report them (an older server
  ignoring `includeWorkItems`, or a different commit coming back); `items` is then left out and `warnings` says why.
  Unknown is never reported as "no links".
- Items are sorted by id and listed once. A commit that does not exist exits with 5, missing permissions with 4.

## Work items

```bash
ado workitem list             # open items assigned to you (current project, if known); --limit n for the n most recently changed
ado workitem list --type Bug --state Active,Resolved
ado workitem list --limit 200 --summary            # displayed count, type/state/priority breakdown; no extra API call
# Text output includes ASSIGNED TO and effective filters (by default @Me and open states).
# --all removes only the assignee filter; use --state any for closed items as well.
ado workitem list --all --state any --contains tzu   # everyone's items, any state, "tzu" in title or description
ado workitem show 4711        # title, type, state, assignee, then every other field the server returns
ado workitem context 4711     # requirement context as JSON: text, acceptance criteria, parent/child/related links
ado workitem search "sevrer crash"   # titles like the query, despite typos, partial words or word order
ado workitem create           # prompts; or --type Bug --title t [--description d] [field options]
ado workitem edit 4711 --state Active --assigned-to jane@company.local --comment "Picked up"
```

Field options, for both `create` and `edit`:

| Option | Field |
|---|---|
| `--title` | `System.Title` |
| `--description` | `System.Description` (plain text; line breaks are kept) |
| `--state` | `System.State` |
| `--assigned-to` | `System.AssignedTo` (display name, e-mail or `DOMAIN\user`; `""` unassigns) |
| `--area` | `System.AreaPath` |
| `--iteration` | `System.IterationPath` |
| `--tags` | `System.Tags` (`"a; b"`; replaces the existing tags) |
| `--comment` | adds a comment to the discussion (`System.History`) |
| `--field Name=value` | any field by reference name, e.g. `--field Microsoft.VSTS.Common.Priority=1`; can be repeated |

- `workitem list` shows id, type, state, priority, iteration, date last changed and title. `--type` and `--state` keep
  only those types and states; both can be repeated or take a comma-separated list (`--type Bug,"User Story"`), and the
  server matches them case-insensitively. Without `--state` it lists every state but Closed, Done and Removed; with it,
  closed items can be listed too (`--state Closed`). The filters go into the query, so `--limit n` returns the n most
  recently changed matches. `--json` prints the same fields.
- More filters for `workitem list`, all ANDed into the query:

  | Option | Keeps items |
  |---|---|
  | `--all` | of everyone, not just assigned to you |
  | `--assigned-to who` | assigned to `who` (`@me` for you); replaces the default |
  | `--state any` | in any state, closed ones included |
  | `--area path`, `--iteration path` | under that area / iteration path |
  | `--tag t` | with that tag; repeat or comma-separate for several (all must match) |
  | `--title-contains text` | whose title contains `text` (ignoring case) |
  | `--contains text` | whose title or description contains `text` |
  | `--wiql "condition"` | matching a WIQL condition of your own, e.g. `--wiql "[Microsoft.VSTS.Common.Priority] = 1"` |

  `--ids` prints only the ids, one per line, for piping into `workitem edit -`.
- `workitem create` needs a project (see [Configuration](#configuration)). The type is the process template's name,
  e.g. `Bug`, `Task` or `"User Story"`. When stdin is not a terminal, `--type` and `--title` are required.
  It prints the new id; `--json` prints the whole work item.
- `workitem edit` changes only the fields given. The server checks the values: an unknown state,
  user or field fails with its message and exit code 1. `--json` prints the updated work item.

### Changing many work items at once

`workitem edit` takes several ids, or `-` to read them from stdin, so a filtered list can be edited in one go:

```bash
# close every open Bug tagged xyz, whoever it is assigned to
ado workitem list --all --type Bug --tag xyz --ids | ado workitem edit - --state Closed --comment "Bulk close" --yes

# rename: replace "abc" with "xyz" in every title that contains it
ado workitem list --all --state any --title-contains abc --ids | ado workitem edit - --replace-title abc --with xyz --dry-run

ado workitem edit 4711 4712 4713 --assigned-to jane@company.local
```

- With more than one work item, `edit` lists them and the changes, then asks `Update n work items? [y/N]`.
  `--yes` skips the question; it is required when stdin is not a terminal, which includes reading ids with `-`.
- `--dry-run` only prints what would change (`--json`: id and fields per item) and changes nothing.
- `--replace-title old --with new` replaces every occurrence of `old` in each title, ignoring case like
  `--title-contains`. Items whose title does not contain it are skipped. It cannot be combined with `--title`.
- Items are updated one by one. One the server rejects (a state the process does not allow, say) is reported as
  `#id: message` and the rest are still updated; the exit code is then 1. `--json` prints the updated work items.
- The state names depend on the process template: `Closed` (Agile, CMMI), `Done` (Scrum, Basic) or `Removed`.

### Requirement context

`ado workitem context <id>` prints the business intent of a work item as JSON (schema `ado.requirement-context/v1`):
title, type, state, description, acceptance criteria, revision and the explicitly linked parent, child and related
work items. It is read-only and always prints JSON.

```bash
ado workitem context 4711
ado workitem context 4711 --revision 3                        # the text as it was at revision 3
ado workitem context 4711 --criteria-field Custom.Akzeptanzkriterien   # custom process template
ado workitem context 4711 | jq -r '.acceptanceCriteria.items[].text'
```

```json
{
  "schema": "ado.requirement-context/v1",
  "server": "https://tfs.company.local/tfs/DefaultCollection",
  "id": 4711,
  "revision": 5,
  "currentRevision": 5,
  "isCurrent": true,
  "url": "https://tfs.company.local/tfs/DefaultCollection/_apis/wit/workItems/4711",
  "webUrl": "https://tfs.company.local/tfs/DefaultCollection/Platform/_workitems/edit/4711",
  "project": "Platform",
  "type": "User Story",
  "state": "Active",
  "title": "Login times out",
  "changedDate": "2026-10-01T08:00:00.123Z",
  "description": {
    "checkedFields": ["System.Description", "Microsoft.VSTS.TCM.ReproSteps"],
    "sourceField": "System.Description",
    "html": "<div>Users are logged out &amp; lose work.</div>",
    "text": "Users are logged out & lose work."
  },
  "acceptanceCriteria": {
    "checkedFields": ["Microsoft.VSTS.Common.AcceptanceCriteria"],
    "sourceField": "Microsoft.VSTS.Common.AcceptanceCriteria",
    "html": "<ul><li>Session lasts <b>8h</b></li><li>Warning before logout</li></ul>",
    "text": "- Session lasts 8h\n- Warning before logout",
    "items": [ { "index": 1, "text": "Session lasts 8h" }, { "index": 2, "text": "Warning before logout" } ]
  },
  "relations": {
    "status": "current",
    "items": [
      { "kind": "parent", "rel": "System.LinkTypes.Hierarchy-Reverse", "id": 100,
        "url": "https://tfs.company.local/tfs/DefaultCollection/_apis/wit/workItems/100",
        "title": "Reliable sessions", "type": "Feature", "state": "Active" }
    ]
  },
  "warnings": []
}
```

- **Fields:** the description is the first non-empty one of `System.Description` and `Microsoft.VSTS.TCM.ReproSteps`
  (Bugs); acceptance criteria come from `Microsoft.VSTS.Common.AcceptanceCriteria`. For custom or localized process
  templates, `--description-field <ref>` and `--criteria-field <ref>` name the field to read instead. `checkedFields`
  lists what was tried and `sourceField` what was used; when no field has a value, `sourceField`, `html` and `text` are
  left out and `items` is empty. Nothing is filled in from another field or revision.
- **Text:** `html` is the raw field value, `text` its plain text: tags removed, line breaks kept, list items as `- `,
  entities decoded; script and style content and comments are dropped. Nothing is rendered or run.
- **Criteria items:** the list items when the field is an HTML list, otherwise its non-empty lines, in their original
  order. `index` is only the position, not a stable identifier.
- **Relations:** only explicit parent (`Hierarchy-Reverse`), child (`Hierarchy-Forward`) and related links, sorted by
  kind, then id; nothing is inferred from titles or paths. A linked item that is deleted or not visible to you keeps its
  id and link but has no title, type or state, and `warnings` says so.
- **Revisions:** `--revision n` reads the fields of revision n (`isCurrent: false`). The server only reports the links
  of the current revision, so for an older revision `relations.status` is `unavailable` and `items` is left out; current
  links are never shown as historical. A revision after the current one exits with 5.

### Search

`ado workitem search <query>` finds work items whose title resembles the query: typos (`sevrer` → "Server"), partial
words (`auth` → "Authentication") and another word order (`login fehler` → "Fehler beim Login") still match. Every
state and everyone's items are searched; `--type`, `--state` and `--project` narrow it down, `--limit n` shows the n
best (default 10).

```bash
ado workitem search "login fehler"
ado workitem search sevrer --type Bug --state Active
ado workitem search "token expiry" --json | jq '.items[0].id'
ado workitem search 4711       # the work item with that id first, then titles containing 4711
```

```json
{
  "schema": "ado.workitem-search/v1",
  "query": "sevrer",
  "candidates": { "count": 12, "max": 500, "truncated": false },
  "items": [
    { "id": 4711, "title": "Server crashes on start", "type": "Bug", "state": "Active", "createdBy": "Ann Lee",
      "assignedTo": "Bo Chen", "score": 0.64, "webUrl": "https://tfs.company.local/tfs/DefaultCollection/Platform/_workitems/edit/4711",
      "match": "candidate" }
  ]
}
```

- **How:** the server returns up to 500 candidates, most recently changed first: work items whose title contains a
  3-letter piece of a query word, or of that word with two neighbouring letters swapped. It checks your permissions as
  for any query; there is no local index or cache. `ado` then ranks them: how well each query word matches a title
  word (exact, prefix, one typo up to 5 letters, two from 6), plus the 3-letter pieces both share.
- **Order:** an id match first (`match: "id"`), then by `score` (0–1), then newest id. The same server state gives the
  same output.
- **Limits:** only titles are searched. Words of up to 3 letters must match exactly or as a prefix. When
  `candidates.truncated` is true, the server had more candidates than 500 and a match may be missing; make the query
  more specific or narrow it down. `match: "candidate"` means similar text, not a verified link.

`ado testplan trace-export --area "Product\\ALPHA" --output alpha-tests.json` exports all relevant test plans as `ado.test-traceability/v2`. The optional positional plan ID retains the previous `v1` single-plan export. The v2 schema preserves exact `planId`, `suiteId` and `testCaseId` memberships, while deduplicating test cases.

`ado testplan trace-export <id> --area "Product\\ALPHA" --output alpha-tests.json` creates a read-only `ado.test-traceability/v1` snapshot for offline DocWizz import. Includes requirement IDs, explicit links to in-plan test-case IDs, suite assignments, and scope metadata. It omits steps, parameters and test execution evidence by design.

## Test plans

Export a test plan to one JSON file, edit test cases, steps and expected results in it — one or hundreds at once,
with an editor, `jq` or a script — and import the changes:

```bash
ado testplan list                                 # id, state, iteration and name of each plan in the project
ado testplan show 12                              # plan metadata, suite hierarchy, distinct test case count
ado testplan show 12 --json                       # structured read-only summary
ado testplan show 12 --with details              # suite cases, steps and expected results
ado testplan show 12 --with results              # actual outcomes from test runs
ado testplan show 12 --with all --json           # detailed machine-readable view
ado testplan export 12 --output plan.json         # without --output: JSON on stdout
ado testplan export 12 --format html --output plan.html # offline, read-only HTML report
ado testplan coverage 12 --area "Product\\ALPHA" --format html --output coverage.html
ado testplan import plan.json --dry-run           # what would change; writes nothing
ado testplan import plan.json                     # asks before updating several test cases (--yes skips that)
```

`show` is read-only and fetches the plan, suites and their test case IDs, not work-item fields or test steps.
Suites are shown in a stable hierarchy; test cases assigned to several suites count only once in the overall total.
Use `export` when you need a portable edit/import document. In `show`, expected results are part of the test case definition; `--with results` instead reads actual outcomes from test runs. An absent run is not a passing test. All modes are read-only.

HTML is a self-contained, read-only report with a DocWizz-style collapsible suite tree, sidebar filtering, active-section highlighting and a responsive mobile navigation drawer, and DocWizz-style light/dark theme switching (with system preference detection). It includes suite navigation, per-case Area Path, local parameter value tables, and tables of actions and expected results. The JSON export also includes `Microsoft.VSTS.TCM.Parameters` and `Microsoft.VSTS.TCM.LocalDataSource` in each test case’s `fields` (alongside the existing `System.AreaPath`). Shared parameter sets are not expanded; parameter values may contain sensitive test data, so protect exported files accordingly. Import continues to use JSON only.
Shared Steps references are resolved recursively in the HTML report from the referenced work item's `Microsoft.VSTS.TCM.Steps` field. Custom or localized work-item type names are supported; the export does not require the literal type name `Shared Steps`. A missing/inaccessible reference or empty steps field is shown as a warning in the report. Cyclic references are detected. The original shared-step references remain unchanged in JSON exports and imports.

### Test coverage by Area Path

`ado testplan coverage <id> --area <path>` produces a read-only report for **one** test plan and software area (including child Area Paths). This keeps historically different software projects such as ALPHA and GAMMA separate even if their code shares one repository and uses different branches. Git branches do not affect the calculation.

```bash
ado testplan coverage 12 --area "Product\\ALPHA"                         # text and uncovered requirements
ado testplan coverage 12 --area "Product\\ALPHA" --format json             # machine-readable
ado testplan coverage 12 --area "Product\\ALPHA" --format html --output coverage.html
```

Requirements are Azure DevOps work items of type `User Story`, `Product Backlog Item` or `Requirement`, across **all states**. A requirement counts as covered when its `Tests` / `Tested By` relation targets at least one test case assigned to a suite in the selected plan. The same test case in several suites counts once. The HTML report lists uncovered requirements.

| Measure | Numerator / denominator |
| --- | --- |
| Requirement coverage | Requirements linked to at least one test case in the plan / requirements in the Area Path |
| Execution coverage | Planned test cases whose latest recorded plan result is Passed, Failed or Aborted / distinct test cases in the plan |
| Pass rate | Planned test cases with latest result Passed / executed test cases (as defined above) |

Test results come from runs belonging to the selected plan; the latest available result per test case is used. `Blocked`, `NotExecuted` and missing results do **not** count as executed or passed. A zero denominator is reported as 0%. Coverage is **not** code coverage, and requirement coverage does not prove test quality or execution success.

The command is read-only. HTML reports are standalone and offline-viewable; JSON remains the editable format for `testplan export` / `import`.

### Test progress (Azure DevOps Server)

`ado testplan progress` reads **test points** (suite + test case + configuration) via the legacy Test REST API 5.0, not the private `/_testManagement/analytics/progressreport` web page. Each point contributes one current outcome, and a test case with multiple configurations contributes multiple points. Filtering with `--area` includes descendants but not similarly prefixed Area Paths.

```bash
ado testplan progress 12 --area "Product\\ALPHA"
ado testplan progress 12 --area "Product\\ALPHA" --days 30 --format json
ado testplan progress 12 --area "Product\\ALPHA" --format html --output progress.html
ado testplan export 12 --format html --with-results --area "Product\\ALPHA" --output plan.html
```

Current point outcomes show Passed, Failed, Blocked, and Not Run. Executed = Passed + Failed; pass rate = Passed / executed (0% for an empty denominator). All other states count as Not Run, so the results are **not guaranteed to equal** Azure DevOps Analytics KPIs. The 14/30-day history shows daily test-run *result events*, not historical test-point snapshots; a single test case can appear multiple times in the history. Filtering affects the point report and the history, not the underlying test case definitions in HTML export. A server that does not expose the legacy point API will report an API error; it will not silently substitute case-level metrics.

### Report quality and validation

For a shareable HTML export, use `--exclude-parameters` to omit local parameter definitions and parameter values from **HTML only**. JSON remains an editable round-trip and still contains these fields; protect JSON files accordingly. Shared-step contents may also contain sensitive test data.

```bash
ado testplan export 12 --format html --with-results --area "Product\\ALPHA" --exclude-parameters --output plan.html
```

Reports include a UTC generation timestamp, source and (when supplied) an Area Path. The progress report separates **current test-point status** (suite × case × configuration) from **historical test-run events** for the selected 14/30-day period. Trends count events, not past coverage snapshots. `Blocked` and missing outcomes do not count as executed in this tool, so percentages may differ from the Azure DevOps Analytics progress report.

**Manual server comparison checklist:** choose a plan with a test case in multiple suites/configurations; compare total points and Passed/Failed/Blocked/Not Run to the Azure DevOps result report for the same scope and time. Repeat for a plan with no runs and one with recent failures. Record any differences in state mapping or filtering instead of silently adjusting percentages. Automated parity is not asserted without an accessible server fixture.

### The file

```json
{
  "format": "ado-testplan/1",
  "project": "Platform",
  "plan": { "id": 12, "name": "Release 1", "state": "Active", "iteration": "Platform\\Sprint 1", "rootSuiteId": 13 },
  "suites": [
    { "id": 13, "name": "Release 1", "suiteType": "StaticTestSuite", "testCaseIds": [101] },
    { "id": 14, "name": "Login", "suiteType": "StaticTestSuite", "parentId": 13, "testCaseIds": [101, 102] }
  ],
  "testCases": [
    {
      "id": 101,
      "rev": 5,
      "title": "Login works",
      "fields": {
        "System.State": "Design",
        "System.AssignedTo": "Jane Doe <jane@company.local>",
        "System.AreaPath": "Platform",
        "System.IterationPath": "Platform\\Sprint 1",
        "System.Tags": "",
        "Microsoft.VSTS.Common.Priority": "2",
        "System.Description": ""
      },
      "steps": [
        { "id": 2, "action": "<DIV><P>Open the login page</P></DIV>", "expectedResult": "" },
        { "id": 3, "action": "Log in as <B>jane</B>", "expectedResult": "Start page shows „Willkommen“" },
        { "id": 4, "sharedStepsId": 555 }
      ]
    }
  ]
}
```

- `plan` and `suites` show the hierarchy: every suite with its parent and its test cases in order. A test case in
  several suites is listed once under `testCases`. Import never changes plans, suites or which test cases they contain.
- `id` and `rev` say which test case the entry is and which revision it was exported at. Don't change them.
- `title`, `fields` and `steps` can be edited. `fields` holds field values as text, by reference name. To change
  another field, add it, e.g. `"Custom.Component": "Login"`. Identities are
  `"Name <unique name>"`; plain e-mail addresses or `DOMAIN\user` work too.
- `steps` are the test steps in order. `action` and `expectedResult` are HTML, as Azure DevOps stores them, so plain
  text works too (write `&lt;` for `<`, `<br>` for a line break). The `id` links a step to its test results and
  attachments: keep it when you change or move a step, and leave it out for a new step. Delete an entry to remove
  the step. `{ "id": 4, "sharedStepsId": 555 }` is a reference to the Shared Steps work item 555; it can be moved
  or removed, but its steps are edited in #555 itself.
- To edit just some test cases, you can delete the others from `testCases`; import only looks at the ones listed.

### What import does

1. It checks the whole file first: valid JSON, no unknown properties (a typo like `"expectedResults"` is an error, not
   ignored), ids, revisions, titles and step ids. Every problem is listed with its place in the file (exit code 2).
2. It compares each test case with the revision in `rev`. Only a title, field or step list that differs is a change;
   everything else, and every test case without changes, is left alone, so importing an unedited export writes
   nothing and a new export is identical.
3. It prints each change — `title: "Old" -> "New"`, `step 2: expected result changed`, `step 4: added`,
   `removed (was step 3): …`. With `--dry-run` that is all (`--json` prints it as JSON).
4. If a test case you edited was changed in Azure DevOps since the export (its revision is newer), that is a
   conflict: nothing at all is imported (exit code 6). Export again and redo those edits, or remove those test cases
   from the file. Test cases you did not edit may change on the server meanwhile; they are not a conflict.
5. It updates each changed test case with only the changed fields. The server double-checks the revision, so a
   change made in the meantime is refused, not overwritten. If one test case is rejected (say an invalid state), the
   others are still updated and `ado` exits with 1.

Export again before the next round of edits, so the file has the new revisions.

### Example: export, edit, import

```bash
ado testplan export 12 --output plan.json

# Bulk edit with jq: set every test case to Ready and add an expected result to every step that has none
jq '.testCases[] |= (.fields["System.State"] = "Ready"
      | .steps[] |= (if .sharedStepsId == null and .expectedResult == "" then .expectedResult = "No error is shown" else . end))' \
  plan.json > edited.json

# Single edit by hand: in an editor, change test case 101's title and its second step, add a step at the end:
#   "title": "Login and logout work",
#   { "id": 3, "action": "Log in as <B>jane</B>", "expectedResult": "Dashboard opens" },
#   { "action": "Log out", "expectedResult": "Login page is shown" }

ado testplan import edited.json --dry-run
# 1 test case to update, 0 unchanged.
# #101 (rev 5) Login and logout work
#   title: "Login works" -> "Login and logout work"
#   System.State: "Design" -> "Ready"
#   step 1: expected result changed
#   step 2: expected result changed
#   step 4: added
# Dry run: nothing was changed.

ado testplan import edited.json --yes
ado testplan export 12 --output plan.json        # the new revisions, ready for the next edit
```

The test plan commands use the legacy `test` REST area with **API version 5.0** on Azure DevOps Server
2020 (Dev18.M170), even when `ADO_API_VERSION` or the configured global `apiVersion` is `6.0`.
Other API areas (Git, Work Items and Builds) continue to use the globally configured version. This endpoint-specific
version is intentional: Dev18.M170.8 can return HTTP 404 for `test/plans` with version 6.0, even though version 5.0
works. No automatic fallback or retry occurs. The token needs the *Test management* read scope besides *Work items
read & write*.

## Builds

```bash
ado build list                # builds of the project, most recently queued first; --limit n for the latest n
ado build show 815
ado build run <definition-id> [--branch b]   # default: the definition's default branch
```

## JSON

`--json` makes data commands print JSON on stdout. Messages, prompts and errors go to stderr, so stdout is always
either JSON or empty (`auth` commands print plain text only). `ado pr context <id>` and `ado commit context <commit>`
always print JSON. A response from the server that is not the expected JSON is reported as an error (exit code 1).

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

### Versions and releases

Versions are bumped automatically. Every merge to `main` becomes a release: the `release` workflow builds and tests
the commit, tags it `vX.Y.Z` and publishes a GitHub release with notes generated from the merged pull requests.
Labels on the pull request choose the bump:

| Label | Bump | Example |
|---|---|---|
| (none) | patch | 0.4.2 → 0.4.3 |
| `minor` | minor | 0.4.2 → 0.5.0 |
| `major` | major | 0.4.2 → 1.0.0 |
| `no-release` | no new version | |

The first release is `v0.1.0`. The workflow creates the labels the first time it runs. There is no version number to
edit by hand: the git tag is the only place it lives.

Builds read the version from the latest tag, so `ado --version` always says what you are running:

| Build | `ado --version` |
|---|---|
| release, or a build of the tagged commit | `ado 0.4.2` |
| 3 commits after `v0.4.2` | `ado 0.4.2-dev.3` |
| without git or tags (e.g. a source archive) | `ado 0.0.0-dev` |

To build a specific version yourself, pass it: `dotnet publish src/AdoCli -c Release -p:Version=1.2.3 ...`.

### CLI progress output

Interactive commands report start and completion on **stderr**; JSON and text results remain exclusively on **stdout**. Long-running `testplan trace-export` additionally shows bounded plan/suite and requirement progress while querying ADO. Progress is suppressed when stderr is redirected (including CI and pipelines), and short commands do not invent percentages before a total is known. A redirected `--output` JSON snapshot remains clean. Future long-running handlers can reuse `CliProgress` for real item counts.
