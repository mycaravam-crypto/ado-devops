# Work items

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
- `workitem create` needs a project (see [Configuration](configuration.md#settings)). The type is the process template's name,
  e.g. `Bug`, `Task` or `"User Story"`. When stdin is not a terminal, `--type` and `--title` are required.
  It prints the new id; `--json` prints the whole work item.
- `workitem edit` changes only the fields given. The server checks the values: an unknown state,
  user or field fails with its message and exit code 1. `--json` prints the updated work item.

## Changing many work items at once

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

## Requirement context

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

## Search

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
