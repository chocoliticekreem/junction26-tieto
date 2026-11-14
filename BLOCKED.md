# Blocked items for Anson

## 1. Anthropic API usage limit reached (blocks every new model call) — Sat 14 Nov ~09:10
Every Anthropic call now returns HTTP 400: "You have reached your specified API usage limits. You will regain access on 2026-12-01 at 00:00 UTC." *Specified* means a spend limit set on the account, not an outage. Spend in this run: about $60 (bench cold $24.56, bench warm $17.11 before the limit hit, four real reviews about $15, A/B variants and prompt iteration the rest).

**Unblock (2 minutes):** console.anthropic.com, then Settings, then Limits. Raise the monthly spend limit by about $60. No code change; the pipeline reads the key from the local env file as before.

Waiting on it, in order:
1. **Bench re-run on the final pipeline.** The published bench numbers come from the pipeline as of PR #11. The adversarial-review fixes (PR #13) and the reader vote (PR #15) changed the lens prompts, so recorded fixtures do not replay. Run `cd pipeline && uv run receipts bench run --jobs 3 && uv run receipts bench run --warm --jobs 3 && uv run receipts metrics` (about 50 min, about $45).
2. **Round 2 of 2297:** `uv run receipts review --change ju26-issue-2297-r2 --req REQ-2297` (about $2). A fix commit for the verified findings is on that branch in `mock-repo`, written by an agent playing the implementer (see `notes/round2.md`). If its re-review comes back `decision-A-B` with both sides unblocked, that is the real A/B merge for the demo.

What still works without it: the dashboard, every committed run, `MOCK_LLM=1` replay of the four Tieto reviews, the Jev router, and every deterministic receipt (builds, tests, mutation, byte diff, regeneration). Model steps without a key are listed under "not checked" in each receipt, never invented.

## 2. Human-only steps (principle 4: a named human decides; the AI must not do these)
1. `cd dashboard && npm install && npm run dev`, open http://localhost:3000, click **Change** (top right) and enter your name.
2. **Context** tab: read `REVIEW_CONTEXT.md` (24 entries, status draft) and click **Confirm as <you>**. This writes `context/status.json`. Until then, ledger citations that suppress a finding are shown as hunches ("suppressed by unconfirmed context entry").
3. **A/B merge.** Today no real change is mergeable: 2297 and 2129 are ambiguous (3 of 3 readers), but both readings of each carry verified high findings, so the dashboard refuses to merge them. That refusal is a feature to show in the demo. After item 1.2 (2297 round 2), open `/decision/ju26-issue-2297-r2`; if both sides are unblocked, click **Merge A** or **Merge B**. That writes `decisions/<change>.json` with your name and runs `git merge --no-ff` into `main` of `mock-repo/` (a local bare clone, never upstream).
4. Optionally sign off 2283 or 2234 (needs sign-off) from the change page.
5. Commit the resulting `decisions/` and `context/status.json` through a PR, or ask the agent to.

## 3. `.env.example` could not be written by the agent
The global guard hook blocks any write to a file whose name starts with `.env`, including the example file. Create it yourself; it holds no secrets:

```
printf 'ANTHROPIC_API_KEY=\nJEV_API_KEY=\nMOCK_LLM=0\nDOTNET=~/.dotnet/dotnet\n' > .env.example
```
