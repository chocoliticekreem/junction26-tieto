"""Mock-clone bench: variant branches with known answers (SPEC section 6).

`receipts bench generate` reads `bench/manifest.json` and, for every entry, creates the branch
`mock/<issue>/<variant>` in the bare mock repo (config.MOCK_REPO, or env RECEIPTS_MOCK_REPO):
the agent branch head `ju26-issue-<issue>` plus `bench/plants/<issue>/<variant>.patch`, committed
with a neutral message ("Change for REQ-<issue>"). The `clean` variant points at the agent head.

The patch is applied to a throwaway index (GIT_INDEX_FILE in a temp dir) rather than a checked-out
worktree: the mock repo is bare, `git apply --cached --check` verifies the patch first, and no files
are written outside the object store. Commits use a fixed author, committer and date, so the same
manifest always yields the same commit ids, and a branch already at the expected tree is skipped.

Labels (variant, category, description) live only in the manifest. Branch names contain the variant,
so the reviewer pipeline must never put branch-name text into an LLM prompt: lenses receive the
requirement, hunks and ledger entries only, and the change is identified by its manifest `id`
outside the prompt.
"""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
from pathlib import Path

from . import config

MANIFEST = config.BENCH / "manifest.json"
PLANTS = config.BENCH / "plants"
VARIANTS = ("clean", "logic", "edge", "compat", "security", "missing-test", "drift", "bait", "control")
CATEGORIES = ("none", "correctness", "compatibility", "security", "tests", "requirement", "convention")

COMMIT_ENV = {
    "GIT_AUTHOR_NAME": "Receipts Bench",
    "GIT_AUTHOR_EMAIL": "bench@receipts.invalid",
    "GIT_AUTHOR_DATE": "2026-11-14T00:00:00+00:00",
    "GIT_COMMITTER_NAME": "Receipts Bench",
    "GIT_COMMITTER_EMAIL": "bench@receipts.invalid",
    "GIT_COMMITTER_DATE": "2026-11-14T00:00:00+00:00",
}


def load_manifest() -> list[dict]:
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


def mock_repo_path() -> Path:
    override = os.environ.get("RECEIPTS_MOCK_REPO")
    return Path(override).expanduser() if override else config.MOCK_REPO


def patch_path(entry: dict) -> Path | None:
    if entry["variant"] == "clean":
        return None
    return PLANTS / entry["issue"] / f"{entry['variant']}.patch"


def agent_branch(issue: str) -> str:
    return f"ju26-issue-{issue}"


def _git(repo: Path, *args: str, env: dict | None = None, check: bool = True) -> subprocess.CompletedProcess:
    full_env = {**os.environ, **(env or {})}
    result = subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, env=full_env)
    if check and result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed in {repo}: {result.stderr.strip()}")
    return result


def resolve_commit(repo: Path, *refs: str) -> str | None:
    for ref in refs:
        r = _git(repo, "rev-parse", "--verify", "--quiet", f"{ref}^{{commit}}", check=False)
        if r.returncode == 0:
            return r.stdout.strip()
    return None


def apply_to_tree(repo: Path, base_commit: str, patch: Path, check_only: bool = False) -> str | None:
    """Apply `patch` on top of `base_commit` in a throwaway index; return the resulting tree id.

    Raises RuntimeError when the patch does not apply. With check_only, returns None after the check.
    """
    with tempfile.TemporaryDirectory(prefix="receipts-bench-") as tmp:
        env = {"GIT_INDEX_FILE": str(Path(tmp) / "index")}
        _git(repo, "read-tree", base_commit, env=env)
        _git(repo, "apply", "--cached", "--check", str(patch), env=env)
        if check_only:
            return None
        _git(repo, "apply", "--cached", str(patch), env=env)
        return _git(repo, "write-tree", env=env).stdout.strip()


def _branch_state(repo: Path, branch: str) -> tuple[str, str, str] | None:
    """(commit, tree, first parent) of an existing branch, or None."""
    commit = resolve_commit(repo, f"refs/heads/{branch}")
    if commit is None:
        return None
    tree = _git(repo, "rev-parse", f"{commit}^{{tree}}").stdout.strip()
    parent = _git(repo, "rev-parse", "--verify", "--quiet", f"{commit}^1", check=False).stdout.strip()
    return commit, tree, parent


def _generate_entry(repo: Path, entry: dict) -> str:
    issue, branch = entry["issue"], entry["branch"]
    head = resolve_commit(repo, f"refs/heads/{agent_branch(issue)}", f"refs/remotes/origin/{agent_branch(issue)}")
    if head is None:
        raise RuntimeError(f"agent branch {agent_branch(issue)} not found in {repo}")

    existing = _branch_state(repo, branch)
    patch = patch_path(entry)
    if patch is None:
        if existing and existing[0] == head:
            return "skip"
        _git(repo, "update-ref", f"refs/heads/{branch}", head)
        return "updated" if existing else "created"

    if not patch.exists():
        raise RuntimeError(f"missing plant {patch}")
    tree = apply_to_tree(repo, head, patch)
    if existing and existing[1] == tree and existing[2] == head:
        return "skip"
    message = f"Change for {config.SAMPLE_BRANCHES.get(agent_branch(issue), 'REQ-' + issue)}"
    commit = _git(repo, "commit-tree", tree, "-p", head, "-m", message, env=COMMIT_ENV).stdout.strip()
    _git(repo, "update-ref", f"refs/heads/{branch}", commit)
    return "updated" if existing else "created"


def generate() -> int:
    repo = mock_repo_path()
    if not repo.exists() or _git(repo, "rev-parse", "--git-dir", check=False).returncode != 0:
        print(f"bench generate: mock repo not found at {repo}. Run `uv run receipts setup` first, "
              "or point RECEIPTS_MOCK_REPO at a bare clone with local ju26-issue-* branches.")
        return 1

    counts: dict[str, int] = {}
    failures = 0
    for entry in load_manifest():
        try:
            outcome = _generate_entry(repo, entry)
        except RuntimeError as e:
            outcome = "failed"
            failures += 1
            print(f"  {entry['id']}: FAILED {e}")
        else:
            print(f"  {entry['id']}: {outcome} {entry['branch']}")
        counts[outcome] = counts.get(outcome, 0) + 1

    summary = ", ".join(f"{k} {v}" for k, v in sorted(counts.items()))
    print(f"bench generate: {summary} ({repo})")
    return 1 if failures else 0


def run(warm: bool = False, only: str | None = None) -> int:
    raise NotImplementedError("bench run lands with the metrics track")
