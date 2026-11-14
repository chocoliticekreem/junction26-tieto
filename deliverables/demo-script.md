# Demo script

Setup before recording: `cd dashboard && npm run dev`, open http://localhost:3000, name picker set to your name, browser at 1440 wide. Offline fallback: everything reads local files, and `MOCK_LLM=1 uv run receipts review ...` replays recorded model output, so the demo needs no network.

## 2-minute video

| Time | Screen | Say (short, declarative) |
|---|---|---|
| 0:00-0:12 | Title card, then the Inbox | "Agent tokens overtook human tokens this year. 'The AI said it was fine' is not a review. Receipts makes AI review auditable." |
| 0:12-0:30 | Inbox: four Tieto changes, the bench below, verdict pills | "Four agent-written changes. Two need sign-off, two are blocked, and every verdict links to executed evidence. Below: 36 bench changes with planted defects, which is how we know how far to trust it." |
| 0:30-0:55 | `ju26-issue-2283`: requirement, Checked table, the fail-on-base/pass-on-head receipt, the mutant table, Not checked | "The reviewer wrote tests from the issue text alone. They fail on the base commit and pass on the change: the fix is proven. But two mutants survive the whole test suite: swap an and for an or, and no test notices. Stryker.NET agrees. Below: what was not checked, and why." |
| 0:55-1:10 | `ju26-issue-2297`: routing map with the generated block collapsed | "237 files. 222 are generated, so they are verified by regenerating them, not by reading them: 89 snapshot tests. 15 files were reviewed." |
| 1:10-1:30 | `/decision/ju26-issue-2297`: A and B side by side, the vote ("3 of 3 independent readers found two readings"), the quoted requirement, Merge buttons disabled with the reason. If round 2 has been reviewed (BLOCKED.md item 1.2) and both sides are unblocked, use `/decision/ju26-issue-2297-r2` instead and click **Merge A**; the decision record and commit sha appear | "The issue says 'I do not feel like a setting is needed, but a setting would be fine too.' Three independent readers found two readings, so the AI refuses to pick. It builds both branches and runs the receipts on each. And it found a defect both readings share, so neither can merge until it is fixed. When one is clean, I pick, and the decision is recorded under my name." |
| 1:30-1:45 | `/questions` and `/context`: a bait question answered once, then the warm run citing the ledger | "When the verdict depends on intent, it asks, at most three questions per change, and writes down the answer. Next time it cites the ledger instead of asking." |
| 1:45-2:00 | `/track-record`: recall by lens, false alarms, router cost, trust levels | "Measured on 36 changes with planted defects: 19 of 24 planted defects caught, zero escapes, questions per change from 0.25 to 0.04, and 94 % of the largest change never reached a human. Trust is earned per area and reset after an escape. AI review you can audit." |

## Live walkthrough (judges)

1. Same flow as the video, slower. Open one receipt log (`View log`) to show the actual `dotnet test` output behind a claim.
2. Open the hunch list on 2283: "These are suspicions without a receipt. We show them, but we never call them findings."
3. If a merge was made: show `decisions/<change>.json` and the merge commit in `mock-repo` (`git -C mock-repo log -1 main`). Otherwise show the disabled Merge button on 2297 and its reason.

Answers to the three judge questions (from the concept card):

- **What would make you not trust your own output?** The track record says where: low-recall lenses and areas in T1, router misses in T3, every hunch, and everything under "not checked". A track record on planted defects is a proxy; in production you feed reverts and incidents back into the area trust.
- **Who owns the decision?** The named human in the decision record. The AI never merges, and the dashboard refuses to merge a blocked branch.
- **How would this scale?** Depth follows risk, generated code is verified as a block, the ledger cuts questions over time, and sandboxes exist per change type (.NET is built; SQL, web UI and mobile follow the same receipt shape).

## Commands to have ready

```
cd pipeline
uv run receipts review --change ju26-issue-2283 --req REQ-2283     # about 3.5 min live, or MOCK_LLM=1 to replay
uv run receipts metrics
```
