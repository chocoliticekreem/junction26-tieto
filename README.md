# PullReceipts: AI review you can audit

Junction 2026 entry for Tieto's "Earned Trust in AI Code" challenge.

**Every finding has a receipt, every gap is declared, every area has a track record, and a named human signs off in one click.**

An AI reviewer that only comments asks the human to trust its confidence. PullReceipts shows a claim as a finding only when an executed artifact backs it: a test written from the requirement that fails before the change and passes after, a mutant the change's tests fail to kill, a byte diff, or a failed regeneration. Everything else is listed apart as an unverified hunch. Each review declares what was not checked and why. Genuinely ambiguous requirements are refused, not guessed: the reviewer builds both readings as branches with receipts, and a named human picks. Nothing merges without a named human.

Also in the box:
- **Blast radius.** Existing code the change modifies, its callers outside the change, which of those callers any test executes, and tests written from the base code that pin the old behaviour (they must pass before the change; a failure after it blocks). A modified member with outside callers is never auto-ready. See `notes/blast-radius.md`.
- **Two model providers.** Anthropic (default) or Verda's EU-hosted Inference API with the open-weight Kimi K3.1 Pro (`LLM_PROVIDER=verda`), each behind a hard spend guard. Results side by side in `docs/verda.md`.
- **A second sandbox.** SQL migrations reviewed in a throwaway Postgres: reversibility, row preservation and value checksums as receipts, no model call. See `notes/sql-adapter.md`.

Read first: [`deliverables/concept-card.md`](deliverables/concept-card.md) (2 minutes). Then the [decision log](deliverables/decision-log.md), the [lessons log](deliverables/lessons-log.md) and the [demo script](deliverables/demo-script.md).

## What is in the repo

| Path | What |
|---|---|
| `pipeline/` | Python 3.12 review pipeline (`uv run receipts ...`): intake, routing (rule floors, blast radius, Jev, Claude Haiku), .NET and SQL sandboxes, six lenses plus the regression lens, receipt checks, verdict, bench, metrics; Anthropic or Verda models |
| `dashboard/` | Next.js dashboard: queue, trust receipt per change (including impact on other features), A/B decision with a real merge, questions, context, track record |
| `tools/compat-harness/` | .NET 8 console app that serializes 124 fixed values with base and head builds and diffs the bytes and the public API |
| `tools/impact/` | Roslyn syntax tool: modified, new and deleted members between base and head, and their call sites |
| `examples/` | SQL migration sample repo for the Postgres sandbox (`make_sql_repo.sh`) |
| `requirements/` | REQ-2283, 2234, 2129, 2297, from the upstream issues |
| `runs/` | Real review output for the four Tieto changes (receipts, evidence, logs) |
| `bench/` | 36 bench cases: 4 changes x 9 variants with planted defects, as fixed patches plus a label manifest the reviewer never sees |
| `metrics/` | Measurement tables T1-T7, trust levels per area; `metrics/verda/` and `provider-comparison.json` for the Verda runs |
| `context/` | Reviewer context (`REVIEW_CONTEXT.md`) and the question ledger |
| `fixtures/llm/` | Recorded model and router responses for offline replay |

## Run it

Prerequisites: `uv`, Node 20+, the .NET 8 SDK (the code calls `~/.dotnet/dotnet`; set `DOTNET` to override).

```bash
# Dashboard on the committed review output (no keys, no network needed)
cd dashboard && npm install && npm run dev        # http://localhost:3000

# Pipeline
cd pipeline && uv sync
uv run receipts setup                             # bare clone of the Tieto sample repo into mock-repo/, base build
MOCK_LLM=1 uv run receipts review --change ju26-issue-2283 --req REQ-2283   # replays recorded model output
uv run receipts review --change ju26-issue-2283 --req REQ-2283              # live, needs ANTHROPIC_API_KEY (JEV_API_KEY optional)
uv run receipts bench generate && uv run receipts bench run && uv run receipts bench run --warm
uv run receipts metrics
LLM_PROVIDER=verda uv run receipts review --change ju26-issue-2283 --req REQ-2283   # same review on Verda (model env vars: docs/verda.md, "Reproduce")
uv run receipts metrics --provider verda                                          # metrics/verda/ + provider comparison

# SQL sandbox (needs Docker)
../examples/make_sql_repo.sh
uv run receipts review-sql --repo ../work/sql-repo.git --change sql-good --req REQ-SQL-1
```

Keys go in `.env.local` at the repo root (never committed): `ANTHROPIC_API_KEY`, optional `JEV_API_KEY`, and `VERDA_API_KEY` for the Verda provider. Without keys or recorded fixtures, every model-based step is listed under "not checked" in the receipt; nothing is invented.

## Tests

```bash
cd pipeline && uv run pytest
cd dashboard && npm run lint && npx tsc --noEmit && npm run test:e2e
```

## Data

Only the public Tieto sample repository (a copy of MessagePack-CSharp with AI-written branches) and synthetic bench variants derived from it.

## License

MIT, see [`LICENSE`](LICENSE).
