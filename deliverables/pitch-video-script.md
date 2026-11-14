# PullReceipts: 2-minute pitch video script

Deck: "PullReceipts: AI review you can sign" (12 slides). The spoken words below are the deck's
speaker notes, word for word: 283 words, ending at 1:57 at about 145 words per minute. Dashboard:
https://pullreceipts.vercel.app (pages named in the Screen column).

Numbers come from `metrics/summary.json`, `notes/jev.md`, `notes/blast-radius.md`, `docs/verda.md`
and `runs/ju26-issue-*/<latest>/impact.json`. Do not round them up on camera.

## 2:00 version

| Time | Screen | Words |
|---|---|---|
| 0:00-0:05 | Deck 1, cover (Slip waving) | AI wrote the pull request, so we wrote the pull receipt. Here is how it works, ticket to signature. |
| 0:05-0:14 | Deck 2, "Don’t take the AI’s word." | An AI reviewer’s opinion is not evidence. Someone still signs the merge and needs proof they can read, and a name on it. |
| 0:14-0:17 | Deck 3, "One change, start to end." (eight-stage map, dots flowing) | Eight stages; a named human at the end. |
| 0:17-0:29 | Deck 4, product context. Optional cut: dashboard `/context` (the product context doc, status draft, area tags) | Product context: the repo’s README, analyzer docs and git history, summarised by area with sources. A named human confirms it; answered questions grow it. Deliberate conventions are cited, not flagged. |
| 0:29-0:43 | Deck 5, the funnel (237 → 222 generated → 15 read → floors → Jev → deep/light) | Depth follows a rule. On the biggest change, 222 of 237 files were generated: regenerated, never read. Rule floors cover the risky areas; Jev scores each remaining hunk one to ten. Floors never drop. |
| 0:43-0:54 | Deck 6, sandbox and the six lenses | The sandbox runs the real tests. Six independent lenses see only the ticket, its hunks and the context, never the implementer’s reasoning, and write their own tests. |
| 0:54-1:05 | Deck 7, the receipt printing line by line, EXECUTED stamp | Every finding needs a receipt: tests failing before, passing after, mutants against the full suite, byte diffs, regeneration, migrations. No receipt, no finding. Hunches stay apart. |
| 1:05-1:18 | Deck 8, blast radius. Optional cut: dashboard `/change/ju26-issue-2297`, step "Impact on other features" (7 modified, 12 callers, 1 untested: the Unity generator) | Shared code is the real fear: new feature works, old caller breaks. A modified member with outside callers gets floor eight, old behaviour is pinned by tests, untested legacy callers are named. |
| 1:18-1:27 | Deck 9, three readers vote, A and B built, a human picks | When a ticket reads two ways, three readers vote. Both readings are built and checked; a human picks A or B. |
| 1:27-1:39 | Deck 10, trust receipt 2283 (proven / not checked / signed by) with the fix loop. Optional cut: dashboard `/change/ju26-issue-2283` | The verdict lists what was proven, what was not checked, residual risk. Blocked, the implementer fixes it, round two is re-reviewed, a named human signs. The AI never merges. |
| 1:39-1:51 | Deck 11, "Measured." (21/24, 0, 94%, 0.50→0.28) | Measured: 24 planted bugs, 21 caught, zero escapes. 94 percent of the biggest change never read. Questions fell 0.50 to 0.28. Same verdicts on an EU-hosted open model. |
| 1:51-1:57 | Deck 12, "No receipt, no finding." (Slip with the signed slip) | No receipt, no finding. The decision record has a name on it. Thank you. |

Recording notes:

- The dashboard queue `/` can run under the cover if you want a live frame before 0:05; the three
  optional dashboard cuts are `/context` (deck 4), step "Impact on other features" on
  `/change/ju26-issue-2297` (deck 8) and `/change/ju26-issue-2283` (deck 10). Everything else is
  the deck.
- Stamps land about 0.7 s after a slide arrives; let EXECUTED (deck 7) and NEEDS SIGN-OFF (deck 10)
  land before the next line.
- Everything is in our words; no quotes.

## 0:30 version (peer-review table)

| Time | Screen | Words |
|---|---|---|
| 0:00-0:07 | Deck 2 | An AI reviewer’s opinion is not evidence. Someone still signs the merge and needs proof they can read, and a name on it. |
| 0:07-0:15 | Deck 7 | No receipt, no finding: every finding is tied to a test, a mutant or a byte diff that ran, and the verdict lists what was not checked. |
| 0:15-0:23 | Deck 8 | Modified code with outside callers is high risk by rule. Old behaviour is pinned by tests from the old code, and untested legacy callers are named. |
| 0:23-0:30 | Deck 12 | 21 of 24 planted bugs, zero escapes. The AI never merges; a named human signs the decision record. |
