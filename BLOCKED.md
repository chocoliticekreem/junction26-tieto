# Blocked items for Anson

## 1. Anthropic API usage limit reached (blocks every new model call) — Sat 14 Nov ~09:10
Every Anthropic call now returns HTTP 400: "You have reached your specified API usage limits. You will regain access on 2026-12-01 at 00:00 UTC." *Specified* means a spend limit set on the account, not an outage. Spend in this run: the full $100 limit (three bench passes about $55, real reviews recorded twice about $17, A/B variants about $8, prompt iteration and router comparison the rest).

**Unblock (2 minutes):** console.anthropic.com, then Settings, then Limits. Raise the monthly spend limit by **about $20** (bench runs now go through the Message Batches API at half price; PR #29). No code change; the pipeline reads the key from the local env file as before.

Waiting on it, in order (estimates from `--dry-run`, no API calls; see `notes/cost.md`):
1. **Bench re-run on the final pipeline.** The published bench numbers come from the pipeline as of PR #11; later fixes changed the lens prompts, so recorded fixtures do not replay.
   - Cold, all 36 cases: `cd pipeline && uv run receipts bench run --batch --max-usd 12` (estimate $9.93).
   - Warm, the 2283 and 2234 subsets: `uv run receipts bench run --warm --batch --only mock-2283 --max-usd 4 && uv run receipts bench run --warm --batch --only mock-2234 --max-usd 4` (about $5).
   - Then `uv run receipts metrics`, and update the two date and PR strings in `dashboard/app/track-record/page.tsx`.
2. **Round 2 of 2297:** `uv run receipts review --change ju26-issue-2297-r2 --req REQ-2297` (about $2, live). A fix commit for the verified findings is on that branch in `mock-repo`, written by an agent playing the implementer (see `notes/round2.md`). If its re-review comes back `decision-A-B` with both sides unblocked, that is the real A/B merge for the demo.

What still works without it: the dashboard, every committed run, `MOCK_LLM=1` replay of the four Tieto reviews, the Jev router, and every deterministic receipt (builds, tests, mutation, byte diff, regeneration). Model steps without a key are listed under "not checked" in each receipt, never invented.

## 1b. Verda: no model is enabled for the project behind VERDA_API_KEY — Sat 14 Nov
The key works (`GET https://inference.verda.com/v1/models` returns 200), but the list is empty, and a 20-token chat call to either model returns `403 subscription_required` ("this project is not subscribed to model ... at service tier standard"). Refused requests are not billed. Enabling a model needs the Console (or a Cloud API token, which the agent does not have).

**Unblock (1 minute):** console.verda.com, **Inference > Models**, click **Enable** on **Kimi K3.1 Pro** and on **DeepSeek V4.1 Flash 1017**, in the same project as the key in `.env.local`. Check with:

```
cd pipeline && uv run receipts estimate-verda   # free, offline projection
curl -s https://inference.verda.com/v1/models -H "Authorization: Bearer $VERDA_API_KEY"   # should list both ids
```

Then run the commands in `docs/verda.md` ("Reproduce"). Every Verda call is capped by `VERDA_MAX_EUR` (default 40) in `work/verda-spend.json`.

## 2. Human-only steps (principle 4: a named human decides; the AI must not do these)
1. `cd dashboard && npm install && npm run dev`, open http://localhost:3000, click **Change** (top right) and enter your name.
2. **Context** tab: read `REVIEW_CONTEXT.md` (24 entries, status draft) and click **Confirm as <you>**. This writes `context/status.json`. Until then, ledger citations that suppress a finding are shown as hunches ("suppressed by unconfirmed context entry").
3. **A/B merge.** Today no real change is mergeable: 2297 and 2129 are ambiguous (3 of 3 readers), but both readings of each carry verified high findings, so the dashboard refuses to merge them. That refusal is a feature to show in the demo. After item 1.2 (2297 round 2), open `/decision/ju26-issue-2297-r2`; if both sides are unblocked, click **Merge A** or **Merge B**. That writes `decisions/<change>.json` with your name and runs `git merge --no-ff` into `main` of `mock-repo/` (a local bare clone, never upstream).
4. Optionally sign off 2283 or 2234 (needs sign-off) from the change page.
5. Commit the resulting `decisions/` and `context/status.json` through a PR, or ask the agent to.

