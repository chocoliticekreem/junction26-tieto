# Blocked items for Anson

## 1. `.env.example` could not be written by the agent
The global guard hook (`~/.claude/scripts/global-guard.py`) blocks any write to a file whose name starts with `.env`, including the example file, and blocks shell commands that mention it. I did not route around a security hook. Create it yourself; it holds no secrets:

```
printf 'ANTHROPIC_API_KEY=\nJEV_API_KEY=\nMOCK_LLM=0\nDOTNET=~/.dotnet/dotnet\n' > .env.example
```

The pipeline does not need this file; it reads `.env.local`.

## 2. Human-only steps (the AI must not do these; principle 4: a named human decides)
These are spec section 0 steps 4 and 5 plus the real A/B merge. The pipeline and dashboard are ready for them.

1. `cd dashboard && npm install && npm run dev`, open http://localhost:3000, click **Change** (top right) and enter your name.
2. **Context** tab: read `REVIEW_CONTEXT.md` (24 entries, status draft) and click **Confirm as <you>**. This writes `context/status.json`.
3. **Inbox, then ju26-issue-2234** (verdict Decision A/B): open the decision page, read A and B side by side, click **Merge A** or **Merge B**. The dashboard runs `git merge --no-ff` into `main` of `mock-repo/` (a local bare clone, never upstream) and writes `decisions/ju26-issue-2234.json` with your name. 2297 also has A and B, but both readings carry a verified high finding, so the dashboard refuses to merge it. That refusal is part of the demo.
4. Optionally sign off 2283 (needs sign-off) from its change page.
5. Commit the resulting `decisions/` and `context/status.json` through a PR, or ask the agent to.
