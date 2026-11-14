# CHALLENGE.md: what we are judged on

This file is the source of truth for **what the judges want**. `SPEC.md` says how we build it. When the two disagree, this file wins on scope and judging, and the conflict goes into `PROGRESS.md`. Before calling any milestone done, check it against the self-check at the bottom.

## Main challenge: Earned Trust in AI Code (id 351), partner Tieto
Track: Agentic AI. Link: https://hackjunction.app/participate/45/challenges/351 · https://www.tieto.com/

### Prizes
1st €2,000, 2nd €1,000, 3rd €500 (cash).

### Problem (Tieto's words, condensed)
Agent tokens overtook human tokens in Feb 2026 and reached 7.3 trillion by August (OpenRouter data, a16z chart). Reviewing every agent change by hand does not scale, yet engineers need a sound basis to trust AI-reviewed code.

Guiding question: **"How can AI review the code made by AI so that humans can trust it?"**

### What to build
An AI code review **concept, shown to work**: the concept, its principles, a reference implementation, and technology considerations across frontend, backend and security. Question whether line-by-line review is still the right model. Balance **risk, hallucination, transparency, confidence and quality**. The output is a **reference implementation, not a product**. **Tieto weights the concept above the technical implementation.**

### Expected from each team (all six)
1. Define the concept the "PR review" factory should follow.
2. State key principles, including how it balances risk, hallucination, transparency, confidence and quality.
3. Illustrate it on **one sample agent-written change with its requirement ID**.
4. Show **what the AI found, what it refused to decide, and why**.
5. Show how the evidence lets **a named human accept the change without a full read**.
6. Name the **failure modes** you saw and keep a short **decision log**.

### Code input
https://github.com/te-industry-tech-office/junction26-messagepack-csharp (make your own copy first; a local clone is listed in `CLAUDE.md`). Branches with AI-made changes to MessagePack-CSharp:
| Branch | Change | Size |
|---|---|---|
| `ju26-issue-2283` | bug #2283 | 4 files |
| `ju26-issue-2234` | bug #2234 | 4 files |
| `ju26-issue-2129` | issue #2129 | 28 files |
| `ju26-issue-2297` | issue #2297 | 237 files |

### Deliverables
- **Concept card**: recommendation, principles, trade-offs, limits.
- **Live walkthrough** showing where the trust comes from.
- **Lessons log.**
- **Demo video or presentation.**

Out of scope: a finished product, model training, vendor selection.

### Tieto's trust principles (the judges will test against these)
- A **named engineer owns and signs off** each change. The AI review is advice backed by evidence, never the approval itself.
- Evidence **traces to a requirement**.
- **Review depth follows risk.**
- Humans **spot-check the AI reviewer** and track what it misses.
- Trust is **earned per area** from track record, and **withdrawn after escapes**.
- The owner signs off on: what was checked and by whom or what; what was not checked and why; and the residual risk they accept.
- **Not accepted:** "The AI said it was fine" (an unowned merge); confidence without evidence; a risk score nobody can explain.
- Vendor guidance: route human attention by risk; review with full repo context and verify findings before posting; measure outcomes (reverts, incidents), not review activity.

Optional directions: a generalist reviewer covering frontend, backend, security and performance; or a classifier that separates code needing human review from code that meets the standard.

### Judges will ask
- What would make you not trust your own output?
- Who owns the decision?
- How would this scale?

Have a written answer to each in `deliverables/`.

### Judging
| Criterion | Weight | What full marks look like |
|---|---|---|
| **Clarity** | 30% | A clear recommendation, principles, explicit trade-offs and limits |
| **Practicality** | 25% | Works in a real PR flow and scales; a named human owns each decision |
| **Measurement** | 25% | Reverts and incidents don't rise as review time falls; AI findings are real and accepted; time to merge; share routed to humans |
| **Evidence** | 20% | Evidence traceable to a requirement lets a named human accept a change without a full read, including what the AI found, what it refused to decide, and why |

### Requirements
**Synthetic or cleared sample data only.** No 8x code or data.

### Resources
Tieto background on good code (standards, security, quality); a requirements set (intent, functional, non-functional); sample agent-written changes, each with a requirement ID; model access through other partners' credits on the Junction Platform. No named mentors.

## Tech challenges this project can also enter
Tick them on the form only if they are genuinely used.
| Tech challenge | What qualifies | Extra step |
|---|---|---|
| Best use of CRACI (42) | The project's CI runs on CRACI (`runs-on: craci`), with creative use of SBOMs, AI fixes, provenance and the API. A natural fit: receipts produced in CI. | Tick it in the form |
| Best use of Modal (40) | Modal does the heavy lifting, e.g. receipts run in Modal Sandboxes. Judged on effective use of Modal 40%, ambition 25%, impact 20%, a live demo 15%. | Tick it in the form |
| Best Use of Codex (49, OpenAI) | Meaningful use of Codex in building the project | Tick it in the form |
| Best Use of Entire (51, MLH) | Use Entire's Sessions, Graph, Brain and Trails, and show token efficiency | Claim the code at mlh.link/junction-26 |

### Added Sat 14 Nov: Verda (decided, build it once the key is in)
**Verda Compute Track (56).** Prizes $10,000 / $2,500 / $2,500 in Verda credits. Judged 25% each on Ambition (what compute let you do), Fit (the right Verda service, with a clear reason), Working prototype (runs, process documented) and Path to scale.
- **Why now:** the Anthropic key hit its spend limit (`BLOCKED.md` item 1). Verda's **Inference API** (open-weight models, hosted in Europe) becomes a model provider for the lenses and the bench re-run.
- **Use:** add Verda as a provider behind the existing model interface, selected by env (e.g. `LLM_PROVIDER=verda`). Keep Anthropic as an option. Re-run the bench on the final pipeline with Verda, and report quality and cost per change next to the Anthropic numbers already recorded. The comparison is itself a finding for the Measurement criterion.
- **Env:** `VERDA_API_KEY` (use the exact name and base URL from the Verda docs) in `.env.local`; add the name to `.env.example`. Until the key exists, log it in `BLOCKED.md` and keep `MOCK_LLM=1` working.
- **To qualify:** write `docs/verda.md` (process, which product and why, results, cost, how it scales). Anson posts a link to it in Verda's Discord channel.
- Credits: €200 per participant via the QR code on the table or verda.com/blog/junction-2026. Mentors: 3rd floor, Booth 6.

---

## Self-check: compare back to this before calling anything done
Answer each line in `PROGRESS.md` with evidence (a run ID, a screenshot path or a command output), not "yes".
- [ ] **Concept first:** is there a concept card a judge can read in 2 minutes: a recommendation, principles, trade-offs, limits? (Tieto weights the concept above the code.)
- [ ] **All six expectations met:** concept; principles covering risk, hallucination, transparency, confidence and quality; one sample change with its requirement ID; found / refused / why; accept without a full read; failure modes plus a decision log.
- [ ] **Evidence traces to a requirement:** does every finding in the walkthrough link to a requirement ID and a receipt (a test, a mutation check, a regenerated file), not just model opinion?
- [ ] **Refusals are shown:** does the output list what the AI did not check or refused to decide, and why?
- [ ] **A named human signs off:** does the dashboard record who accepted, what was checked, by whom or what, what was not checked, and the residual risk? Is there no path where "the AI said it was fine" merges a change?
- [ ] **Risk routes depth:** do low-risk and high-risk changes get visibly different review depth?
- [ ] **Trust per area:** is trust earned per area from track record and withdrawn after an escape, shown with data?
- [ ] **Measurement:** are there numbers for findings that are real and accepted, the share routed to humans, time to merge, and escapes (planted bugs missed) across the mock clones?
- [ ] **Judges' three questions** answered in writing: what would make you not trust your own output; who owns the decision; how would it scale.
- [ ] **Data:** synthetic or cleared only; nothing from 8x.
- [ ] **Junction rules:** MIT `LICENSE`, README that runs, no keys in git.
- [ ] **Submission pack:** `docs/submission.md` with a project name (max 30 characters), punchline (max 200), description (max 5000), a 2-minute demo script, the concept card, the lessons log, and the tech challenges genuinely used.
- [ ] **Peer review:** can it be shown in 3 minutes, and does it score on Innovation, Impact and Implementation?

---

## Junction 2026 context (applies to every entry)

Source: hackjunction.app/participate/45 and the Info Team's answers in Discord #ask-organizers, scraped Fri 13 Nov 2026. Promo codes, access codes and keys are "on platform": never write them into this repo.

### Deadlines (Helsinki, UTC+2)
| What | When |
|---|---|
| Submissions open | Sat 14 Nov, 12:00 |
| **Final submission deadline** | **Sun 15 Nov, 10:00.** The form closes automatically; no late submissions. Draft projects are not reviewed: it must be set to FINAL. |
| Live peer review | Sun 15 Nov, 10:30 to 13:00 (the rules text says until 14:00). Every team member must be at the table. |
| Final ceremony | Sun 14:00 to 16:05: Top 5 pitches 14:15 to 15:10, challenge winners 15:10 to 15:50, Junction winner 15:50 to 16:05 |

Working rule for this build: feature freeze Sun 07:00, demo video recorded by Sun 08:30, submission final by Sun 09:30.

### How entering works
- 19 main challenges in 6 tracks. Each partner picks its own winners using its own criteria (the rubric in the section above is what decides this challenge).
- **One project per main challenge.** The Info Team said: "you can pick many main challenges, but you have to make separate submissions and separate projects for them" (Sat 01:51), and "You need to be able to present each of the challenges for the peer review" (23:32). This team is building three separate projects for three challenges. This repo is one of them and must stand alone: do not share code with or reference the other two.
- Tech (side) challenges stack on top of a submission: tick them in the form's "Tech Challenge Participation" field. Some need an extra step (listed below for this project).

### Overall Junction prize (separate from the partner prize)
- Live peer review on Sunday picks the Top 5. Peers score **Innovation** (new or different from projects already presented), **Impact** (significant societal impact, share of affected people helped) and **Implementation** (realistic, viable, feasible).
- The Top 5 pitch on stage: exactly 3 minutes (mics cut at time) plus 3 minutes of judges' questions, demo from your own laptop. A panel of 7 judges picks the winner. Main prize €10,000, total pool €100,000.
- Skipping peer review makes the team ineligible for the main prize.

### Submission form (opens Sat 12:00)
| Field | Rule |
|---|---|
| Project Name | Required, max 30 characters |
| Punchline | Required, max 200 characters |
| Description | Required, max 5000 characters, Markdown: what you built and the problem it solves |
| Challenge Picker | Required: this project's main challenge |
| Project Demo | Optional link where it can be experienced live |
| Source Code | Required by the rules: a repo link organisers can access |
| Video | Required by the rules: **max 2 minutes**, publicly viewable (YouTube, Vimeo or Loom). "Not a second longer than 2 minutes, and we will be very strict." |
| Presentation | Optional PDF/PPTX/KEY, max 20 MB |
| Tech Challenge Participation | Optional multi-select |

The Guide also asks for technologies used, whether you want to be credited, a pitch deck PDF, team phone numbers and Discord usernames. Check the live form on Saturday.

So the build must produce, inside this repo: a README that a judge can follow, a punchline (max 200 characters), a description draft (max 5000 characters) in `docs/submission.md`, and a 2-minute demo script.

### Junction rules (prize eligibility)
- No code written before the event. All code outside open-source libraries must be written at the event. Pre-written code for the idea, or a project tied to an existing company, means disqualification. (So: no code from Raise, 8x or any earlier project.)
- Team size 1 to 4. Enter at least one challenge.
- All submissions are licensed under **MIT**, and organisers must be able to access the source. Add an MIT `LICENSE` file.
- Partner APIs and materials stay subject to partner terms.
- The repo stays private until Anson makes it public for submission.
