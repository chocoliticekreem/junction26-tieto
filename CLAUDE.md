Mode: hackathon

# Junction 2026: Tieto "Earned Trust in AI Code" entry

Build spec: `SPEC.md`. Startup prompt: `START.md`. Progress log: `PROGRESS.md` (the agent keeps it current).

Deadline: final submission Sun 15 Nov 2026, 10:00 Helsinki time (UTC+2). Peer review from 10:30.

## Environment already prepared
- .NET SDK 8.0.402 installed at `~/.dotnet` (call `~/.dotnet/dotnet`; it is not on PATH).
- Sample repo clone (read-only reference; make your own copy before changing anything): `/private/tmp/claude-501/-Users-sonionring-orca-projects-junction/aae424c1-cec2-4b34-862a-bf3153e7d7ee/scratchpad/tieto/repo`. Upstream: https://github.com/te-industry-tech-office/junction26-messagepack-csharp
- Base commit `32258fdb` builds; `tests/MessagePack.SourceGenerator.Tests` passes 167/167 in about 6 seconds.
- Docker is installed.

## Never relaxed, even in hackathon mode
- No secrets in git. Keys live in `.env.local` only; `.env.example` lists the names with empty values.
- Record everything on GitHub through pull requests (see `START.md`, rule 6). Repo: `chocoliticekreem/junction26-tieto`, private until Anson makes it public for submission. Never deploy without Anson saying so.
- Synthetic or cleared data only (Tieto rule). No code or data from 8x; only general lessons.
- Confirm before anything destructive.

## How to work
- Mock and fallback paths first; a missing key never blocks the build. Log what is missing in `BLOCKED.md` for Anson.
- Verify before claiming done: run the pipeline on real branches, read the output, run Playwright on the dashboard.
- Parallelise with subagents. Dashboard and visual work: model `fable`. Pipeline, sandboxes, measurement and reviews: model `opus`. Bulk or mechanical work (mock clones, fixtures, docs): model `sonnet`.
