"""Thin subprocess wrapper around git. Every pipeline module shells out through here."""

import subprocess
from pathlib import Path


class GitError(RuntimeError):
    pass


def git(*args: str, cwd: Path | str, check: bool = True) -> str:
    proc = subprocess.run(["git", *args], cwd=str(cwd), capture_output=True, text=True)
    if check and proc.returncode != 0:
        raise GitError(f"git {' '.join(args)} (cwd={cwd}) failed: {proc.stderr.strip()}")
    return proc.stdout


def rev_parse(repo: Path, ref: str) -> str:
    return git("rev-parse", "--verify", f"{ref}^{{commit}}", cwd=repo).strip()


def ref_exists(repo: Path, ref: str) -> bool:
    return subprocess.run(
        ["git", "rev-parse", "--verify", "--quiet", f"{ref}^{{commit}}"],
        cwd=str(repo), capture_output=True,
    ).returncode == 0
