# PullReceipts: 2-minute demo video script

Structure: hook → punchline → how it works (two deck slides) → the user journey (live product) → proof → closing joke. Deck: "PullReceipts: AI review you can sign". Live product: https://pullreceipts.vercel.app (set your name top right; your clicks stay in your browser; reset with https://pullreceipts.vercel.app/api/demo-reset).

Numbers come from `metrics/summary.json`, `metrics/T2.json` and `runs/ju26-issue-*/<latest>/impact.json`. Do not round them up on camera. The proof line is the honest one: if the bench is re-run and the numbers change, update it from `metrics/summary.json`.

## Script (about 270 words, ends before 2:00)

**[Deck: thesis]**
AI already reviews our code. The problem isn't the review. It's accountability. "The AI said it was fine" is *not* a sign-off.

**[Deck: cover]**
So: AI wrote the pull request, and we wrote the pull receipt.

**[Deck: workflow map]**
Here's how it works. It learns your product context, then sets the review depth by risk. It builds the code, runs the real tests, and independent AI reviewers try to break it. And nothing counts as a finding unless it's proven.

**[Deck: depth funnel]**
On our biggest change, 222 of 237 files didn't need reading at all: we rebuilt them and checked they match. The AI reviews the rest. You just read the receipt.

**[Live: Product context page → click "Confirm"]**
It starts with your product. The reviewer drafts what it knows, your conventions and what's deliberate, and a named person confirms it. From then on, it reviews against that.

**[Live: queue]**
Then the queue: what needs a decision, and what's blocked.

**[Live: 2297, plain words → "Impact on other features" → receipt]**
Every change explains itself in plain words. Then it checks the blast radius: the interdependencies, meaning every other part of the product that relies on the code that changed. Here, one of those has no test at all, so we flag it instead of hoping. And every finding has a receipt: something that actually ran.

**[Live: 2297 decision]**
When the ticket could mean two things, it doesn't guess. It builds both and lets you choose.

**[Live: 2283, click Sign off]**
When it's clean, a real person signs, with their name on it. The AI never merges.

**[Deck: proof]**
We planted bugs in 24 changes. It caught all 24. And nothing slipped through as approved.

**[Deck: close]**
In a world where AI ships the PRs, we make sure you never need a PR team afterwards.

## Recording notes

- Before recording: open the five live pages in tabs (Product context, queue, 2297 change, 2297 decision, 2283 change) and set your name once. The context must still say "draft" and 2283 must be unsigned; on localhost, ask for a reset; on the public site, open `/api/demo-reset`.
- Let the receipt finish printing and the stamp land before talking over it.
- After recording: open `/api/demo-reset` to clear your sign-off (judges never see it; it lives in your browser).
