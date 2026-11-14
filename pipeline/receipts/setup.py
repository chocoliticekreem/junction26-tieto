"""`receipts setup`: bare-clone the sample repo into MOCK_REPO, ensure branches, build base once.

Idempotent: an existing clone is reused, `main` is created only if missing (A/B merges move it later).
"""

import os
from pathlib import Path

from . import config, runstore, sandbox
from .gitutil import git, ref_exists, rev_parse

# A local clone of the same upstream; --reference + --dissociate copies objects from it instead of
# downloading them. Optional: falls back to a plain network clone.
REFERENCE_CLONE = Path(os.environ.get(
    "RECEIPTS_REFERENCE_CLONE",
    "/private/tmp/claude-501/-Users-sonionring-orca-projects-junction/"
    "aae424c1-cec2-4b34-862a-bf3153e7d7ee/scratchpad/tieto/repo",
))


def ensure_clone() -> None:
    repo = config.MOCK_REPO
    if not (repo / "HEAD").exists():
        repo.parent.mkdir(parents=True, exist_ok=True)
        cmd = ["clone", "--bare"]
        if (REFERENCE_CLONE / ".git").exists():
            cmd += ["--reference", str(REFERENCE_CLONE), "--dissociate"]
        print(f"cloning {config.UPSTREAM_URL} -> {repo}")
        git(*cmd, config.UPSTREAM_URL, str(repo), cwd=repo.parent)
    missing = [b for b in config.SAMPLE_BRANCHES if not ref_exists(repo, f"refs/heads/{b}")]
    if missing:
        refspecs = [f"+refs/heads/{b}:refs/heads/{b}" for b in missing]
        git("fetch", config.UPSTREAM_URL, *refspecs, cwd=repo)
    if not ref_exists(repo, "refs/heads/main"):
        git("branch", "main", rev_parse(repo, config.BASE_SHA), cwd=repo)
    for b in [*config.SAMPLE_BRANCHES, "main"]:
        print(f"  {b:18} {rev_parse(repo, b)[:10]}")


def run_setup() -> int:
    ensure_clone()
    wt = sandbox.worktree("base", config.BASE_SHA)
    run_dir = config.RUNS / "_setup" / "latest-build"
    run_dir.mkdir(parents=True, exist_ok=True)
    receipt = sandbox.build(wt, run_dir, "build-base")
    runstore.write(run_dir, "commands.json", [receipt])
    status = "ok" if receipt.exit_code == 0 else "FAILED"
    print(f"base build {status}: exit {receipt.exit_code} in {receipt.duration_s}s "
          f"(worktree {wt}, log {run_dir / receipt.log_path})")
    return 0 if receipt.exit_code == 0 else 1
