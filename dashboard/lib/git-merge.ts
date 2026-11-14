import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

function git(args: string[], cwd?: string): string {
  return execFileSync("git", args, {
    cwd,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    timeout: 60_000,
    env: { ...process.env, GIT_TERMINAL_PROMPT: "0" },
  }).trim();
}

export function branchExists(repo: string, branch: string): boolean {
  try {
    git(["-C", repo, "rev-parse", "--verify", "--quiet", `refs/heads/${branch}`]);
    return true;
  } catch {
    return false;
  }
}

/**
 * Merge `branch` into `main` of a bare repo with --no-ff, authored by `who`.
 * Bare repos cannot merge in place, so: temporary worktree on main -> merge -> remove worktree.
 */
export function mergeIntoMain(repo: string, branch: string, who: string, message: string): string {
  if (!fs.existsSync(repo)) throw new Error(`Mock repo not found at ${repo}`);
  if (!branchExists(repo, "main")) throw new Error("Mock repo has no main branch");
  if (!branchExists(repo, branch)) throw new Error(`Branch ${branch} does not exist in the mock repo`);

  const wt = fs.mkdtempSync(path.join(os.tmpdir(), "receipts-merge-"));
  fs.rmSync(wt, { recursive: true, force: true });
  git(["-C", repo, "worktree", "prune"]);
  git(["-C", repo, "worktree", "add", wt, "main"]);
  try {
    const email = `${who.toLowerCase().replace(/[^a-z0-9]+/g, ".").replace(/^\.|\.$/g, "") || "human"}@receipts.local`;
    git(["-C", wt, "-c", `user.name=${who}`, "-c", `user.email=${email}`, "merge", "--no-ff", "--no-edit", "-m", message, branch]);
    return git(["-C", wt, "rev-parse", "HEAD"]);
  } catch (e) {
    const msg = e instanceof Error && "stderr" in e ? String((e as { stderr?: unknown }).stderr || e.message) : String(e);
    throw new Error(`git merge failed: ${msg.trim()}`);
  } finally {
    try {
      git(["-C", repo, "worktree", "remove", "--force", wt]);
    } catch {
      fs.rmSync(wt, { recursive: true, force: true });
      git(["-C", repo, "worktree", "prune"]);
    }
  }
}
