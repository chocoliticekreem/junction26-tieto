# PullReceipts: 2-minute demo video script

Structure: hook → punchline → how it works (two deck slides) → the user journey (live product) → proof and what's next → closing line. Deck: "PullReceipts: AI review you can sign". Live product: https://pullreceipts.vercel.app (sign in with your name top right; your clicks stay in your browser, reset with https://pullreceipts.vercel.app/api/demo-reset).

Numbers come from `metrics/summary.json` and `runs/ju26-issue-*/<latest>/impact.json`. Do not round them up on camera.

## 2:00 version

| Time | Screen | Say |
|---|---|---|
| 0:00-0:10 | Deck 2, thesis | AI already reviews code. The problem isn't the review, it's accountability. "The AI said it was fine" isn't a sign-off. |
| 0:10-0:15 | Deck 1, cover (Slip waving) | AI wrote the pull request; we wrote the pull receipt. |
| 0:15-0:32 | Deck 3, workflow map (dots flowing) | Every change goes through eight stages: the product's own context, deciding how deep to look, a real build and tests, independent reviewers, receipts, what else it touches, ambiguity, and a named human at the end. |
| 0:32-0:46 | Deck 5, the depth funnel | We don't read everything. Generated files are checked by regenerating them, rules protect the risky areas, and a cheap router scores the rest. On the biggest change, 222 of 237 files never needed a human. |
| 0:46-0:56 | Live: the queue | This is the reviewer's queue: two changes need a decision, two are blocked. |
| 0:56-1:16 | Live: `/change/ju26-issue-2297`: plain words, then step 2 "Impact on other features", then the trust receipt | Each change explains itself in plain words. Here it found what else the change touches: twelve places call the changed code, and one has no test at all. Every finding has a receipt: a test that actually ran. |
| 1:16-1:28 | Live: `/decision/ju26-issue-2297` | The ticket reads two ways, so it built both and refuses to guess. Both share a proven defect, so neither can merge. |
| 1:28-1:40 | Live: `/change/ju26-issue-2283`, click "Sign off", the stamp flips | When it's clean, a named human signs, and the record says what was checked and what wasn't. The AI never merges. |
| 1:40-1:51 | Deck 11, "Measured." | On 36 changes with planted bugs, it caught 21 of 24 and waved none through. Next: web and mobile, on models that can stay in Europe. |
| 1:51-1:57 | Deck 12, close | In a world where AI ships pull requests, we ship pull receipts. |

Recording notes:

- Before recording: open the four live pages in tabs (queue, 2297 change scrolled to step 1, 2297 decision, 2283 change), set your name once, and make sure 2283 is not already signed (open `/api/demo-reset` first if it is).
- Let the receipt finish printing and the stamp land before speaking over it.
- After recording: open `/api/demo-reset` so your sign-off is cleared (judges never see it anyway; it lives in your browser).

## 0:30 version (peer-review table)

| Time | Screen | Say |
|---|---|---|
| 0:00-0:08 | Deck 2 | AI already reviews code. The problem is accountability: "the AI said it was fine" isn't a sign-off. |
| 0:08-0:12 | Deck 1 | AI wrote the pull request; we wrote the pull receipt. |
| 0:12-0:24 | Live: 2297 change page | Every finding has a receipt that actually ran, it shows what else the change can break, and what it didn't check. |
| 0:24-0:30 | Deck 12 | A named human signs. In a world where AI ships pull requests, we ship pull receipts. |
