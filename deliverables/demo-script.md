# Demo script

Setup before recording: `cd dashboard && npm run dev`, open http://localhost:3000, name picker set to your name, browser at 1440 wide. Offline fallback: everything reads local files, and `MOCK_LLM=1 uv run receipts review ...` replays recorded model output, so the demo needs no network.

## 2-minute video

| Time | Screen | Say (short, declarative) |
|---|---|---|
| 0:00-0:12 | Title card, then the queue | "Agent tokens overtook human tokens this year. 'The AI said it was fine' is not a review. PullReceipts makes AI review auditable." |
| 0:12-0:25 | Queue: four Tieto changes, verdict pills | "Four agent-written changes. Two need sign-off, two are blocked, and every verdict links to executed evidence. Behind them, 36 practice changes with planted defects tell us how far to trust it." |
| 0:25-0:47 | `ju26-issue-2283`: requirement, the fail-on-base/pass-on-head receipt, the mutant table, Not checked | "The reviewer wrote tests from the issue text alone. They fail on the base commit and pass on the change: the fix is proven. But two mutants survive the whole test suite: swap an and for an or, and no test notices. Stryker.NET agrees. Below: what was not checked, and why." |
| 0:47-0:58 | `ju26-issue-2297`: routing map with the generated block collapsed | "237 files. 222 are generated, so they are verified by regenerating them, not by reading them: 89 snapshot tests. 15 files were reviewed." |
| 0:58-1:08 | `ju26-issue-2297`, step "Impact on other features": blast radius and the untested caller | "Does it break an old feature? Seven existing methods, 12 callers elsewhere. Tests pinned from the old code still pass. One Unity caller has no test: declared, not assumed safe." |
| 1:08-1:32 | `/decision/ju26-issue-2297`: the vote ("3 of 3 independent readers found two readings"), Merge disabled with the reason. Then `/change/ju26-issue-2297-r2@verda` | "Three independent readers found two readings, so the AI refuses to pick and builds both. Both share a verified defect, so neither can merge. The implementing agent fixed it. Round 2: the blockers are gone and side A needs sign-off, but the second reading could not be built. So I sign off A by name, or send it back. The AI never merges." |
| 1:32-1:44 | `/questions` and `/context`: a question answered once, then the ledger entry it became | "When the verdict depends on intent, it asks, at most three questions per change, and writes the answer down. On the same 18 cases, questions fell from 0.50 to 0.28 per change, none repeated." |
| 1:44-2:00 | Track record: recall by lens, false alarms, trust levels, the Verda comparison | "On 36 changes with planted defects: 24 of 24 caught, zero escapes. An open-weight model hosted in the EU gave the same four verdicts at about half the cost. Trust is earned per area and reset after an escape. AI review you can audit." |

## Live walkthrough (judges)

1. Same flow as the video, slower. Open one receipt log (`View log`) to show the actual `dotnet test` output behind a claim.
2. Open the hunch list on 2283: "These are suspicions without a receipt. We show them, but we never call them findings."
3. Open "Impact on other features" on 2129: 14 callers outside the change, 11 of them `.Equals(` calls matched by name (most on other types), and the one real untested caller (`MessagePackGenerator.cs:156`, `Comparer.Equals`). Say plainly that call sites are matched by name, not with a semantic model.
4. If a sign-off or merge was made: show `decisions/<change>.json` (and, for a merge, the commit in `mock-repo`: `git -C mock-repo log -1 main`). Otherwise show the disabled Merge button on 2297 and its reason.

Answers to the three judge questions (from the concept card):

- **What would make you not trust your own output?** The track record says where: low-recall lenses and areas in T1 (security: 0 of 4 on its own), router misses in T3, every hunch, everything under "not checked", and callers matched by name only. A track record on planted defects is a proxy; in production you feed reverts and incidents back into the area trust.
- **Who owns the decision?** The named human in the decision record. The AI never merges, and the dashboard refuses to merge a blocked branch.
- **How would this scale?** Depth follows risk, generated code is verified as a block, the ledger cuts questions over time, the model can be swapped for an open-weight one the company hosts (measured on Verda), and sandboxes exist per change type (.NET and SQL are built; web UI and mobile follow the same receipt shape).

## Commands to have ready

```
cd pipeline
uv run receipts review --change ju26-issue-2283 --req REQ-2283     # several minutes live, or MOCK_LLM=1 to replay
uv run receipts metrics
```
