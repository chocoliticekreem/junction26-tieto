# SQL sandbox adapter (second adapter, SPEC polish item 6)

Proves the receipt model is not .NET-specific: a SQL migration change runs through a different
sandbox (a throwaway Postgres in Docker) and lands in the same run folder, the same verdict composer
and the same dashboard. Deterministic: no LLM call.

## Run it

```
examples/make_sql_repo.sh                       # bare repo at work/sql-repo.git (reproducible shas)
cd pipeline
uv run receipts review-sql --repo ../work/sql-repo.git --change sql-good      --req REQ-SQL-1
uv run receipts review-sql --repo ../work/sql-repo.git --change sql-bad-down  --req REQ-SQL-1
uv run receipts review-sql --repo ../work/sql-repo.git --change sql-data-loss --req REQ-SQL-2
```

Sources: `examples/sql-repo/base/` (main), `examples/sql-repo/changes/<branch>/` (one commit each),
requirements in `examples/sql-repo/requirements/`. Code: `pipeline/receipts/adapters/sql.py`,
registry `adapters/__init__.py`, orchestration `review_sql.py`.

## What it checks

One container per review: `postgres:16-alpine`, `--rm`, name `receipts-sql-<random>`, published on
a random `127.0.0.1` port, ready when `pg_isready -h 127.0.0.1` succeeds inside the container (the
init-time server listens on the socket only, so this waits for the real one). Removed in `finally`.

1. Apply every `migrations/*.up.sql` from the base commit, then snapshot.
2. Apply the head's new up migrations, snapshot.
3. Run the head's `queries/*.sql` (must succeed).
4. Apply the new down migrations in reverse, snapshot.

Snapshot = schema (columns with type, nullability and default; constraints; index definitions)
+ row count per table + per-column checksum (md5 of non-null values sorted as text, plus count).

| Evidence | Kind | Fails when |
|---|---|---|
| `sql-reversible` | migration-up-down | up fails, a down file is missing, down fails, or schema after down != schema at base (diff attached) |
| `sql-rows` | row-preservation | any table has fewer rows after up than at base (row count table attached) |
| `sql-values` | row-preservation | a rename the requirement asks for (`rename \`a\` to \`b\``) does not carry the values: checksum of `a` at base != checksum of `b` after up |
| `sql-queries` | existing-tests | a repo query errors on the migrated schema |

Every failed item becomes a verified `compatibility` finding, severity high, citing the evidence id,
so the composer blocks. Every docker/psql call is a CommandReceipt with a log; the SQL piped to psql
is saved under `inputs/` in the run folder and named in the log header.

## Results (2026-11-14, Apple Silicon, image already pulled)

| Change | Verdict | Failed evidence | Wall |
|---|---|---|---|
| sql-good | needs-sign-off (risk 7, db-migrations trust 1, no lens ran) | none | 2.1 s |
| sql-bad-down | blocked | `sql-reversible`: `+column orders.discount_cents` left after down | 3.0 s |
| sql-data-loss | blocked | `sql-values`: `orders.amount -> orders.amount_cents` checksum differs; schema round-trips fine | 2.7 s |

Container start 0.2 s, ready wait 1.1-1.6 s, each psql call 0.07 s, teardown 0.2 s. A first run
without the image adds the pull (~100 MB). `docker ps -a` showed no `receipts-sql-*` containers
after the runs and after the Docker integration test.

## What it cannot check (also in each receipt's not_checked)

- Duration and lock time on production-size tables (5 seed rows).
- Locking under concurrent writes (idle database, one connection).
- Values in columns the requirement does not name; the rename check needs the requirement to say
  `rename \`old\` to \`new\``.
- Whether down restores data (schema only). Column order is ignored by design.
- Other Postgres versions or engines; application code beyond `queries/*.sql`.
- Edits to already-applied migrations are reported as not checked, not judged.
- The six AI lenses: not run in this deterministic demo.

## Adding a third adapter

An adapter is a module with `run(AdapterInput) -> AdapterResult` plus one entry in
`adapters.ADAPTERS` and a rule in `adapters.change_type()`. The verdict composer needs nothing new:
it reads CommandReceipts, Evidence and verified Findings; `VerdictInputs.adapter` only stops it from
listing .NET gaps for non-.NET changes.

- Web UI (Playwright): `change_type` matches `web/**`; `run` builds base and head, serves each,
  drives the same flows, records screenshots and console errors as CommandReceipts. Evidence kinds
  `visual-diff` (pixel diff over a threshold) and `console-errors` (new errors on head).
- Mobile (Maestro): `change_type` matches `app/**` or `*.swift`/`*.kt`; `run` builds for a
  simulator, runs `maestro test flows/*.yaml` on base and head. Evidence `flow-result` with
  per-step pass/fail and screenshots.

Each new kind is added to `models.Evidence.kind` and `dashboard/lib/types.ts`; its details render
through `evidence-card.tsx` (record tables, diff blocks, file lists are already generic).
