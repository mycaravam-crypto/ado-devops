# Test plans

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

## Test closure report (MVP)

```bash
ado testplan report 12 --format html --output closure.html
ado testplan report 12 --area "Product\\ALPHA" --format json --output closure.json
```

A read-only, point-based closure snapshot with generated UTC timestamp, total test points, Passed/Failed/Blocked/Not Run, execution rate, pass rate and suite counts. HTML reuses the standalone test-plan layout (including the progress section) and excludes local test parameters by default; the report still includes test-plan case descriptions and shared steps, so review it before sharing. JSON uses the `ado.test-closure/v1` schema. Results are current test-point outcomes, **not** an end-of-period snapshot. This first version does not evaluate defects, risk acceptance or release approval. No work items are changed.

## Test coverage by Area Path

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

## Test progress (Azure DevOps Server)

`ado testplan progress` reads **test points** (suite + test case + configuration) via the legacy Test REST API 5.0, not the private `/_testManagement/analytics/progressreport` web page. Each point contributes one current outcome, and a test case with multiple configurations contributes multiple points. Filtering with `--area` includes descendants but not similarly prefixed Area Paths.

```bash
ado testplan progress 12 --area "Product\\ALPHA"
ado testplan progress 12 --area "Product\\ALPHA" --days 30 --format json
ado testplan progress 12 --area "Product\\ALPHA" --format html --output progress.html
ado testplan export 12 --format html --with-results --area "Product\\ALPHA" --output plan.html
```

Current point outcomes show Passed, Failed, Blocked, and Not Run. Executed = Passed + Failed; pass rate = Passed / executed (0% for an empty denominator). All other states count as Not Run, so the results are **not guaranteed to equal** Azure DevOps Analytics KPIs. The 14/30-day history shows daily test-run *result events*, not historical test-point snapshots; a single test case can appear multiple times in the history. Filtering affects the point report and the history, not the underlying test case definitions in HTML export. A server that does not expose the legacy point API will report an API error; it will not silently substitute case-level metrics.

## Report quality and validation

For a shareable HTML export, use `--exclude-parameters` to omit local parameter definitions and parameter values from **HTML only**. JSON remains an editable round-trip and still contains these fields; protect JSON files accordingly. Shared-step contents may also contain sensitive test data.

```bash
ado testplan export 12 --format html --with-results --area "Product\\ALPHA" --exclude-parameters --output plan.html
```

Reports include a UTC generation timestamp, source and (when supplied) an Area Path. The progress report separates **current test-point status** (suite × case × configuration) from **historical test-run events** for the selected 14/30-day period. Trends count events, not past coverage snapshots. `Blocked` and missing outcomes do not count as executed in this tool, so percentages may differ from the Azure DevOps Analytics progress report.

**Manual server comparison checklist:** choose a plan with a test case in multiple suites/configurations; compare total points and Passed/Failed/Blocked/Not Run to the Azure DevOps result report for the same scope and time. Repeat for a plan with no runs and one with recent failures. Record any differences in state mapping or filtering instead of silently adjusting percentages. Automated parity is not asserted without an accessible server fixture.

## The file

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

## What import does

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

## Example: export, edit, import

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

## Traceability export for docwizz

`trace-export` writes a read-only snapshot for offline docwizz import: requirement ids, explicit links to in-plan test
case ids, suite assignments and scope metadata. It leaves out steps, parameters and execution evidence by design.

```bash
ado testplan trace-export --area "Product\\ALPHA" --output alpha-tests.json      # every relevant plan: ado.test-traceability/v2
ado testplan trace-export 12 --area "Product\\ALPHA" --output alpha-tests.json   # one plan: ado.test-traceability/v1
```

v2 keeps the exact `planId`, `suiteId` and `testCaseId` memberships while listing each test case once.
