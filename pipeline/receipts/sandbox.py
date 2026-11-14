""".NET sandbox adapter: git worktrees from the bare mock repo, build, test, TRX parsing, patching.

Every executed command becomes a CommandReceipt with its log at run_dir/logs/<id>.log.

Worktrees live under config.WORK and are shared across pipeline processes (and project worktrees,
via RECEIPTS_WORK). Build/test/patch hold an exclusive per-worktree file lock so two processes never
build the same worktree at once. A caller that needs patch -> build -> test -> reset to be atomic
(mutation) wraps the sequence in `with lock(wt):`; the lock is re-entrant within one process.
"""

import fcntl
import os
import re
import shutil
import subprocess
import threading
import time
import xml.etree.ElementTree as ET
from contextlib import contextmanager
from pathlib import Path

from . import config
from .gitutil import GitError, git, rev_parse
from .models import CommandReceipt, TestCase, TestRun, now_iso
from .runstore import portable, portable_file

TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
BUILD_TIMEOUT_S = 1800
TEST_TIMEOUT_S = 1800

_held: dict[str, int] = {}
_held_guard = threading.Lock()


@contextmanager
def _flock(path: Path):
    key = str(path)
    with _held_guard:
        if key in _held:
            _held[key] += 1
            reentrant = True
        else:
            reentrant = False
    if reentrant:
        try:
            yield
        finally:
            with _held_guard:
                _held[key] -= 1
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w") as fh:
        fcntl.flock(fh, fcntl.LOCK_EX)
        with _held_guard:
            _held[key] = 1
        try:
            yield
        finally:
            with _held_guard:
                del _held[key]
            fcntl.flock(fh, fcntl.LOCK_UN)


def lock(wt: Path):
    """Exclusive lock on one worktree. Re-entrant within this process."""
    return _flock(config.WORK / ".locks" / f"{Path(wt).name}.lock")


ENV_PASS = ("PATH", "HOME", "TMPDIR", "LANG")
ENV_PASS_PREFIXES = ("DOTNET_", "NUGET_")
ENV_DENY = re.compile(r"KEY|TOKEN|SECRET|PASSWORD|^ANTHROPIC|^JEV", re.I)


def dotnet_env() -> dict[str, str]:
    """Environment for every process that builds or runs code from the change under review.

    The change is untrusted (MSBuild targets, source generators and tests execute its code), so it
    gets an allowlist, never os.environ: API keys and tokens must not be readable by it.
    """
    env = {k: v for k, v in os.environ.items()
           if k in ENV_PASS or k.startswith(ENV_PASS_PREFIXES)}
    env.update(config.DOTNET_ENV)
    return {k: v for k, v in env.items() if not ENV_DENY.search(k)}


def run_cmd(
    receipt_id: str, cmd: list[str], cwd: Path, run_dir: Path, timeout: int = BUILD_TIMEOUT_S
) -> CommandReceipt:
    log_rel = f"logs/{receipt_id}.log"
    log_path = run_dir / log_rel
    log_path.parent.mkdir(parents=True, exist_ok=True)
    started = now_iso()
    t0 = time.monotonic()
    with open(log_path, "w") as log:
        log.write(portable(f"$ {' '.join(cmd)}\n# cwd: {cwd}\n# started: {started}\n\n"))
        log.flush()
        try:
            proc = subprocess.run(cmd, cwd=str(cwd), stdout=log, stderr=subprocess.STDOUT,
                                  env=dotnet_env(), timeout=timeout)
            code = proc.returncode
        except subprocess.TimeoutExpired:
            log.write(f"\n# TIMEOUT after {timeout}s\n")
            code = 124
    duration = round(time.monotonic() - t0, 2)
    with open(log_path, "a") as log:
        log.write(f"\n# exit: {code}  duration: {duration}s\n")
    portable_file(log_path)
    return CommandReceipt(id=receipt_id, cmd=[portable(c) for c in cmd], cwd=portable(str(cwd)),
                          exit_code=code,
                          duration_s=duration, log_path=log_rel, started_at=started)


# Worktrees -----------------------------------------------------------------


def worktree(side: str, sha: str, name: str | None = None) -> Path:
    """A detached worktree of `sha` under WORK, reused when it already sits at that sha.

    Default dir is "<side>-<sha10>", so every distinct commit gets its own build cache. Pass `name`
    for a private copy (e.g. mutation runs that must not disturb a shared head worktree).
    """
    full = rev_parse(config.MOCK_REPO, sha)
    path = config.WORK / (name or f"{side}-{full[:10]}")
    with _flock(config.WORK / ".locks" / "worktree-admin.lock"):
        if path.exists():
            try:
                if rev_parse(path, "HEAD") == full:
                    return path
                with lock(path):
                    git("checkout", "--detach", "--force", full, cwd=path)
                    git("clean", "-fd", cwd=path)
                return path
            except GitError:
                shutil.rmtree(path)
        git("worktree", "prune", cwd=config.MOCK_REPO)
        path.parent.mkdir(parents=True, exist_ok=True)
        git("worktree", "add", "--detach", "--force", str(path), full, cwd=config.MOCK_REPO)
    return path


def apply_patch(wt: Path, patch_text: str) -> None:
    with lock(wt):
        proc = subprocess.run(["git", "apply", "--whitespace=nowarn", "-"], cwd=str(wt),
                              input=patch_text, capture_output=True, text=True)
        if proc.returncode != 0:
            raise GitError(f"git apply failed in {wt}: {proc.stderr.strip()}")


def reset(wt: Path) -> None:
    """Discard tracked edits and untracked files; ignored build output (bin/obj) survives."""
    with lock(wt):
        git("checkout", "--", ".", cwd=wt)
        git("clean", "-fd", cwd=wt)


# Build and test ------------------------------------------------------------


def build(wt: Path, run_dir: Path, receipt_id: str, project: str | None = None) -> CommandReceipt:
    cmd = [config.DOTNET, "build", project or config.TEST_PROJECT, "-f", "net8.0"]
    with lock(wt):
        return run_cmd(receipt_id, cmd, wt, run_dir, BUILD_TIMEOUT_S)


def test(
    wt: Path,
    run_dir: Path,
    receipt_id: str,
    side: str,
    filter: str | None = None,
    extra_test_files: dict[str, str] | None = None,
    project: str | None = None,
    commands: list[CommandReceipt] | None = None,
    no_build: bool = False,
    timeout: int = TEST_TIMEOUT_S,
) -> TestRun:
    """Run `dotnet test` (which builds first) and parse its TRX. compiled=False when no TRX appears.

    The CommandReceipt is appended to `commands` when given (TestRun.receipt_id names it).

    extra_test_files maps a path relative to the test project dir to file content; the files are
    written before the build and removed afterwards so the shared worktree stays clean.
    """
    project = project or config.TEST_PROJECT
    proj_dir = wt / Path(project).parent
    results_dir = run_dir / "logs" / f"{receipt_id}-results"
    trx_name = f"{receipt_id}.trx"
    cmd = [config.DOTNET, "test", project, "-f", "net8.0", "--results-directory", str(results_dir),
           "--logger", f"trx;LogFileName={trx_name}"]
    if filter:
        cmd += ["--filter", filter]
    if no_build and not extra_test_files:
        cmd.append("--no-build")
    written: list[Path] = []
    with lock(wt):
        try:
            for rel, content in (extra_test_files or {}).items():
                dest = proj_dir / rel
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_text(content)
                written.append(dest)
            receipt = run_cmd(receipt_id, cmd, wt, run_dir, timeout)
        finally:
            for f in written:
                f.unlink(missing_ok=True)
    trx = results_dir / trx_name
    if trx.exists():
        run = parse_trx(trx, receipt_id, side, filter or "")
        portable_file(trx)
    else:
        run = TestRun(receipt_id=receipt_id, side=side, filter=filter or "", compiled=False)
    if commands is not None:
        commands.append(receipt)
    return run


def _duration_s(raw: str) -> float:
    if not raw:
        return 0.0
    h, m, s = raw.split(":")
    return round(int(h) * 3600 + int(m) * 60 + float(s), 4)


_OUTCOMES = {"passed": "passed", "failed": "failed", "notexecuted": "notexecuted",
             "skipped": "skipped", "inconclusive": "skipped", "timeout": "failed",
             "aborted": "failed", "error": "failed"}


def parse_trx(path: Path, receipt_id: str, side: str, filter: str = "") -> TestRun:
    root = ET.parse(path).getroot()
    cases: list[TestCase] = []
    for r in root.iterfind(".//t:Results/t:UnitTestResult", TRX_NS):
        outcome = _OUTCOMES.get((r.get("outcome") or "").lower(), "failed")
        msg_el = r.find("t:Output/t:ErrorInfo/t:Message", TRX_NS)
        message = (msg_el.text or "").strip()[:2000] if msg_el is not None else ""
        cases.append(TestCase(name=r.get("testName", ""), outcome=outcome,
                              duration_s=_duration_s(r.get("duration", "")), message=message))
    passed = sum(c.outcome == "passed" for c in cases)
    failed = sum(c.outcome == "failed" for c in cases)
    return TestRun(receipt_id=receipt_id, side=side, filter=filter, compiled=True,
                   total=len(cases), passed=passed, failed=failed,
                   skipped=len(cases) - passed - failed, cases=cases)
