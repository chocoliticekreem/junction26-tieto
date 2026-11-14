# Receipts dashboard

Next.js 15 (App Router), TypeScript, Tailwind v4, shadcn/ui. Reads the pipeline's JSON folders; writes
`decisions/` and `context/` through route handlers. No database.

```
npm install
npm run dev            # http://localhost:3000, reads ../runs ../context ../decisions ../metrics ../mock-repo
npm run lint && npm run typecheck && npm run build
npm run test:e2e       # next build + Playwright against a temp copy of e2e/fixtures (+ a bare git repo)
```

## Folders (env vars, defaults relative to `dashboard/`)

| Env | Default | Read | Written |
|---|---|---|---|
| `RECEIPTS_RUNS` | `../runs` | `<change-id>/latest`, `<change-id>/<run-ts>/*.json`, `logs/*.log` | never |
| `RECEIPTS_CONTEXT` | `../context` | `REVIEW_CONTEXT.md`, `ledger.jsonl`, `status.json` | `ledger.jsonl` (append), `status.json` |
| `RECEIPTS_DECISIONS` | `../decisions` | `<change-id>.json` | `<change-id>.json` (create once, never overwrite) |
| `RECEIPTS_METRICS` | `../metrics` | `summary.json`, `T1.json`…`T7.json`, `trust.json`, `trust-history.json` | never |
| `RECEIPTS_MOCK_REPO` | `../mock-repo` | bare git repo | `git merge --no-ff` into `main` via a temporary worktree |

Change ids are opaque. Ids with `/` (e.g. `mock/2283/logic`) map to nested folders under `runs/`; the loader walks
`runs/` recursively and treats every folder containing a `latest` file as a change. Only ids starting `ju26-issue-`
appear in the queue (`/`), grouped by what the user must do (needs your decision, blocked, done). Everything else is
shown on `/track-record` ("How we know it works"): ids starting `sql-` under "database migrations", the rest under
"practice runs" (the UI never says "bench"). Ids ending in `__A` / `__B` are the per-branch runs of an A/B decision
and are hidden from the lists; `/decision/<id>` reads them.

## Journey

Three nav items, in order: **Your queue** (`/`) -> **Product context** (`/context`) -> **How we know it works**
(`/track-record`). A change page is a single-column story with a sticky step strip (1 What changed · 2 Impact on other
features · 3 What we proved · 4 What we didn't check · 5 Your decision) and the trust receipt beside it (after step 3 on
mobile). Everything an
engineer might want (receipts, mutants, routing, requirement text, hunches, pipeline reason, copy-as-PR-comment) sits
in one "Technical details" drawer at the bottom, closed by default. Pending questions appear on the queue card
("1 question for you") and inline in step 4 of the change page; `/questions` still works for old links.

## Assumptions about run data (the pipeline must honour these)

- Shapes are exactly `pipeline/receipts/models.py`, mirrored in `lib/types.ts`. Only `receipt.json` is required
  per run; every other file is optional and missing files render as empty.
- `runs/<change-id>/latest` holds the run-ts folder name; a run without `receipt.json` is ignored.
- Receipts count in the inbox = `evidence.json` results: `pass` is green, `fail`/`error` red, `partial`/`skipped` grey.
- A finding is shown as verified only if it is in `receipt.findings`; its `evidence_ids` must match `Evidence.id`
  values in `evidence.json`. `receipt.hunches` are rendered collapsed and labelled "unverified hunch, no receipt".
- `Evidence.command_ids` must match `CommandReceipt.id`; `CommandReceipt.log_path` is relative to the run dir and
  is served by `GET /api/log?change=&run=&path=&lines=` (tail, path-traversal refused).
- `Evidence.details` keys the dashboard renders specially (everything else falls back to key/value or JSON):
  - `tests: [{name, base, head}]` for fail-before/pass-after tables
  - `failing_tests`, `killing_tests`, `generated_files`, `files: string[]`
  - `mutant_ids: string[]` joined against `mutants.json`
  - `diff`, `byte_diff`, `api_diff: string` rendered as monospace blocks
- `impact.json` (optional; `ImpactReport` in `lib/types.ts`) drives step 2 "Impact on other features", the `plain-impact`
  sentence, the queue-card clause and the receipt line `Impact on other features`. Missing file = "This run predates the
  impact check" and a `—` receipt line. `members[].kind` `modified`/`deleted` rows form the blast radius (sorted by
  `callers_outside_change`, dots = callers with `in_change: false`, `tested` true/false/null = filled/hollow/grey);
  `new` members collapse into one "New: …" line. `characterization[].member` must equal a `members[].name` and its
  `evidence_id` an `Evidence.id`. Receipt line: FAIL when any characterization is `changed` (or `summary.behaviour_changes
  > 0`), PASS when nothing outside the change is touched or every pinned behaviour held with full coverage, PARTIAL
  otherwise (untested callers, `coverage_available: false`, an `invalid` pin, or a modified member with callers but no pin).
  `method` containing `name-based` renders the footnote "Call sites found by name, not by full type analysis".
- Evidence kinds `behaviour-preserved` / `behaviour-changed`: `details.member` names the function, `details.tests`
  the pinned tests (rendered as a base/head table), `details.callers_outside_change` feeds the plain-words "wrong"
  sentence. `behaviour-preserved` with `result: pass` is a Proven line ("Old behaviour of X still holds · N/N pinned tests
  pass"). A `behaviour-changed` finding whose claim starts `behaviour of <member> changed for <N> callers outside the
  change` renders as "Changes how <member> behaves for N other places that rely on it."
- Pending questions = `questions.json` entries (from all latest runs) whose `id` has no `question_id` in
  `context/ledger.jsonl`. `Question.status` is not consulted.
- `receipt.area_trust` (area -> 0..3) drives the inbox "Trust" column (min over areas). Absent = "—".
- A/B: `receipt.verdict == "decision-A-B"` with `receipt.interpretations[{label, summary, branch}]`. The merge
  route merges `interpretations[label].branch` (fallback `review/<change-id>/<label>`) into `main`. The decision's
  `evidence_ids` come from `runs/<change-id>__<label>` if that run exists, else from the parent run.
- `context/status.json`: `{status: "draft"|"confirmed", confirmed_by?, ts?}`. Missing file = draft.
- Every write needs the named human (cookie `receipts_human`, set via the header). Without it the routes return 400.
  Decisions are written with `O_EXCL`; a second sign-off or merge for the same change returns 409.

## Metrics shape (`metrics/*.json`)

`T1.json` … `T7.json` share one shape:

```json
{ "columns": ["lens", "planted_in_scope", "caught", "missed", "false_alarms", "recall", "precision"],
  "rows": [["requirement-tests", 8, 7, 1, 0, 0.875, 1.0]],
  "note": "optional footnote shown under the table" }
```

Rows are arrays aligned to `columns`; cells are `string | number | null`. Columns whose name matches
`recall|precision|rate|share` and whose value is in 0..1 render as percentages; `usd|cost` columns render as
dollars. Numeric columns right-align automatically. A `note` shows its first sentence; the rest sits behind a
"Definitions" disclosure. String cells over 48 characters collapse behind a disclosure (split on `;` or `,`).

Column names the page keys on (case-insensitive):

- Headline tiles: `summary.json` keys `cold.{planted,caught,recall,escapes,false_alarm_cases,control_bait_cases,
  questions_per_change,lens_cost_usd,router_cost_usd,cases}`, `warm.{questions_per_change,cases}`,
  `ju26-issue-2297_routed_away_from_humans.{generated_files,files_total,share}`, `excluded_incomplete_runs`.
  A tile is skipped when its keys are missing.
- Recall by lens (bar, with caught/in-scope): `T1` columns `lens`, `recall`, `caught`, `planted_in_scope`, optional `kind`.
  A null `recall` renders as "—" (nothing in scope).
- Questions per change over the sequence (line, cold vs warm): `T5` columns `seq` (number), `questions` (number)
  and `mode` (`cold` | `warm`; one line per distinct mode, `warm` dashed). The cold-vs-warm summary sums
  `questions`, `bait_false_alarms`, `ledger_citations`, `repeat_questions` per mode (missing columns show "—").
- Human load: `T4` column `change_id`; rows starting `ju26-issue-` render as cards (`files_to_human`, `files_total`,
  `generated`, `generated_verified_by_regeneration`, `deep`, `light`, `minutes_saved`, `minutes_read_all`), the rest
  behind a "bench rows" disclosure.
- Receipts headline: `T6` row whose `lens` is `all`, columns `findings_shown`, `with_executed_receipt`,
  `hunches_withheld`, `ledger_citations`.
- `T7` renders with wrapped text cells; `T2`, `T3` as plain tables.
- Area trust over the bench: `trust-history.json` = `[{seq, change_id, area, level, event}]`. Areas whose level
  changed get a step chart each (events matching `reset|escape` are marked); the rest collapse into one line.

Suggested columns, as used by the fixtures:

- T1: `lens, planted_in_scope, caught, missed, false_alarms, recall, precision`
- T3: `router, hunks_scored, tokens, cost_usd, seconds, planted_routed_low, end_to_end_recall`
- T5: `seq, change_id, mode, questions, bait_false_alarms`

`trust.json`: `{ "<area>": 0..3 }`.

## Routes

| Route | Reads | Writes |
|---|---|---|
| `/` (Your queue) | latest run per `ju26-issue-*` change, ledger (pending counts), decisions | |
| `/change/[id]` | run bundle, decision, ledger (pending questions inline) | `POST /api/decisions/sign-off {change_id}`; `POST /api/questions/answer`; “Copy as PR comment” (in the technical drawer) copies `GET /api/receipt-markdown?change=<id>` (GitHub markdown of the trust receipt, `text/markdown`) |
| `/decision/[id]` | parent run + `__A`/`__B` runs, decision | `POST /api/decisions/merge {change_id, choice}` |
| `/questions` (not in the nav) | all latest runs' questions, ledger | `POST /api/questions/answer {question_id, change_id, answer}` |
| `/context` (Product context) | `REVIEW_CONTEXT.md`, `status.json`, ledger | `POST /api/context/confirm` |
| `/track-record` (How we know it works) | `metrics/summary.json`, `T1`–`T7`, `trust.json`, `trust-history.json`, all non-sample runs | |
| header | cookie | `POST /api/human {name}` |

## Tests

`e2e/fixtures/` holds runs for `ju26-issue-2283`, `ju26-issue-2297` (+ `__A`, `__B`), `mock/2283/{logic,control,bait}`,
a context file, a ledger with one answer, and metrics. They validate against `models.py`. `e2e/global-setup.ts`
copies them to `e2e/.tmp/` and creates a bare repo with `main`, `review/ju26-issue-2297/A` and `.../B`, so the
Merge A test produces a real `--no-ff` commit authored by the named human. `e2e/screenshots.spec.ts` writes
desktop (1440) and mobile (390) screenshots of every page to `e2e/screenshots/`. `e2e/receipt-markdown.spec.ts` unit-tests
the PR-comment generator without a server; `e2e/humanize.spec.ts` unit-tests the plain-language mapper; `e2e/impact-summary.spec.ts`
unit-tests the impact sentences (only-new, preserved, changed, untested callers, coverage unavailable). `e2e/screenshots/impact-*.png`
are element shots of step 2 on the two fixture variants. `e2e/screenshots/real-*.png`
(and `real-*-dark.png`) are taken by hand against the repo's real `runs/` and `metrics/` (`npx next dev -p 3171` with the
write folders pointed at scratch copies, then a Playwright script at 1440 and 390, light and dark).

## Plain language

Every machine string the pipeline writes (verdict reasons such as `max routed risk 7 >= 5; not every receipt is green (gentest-2, mutation)`,
evidence titles, sandbox step names) is rendered through `lib/humanize.ts` before a human sees it. The raw text stays one hover or
click away (a `title` tooltip, or the "pipeline reason" disclosure). Shapes the mapper does not know fall back to the raw text, so a
new pipeline string is never hidden, only unpolished. Add a case to `humanizeVerdict` / `humanizeTitle` and a test in
`e2e/humanize.spec.ts` when the pipeline grows a new shape.
