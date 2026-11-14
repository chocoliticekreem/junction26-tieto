# Blocked items for Anson

State at Sat 14 Nov, 23:40 EET. Section 1 is resolved and kept as history. Section 2 is what only you can do.

## 1. Anthropic API usage limit reached (blocked every new model call) — Sat 14 Nov ~09:10
**Resolved 23:15:** limit raised; bench re-run on the final pipeline through the Message Batches API (cold 36 cases + warm 2283/2234, $15.12 this pass, ledger total $16.51 of the $20 cap; see PROGRESS.md). Both waiting items are done:
1. **Bench re-run on the final pipeline: done.** Cold 21/24 caught, 0 escapes; `metrics/` and `metrics/README.md` regenerated.
2. **Round 2 of 2297: done, on Verda only** (`runs/ju26-issue-2297-r2@verda/20261114T111141Z`, `docs/verda.md`). It was not re-run on Anthropic.

History: from ~09:10 every Anthropic call returned HTTP 400 ("You have reached your specified API usage limits"), a spend limit set on the account. Spend up to then: the full $100 limit (three bench passes about $55, real reviews recorded twice about $17, A/B variants about $8, prompt iteration and router comparison the rest).

## 1b. Verda: no model was enabled for the project behind VERDA_API_KEY — Sat 14 Nov
**Resolved:** Kimi K3.1 Pro enabled; DeepSeek V4.1 Flash stays closed to new projects. All Verda runs used Kimi in every role: EUR 25.00 of EUR 40 over 372 calls (`work/verda-spend.json`, `docs/verda.md`).

History: the key worked (`GET https://inference.verda.com/v1/models` returned 200 with an empty list), but chat calls returned `403 subscription_required` until a model was enabled in the Console.

## 2. Human-only steps (principle 4: a named human decides; the AI must not do these)
1. `cd dashboard && npm install && npm run dev`, open http://localhost:3000, click **Change** (top right) and enter your name.
2. **Context** tab: read `REVIEW_CONTEXT.md` (24 entries, status draft) and click **Confirm as <you>**. This writes `context/status.json`. Until then, ledger citations that suppress a finding are shown as hunches ("suppressed by unconfirmed context entry").
3. **A/B decisions: there is no clean A/B merge on Anthropic.** 2129 and 2297 are ambiguous (3 of 3 readers), but both readings of each carry verified high findings, so the dashboard refuses to merge them. Show that refusal in the demo.
4. **2297 round 2 (Verda): your call.** `/decision/ju26-issue-2297-r2@verda` is `decision-A-B`, but only side A exists. Side A (the implementer's fix, `review/ju26-issue-2297-r2@verda/A` = `7fc9ebe1`) needs sign-off: builds, 171/171 existing tests, 222/222 generated files regenerate, no high finding. Its one requirement test still fails on the generator's namespace-mirror class, which the fix deliberately left alone (`notes/round2.md`), and no lens attached a claim to it. Side B (attribute on formatters only) was never built: Kimi failed twice. This run predates the blast-radius step, so it has no impact section. What you can do:
   - **Merge A** on that page. The dashboard allows it because side A is not blocked. It writes `decisions/ju26-issue-2297-r2@verda.json` with your name and runs `git merge --no-ff` into `main` of `mock-repo/` (a local bare clone, never upstream). By doing so you accept, as residual risk, the red mirror-class test and choosing A without a built B.
   - **Or leave it undecided** and show it in the demo as the fix loop: blocked in round 1, fixed by the implementer, round 2 unblocked on one side, the choice left to a human. Sending it back for a mirror-class fix (r3) is the other honest option.
5. **Sign-offs on 2283 and 2234** (both needs sign-off), optional, from the change page. A local sign-off of 2234 by "anson" already exists in the main checkout (`decisions/ju26-issue-2234.json`, 15:08, uncommitted). It was made before the blast-radius re-run, so its evidence list lacks `characterize-1`. Keep it or delete it and sign again on the current run.
6. Commit the resulting `decisions/` and `context/status.json` through a PR, or ask the agent to.
7. Record the video (max 2:00) from `deliverables/demo-script.md`, make the repo public, fill the form from `docs/submission.md` (tick Verda Compute Track), set it to FINAL before Sun 10:00, and post the `docs/verda.md` link in Verda's Discord channel.
