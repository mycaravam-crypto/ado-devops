# Repositories, pull requests and commits

## Repositories

```bash
ado repo list                 # repositories of the current project (all, if unknown)
ado repo show [<repo>]
ado repo clone <repo> [dir]
ado repo status               # server/project/repo of the current clone (no network call)
```

`repo status` warns when the clone's server is not the one you are logged in to. `repo clone` passes the TLS and proxy
settings on to git (see [Configuration](configuration.md#settings)).

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
