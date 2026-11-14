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

Change ids are opaque. Ids with `/` (e.g. `mock/2283/logic`) map to nested folders under `runs/`; the inbox walks
`runs/` recursively and treats every folder containing a `latest` file as a change. Ids starting `ju26-issue-` are
grouped as "Tieto sample changes", everything else as "Bench". Ids ending in `__A` / `__B` are the per-branch
runs of an A/B decision and are hidden from the inbox; `/decision/<id>` reads them.

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
| `/` | latest run per change, ledger (pending counts), decisions | |
| `/change/[id]` | run bundle, decision, ledger | `POST /api/decisions/sign-off {change_id}`; “Copy as PR comment” copies `GET /api/receipt-markdown?change=<id>` (GitHub markdown of the trust receipt, `text/markdown`) |
| `/decision/[id]` | parent run + `__A`/`__B` runs, decision | `POST /api/decisions/merge {change_id, choice}` |
| `/questions` | all latest runs' questions, ledger | `POST /api/questions/answer {question_id, change_id, answer}` |
| `/context` | `REVIEW_CONTEXT.md`, `status.json`, ledger | `POST /api/context/confirm` |
| `/track-record` | `metrics/summary.json`, `T1`–`T7`, `trust.json`, `trust-history.json` | |
| header | cookie | `POST /api/human {name}` |

## Tests

`e2e/fixtures/` holds runs for `ju26-issue-2283`, `ju26-issue-2297` (+ `__A`, `__B`), `mock/2283/{logic,control,bait}`,
a context file, a ledger with one answer, and metrics. They validate against `models.py`. `e2e/global-setup.ts`
copies them to `e2e/.tmp/` and creates a bare repo with `main`, `review/ju26-issue-2297/A` and `.../B`, so the
Merge A test produces a real `--no-ff` commit authored by the named human. `e2e/screenshots.spec.ts` writes
desktop (1440) and mobile (390) screenshots of every page to `e2e/screenshots/`. `e2e/receipt-markdown.spec.ts` unit-tests
the PR-comment generator without a server; `e2e/humanize.spec.ts` unit-tests the plain-language mapper. `e2e/screenshots/real-*.png`
(and `real-*-dark.png`) are taken by hand against the repo's real `runs/` and `metrics/` (`npm run build && npm run start -- -p 3111`,
then a Playwright script at 1440 and 390, light and dark).

## Plain language

Every machine string the pipeline writes (verdict reasons such as `max routed risk 7 >= 5; not every receipt is green (gentest-2, mutation)`,
evidence titles, sandbox step names) is rendered through `lib/humanize.ts` before a human sees it. The raw text stays one hover or
click away (a `title` tooltip, or the "pipeline reason" disclosure). Shapes the mapper does not know fall back to the raw text, so a
new pipeline string is never hidden, only unpolished. Add a case to `humanizeVerdict` / `humanizeTitle` and a test in
`e2e/humanize.spec.ts` when the pipeline grows a new shape.
