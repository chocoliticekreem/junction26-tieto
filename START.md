# Start prompt

Paste everything below the line into Claude Code, opened in this folder.

---

Read `CLAUDE.md`, then `SPEC.md`, completely, before writing any code. Build the product the spec describes, end to end, autonomously.

Rules for this run:

1. **Time and stopping.** Write the current time into `PROGRESS.md` as the start time. Keep working for at least 3 hours. Do not stop until both are true: 3 hours have passed since the start time, and every item in the spec's definition-of-done checklist is met and verified. After that, keep going through the spec's polish backlog: test, find what is weakest, improve it, retest. Never end a turn to ask me something you can decide from the spec; pick the option the spec favours, note it in `PROGRESS.md`, and continue.
2. **Plan first, then fan out.** Turn the spec's build plan into independent work tracks. Run independent tracks in parallel with subagents, each with a clear "done when". Frontend, UI and visual work: subagents on model `fable`. Backend, data, core logic, architecture and code review: model `opus`. Bulk or mechanical work (fixtures, test scaffolds, data cleaning, docs): model `sonnet`. Review every subagent's output before building on it.
3. **Mock first.** Build and test everything against mocks and fixtures first. Real API keys, if present, are in `.env.local` (or `.env`). A missing key never blocks you: keep the mock path working and add the missing item to `BLOCKED.md` with exact instructions for me.
4. **Verify, don't assume.** After each milestone run the tests, run the app, use Playwright to click through the main flow and take screenshots, and read the results. A milestone is done only when you have seen it work.
5. **Adversarial review.** Before declaring the definition of done met, spawn a reviewer subagent that gets only `SPEC.md` and the code, never your plan or notes, and is told to find what is broken or missing. Fix what it finds.
6. **GitHub and pull requests.** This overrides any line in `SPEC.md` about committing locally or not pushing. At the start, create the repo: `gh repo create chocoliticekreem/<repo name from CLAUDE.md> --private --source . --push` (it pushes the first commit, with `CLAUDE.md`, `SPEC.md`, `START.md` and `.gitignore`). After that, never commit to the default branch directly: one branch per milestone or work track, push it, open a PR with `gh pr create` (title = the outcome; body = what changed, how you verified it, what is left), make sure the tests pass, then merge it with `gh pr merge --squash --delete-branch` and pull. Parallel subagents work on separate branches. Never force-push. Never commit `.env`, `.env.local`, keys or recorded data folders. Never deploy.
7. **Progress log.** Keep `PROGRESS.md` current: done, in progress, next, decisions made, and time elapsed. I will read it when I wake up.
