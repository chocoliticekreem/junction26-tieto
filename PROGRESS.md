# Progress log

**Start time:** 2026-11-14 05:32 EET (Helsinki). Minimum run until 08:32 EET, then until the definition of done is verified, then polish backlog.

## Done
- 05:33 Repo created: github.com/chocoliticekreem/junction26-tieto (private), initial commit on `main`.

## In progress
- Skeleton: pipeline package, shared pydantic schemas, CLI, MOCK_LLM fixture layer, sample run JSON for the dashboard.

## Next (work tracks)
| Track | Owner model | Scope |
|---|---|---|
| A core | opus | setup, intake, RuleRouter, .NET sandbox, review orchestration on 2283 |
| B dashboard | fable | Next.js pages + API routes + Playwright against sample runs |
| C routers + lenses | opus | LLM client, LLMRouter, JevRouter, six lenses |
| D receipts | opus | requirement tests fail/pass, mutation, compat harness, regeneration |
| E content | sonnet | requirements/, context bootstrap, deliverable scaffolds |
| F bench | opus | plants, manifest, bench run, metrics T1-T7 |

## Decisions
- Docker daemon is not running; Docker is only needed for the SQL adapter (polish item 6), so no impact on the definition of done.

## Time elapsed
- 05:33 — 0 h 01 m
