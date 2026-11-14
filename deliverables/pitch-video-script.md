# Receipts: 2-minute pitch video script

Curated to what a Tieto engineer told us in the live review, in his order of concern. Deck: the
"Receipts: AI review you can sign" slides (10 slides). Dashboard: `http://localhost:3000` locally, or
the Vercel URL once live. 287 spoken words; at a calm 145 words per minute that is 1:58.

Numbers come from `metrics/summary.json`, `notes/blast-radius.md`, `docs/verda.md` and
`runs/ju26-issue-*/<latest>/impact.json`. Do not round them up on camera.

## 2:00 version

| Time | Screen | Words |
|---|---|---|
| 0:00-0:08 | Deck 1, cover (Slip beside the signed receipt) | This is Receipts: AI review you can sign. Built around what a Tieto engineer asked us in a live review. |
| 0:08-0:21 | Deck 2, "AI writes the fix. Who signs for it?" (strike through "The AI said it was fine") | Tieto is a consultancy. If an AI writes a fix and an AI says it's fine, who is liable? "The AI said it was fine" is an unowned merge. |
| 0:21-0:32 | Deck 3, "No receipt, no finding." | So the concept: no receipt, no finding. The reviewer may only report what actually ran, a test, a mutant, a byte diff, linked to the ticket. |
| 0:32-0:45 | Deck 4, "All green: safe, or are the tests false?" (receipt with the NOT CHECKED section) | He asked: all green, is it safe, or are the tests false? Green means proven for what was checked. The receipt lists what was not. Nothing is safe by silence. |
| 0:45-0:59 | Dashboard `/change/ju26-issue-2297`, step 2 "Impact on other features" (7 modified functions, 12 callers, 1 untested). Cut to deck 5 for the caller diagram if the dashboard is not live. | Even all green, a function used in many places may do the new thing right and break the old thing. Legacy code has no full coverage, and no full regression per change. |
| 0:59-1:16 | Deck 6, "It has to be a concrete rule." (blast radius rule receipt; Slip pinning) | So it is a concrete rule. Modified code with outside callers: high risk. Deleted: higher. New code: no floor. Tests written from the old code pin its behaviour, and every untested caller is named. The router cannot lower the floor. |
| 1:16-1:30 | Dashboard `/decision/ju26-issue-2297` (readings A and B, both blocked). Deck 7 has the same screenshot with the Blocked stamp. | It finds real problems: 2297 is blocked by a failing test. "It finds the problem, that's excellent." And when a ticket reads two ways, it builds both; a human picks A or B. |
| 1:30-1:41 | Deck 8, the fix loop (round 1 blocked, implementer fixes, round 2 re-reviewed, a named human signs) | Then they go back and fix. Blocked, the implementer fixes the proven defect, round two is re-reviewed, on Verda. Side A needs sign-off. A named human signs. |
| 1:41-1:52 | Deck 9, "Proof it works" (21/24, 0, 94%, 0.50 to 0.28) | On the bench: 21 of 24 planted bugs caught, zero escapes. 94 percent of the biggest change verified unread. Same verdicts on an EU-hosted open model, half the cost. |
| 1:52-2:00 | Deck 10, "The decision record has a name on it" (Signed by a human stamp) | Liability made explicit: the decision record has a name on it. The AI never merges. No receipt, no finding. Thank you. |

Recording notes:

- Dashboard moments: open `/` (the queue) under the cover if you want a live frame before 0:08; the
  two scripted dashboard cuts are step 2 Impact on `/change/ju26-issue-2297` and
  `/decision/ju26-issue-2297`. Everything else is the deck.
- Deck stamps land about 0.65 s after the slide arrives; let them land before the next line.
- Quote the engineer only where the script does; everything else is in our words.

## 0:30 version (peer-review table)

| Time | Screen | Words |
|---|---|---|
| 0:00-0:07 | Deck 2 | Tieto is a consultancy. "The AI said it was fine" is an unowned merge. |
| 0:07-0:14 | Deck 3 | So: no receipt, no finding. Every finding is a test that ran, linked to the ticket, and the receipt lists what was not checked. |
| 0:14-0:23 | Deck 6 | Modified code with outside callers is high risk, deterministically. We pin the old behaviour with tests from the old code and name every untested caller. |
| 0:23-0:30 | Deck 10 | 21 of 24 planted bugs, zero escapes. The AI never merges; a named human signs the decision record. |
