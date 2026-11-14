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
import re
import shutil
import subprocess
import sys
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
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


# Run -----------------------------------------------------------------------------------------
#
# Each case runs `receipts review --change <manifest id> --req <REQ>` in its own process. The change
# id must resolve in the mock repo, so `refs/tags/<manifest id>` is pointed at the variant branch
# first. Neither the id nor the branch name reaches a prompt: routers and lenses see hunks,
# requirement text and ledger entries only (leak_scan checks this after every run).

CASE_TIMEOUT_S = 3600
DEFAULT_JOBS = 3
LEAK_PATTERNS = [r"mock[/-]\d{4}", r"\bmanifest\b", r"\bbait\b", r"\bplant(ed|s)?\b",
                 r"missing-test", r"\bdrift\b"]


def select_entries(manifest: list[dict], only: str | None) -> list[dict]:
    return [e for e in manifest if not only or only in e["id"]]


def alias_ref(repo: Path, entry: dict) -> str:
    commit = resolve_commit(repo, f"refs/heads/{entry['branch']}")
    if commit is None:
        raise RuntimeError(f"{entry['branch']} not in {repo}; run `receipts bench generate`")
    _git(repo, "update-ref", f"refs/tags/{entry['id']}", commit)
    return commit


def _lines(path: Path) -> list[str]:
    return [ln for ln in path.read_text().splitlines() if ln.strip()] if path.exists() else []


def _write_lines(path: Path, lines: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text("".join(ln + "\n" for ln in lines))
    tmp.replace(path)


def _is_oracle(line: str) -> bool:
    from .oracle import ORACLE

    try:
        return json.loads(line).get("answered_by") == ORACLE
    except ValueError:
        return False


def _strip_oracle_decisions() -> None:
    from .oracle import ORACLE

    md = config.CONTEXT / "REVIEW_CONTEXT.md"
    if md.exists():
        from .context import DECISIONS_HEADING, NO_DECISIONS

        kept = [ln for ln in md.read_text().split("\n") if f"answered by {ORACLE}" not in ln]
        text = "\n".join(kept)
        heading = f"## {DECISIONS_HEADING}"
        if text.rstrip().endswith(heading):  # no decision left: restore the placeholder
            text = text.rstrip() + f"\n\n{NO_DECISIONS}\n"
        md.write_text(text)


class LedgerScope:
    """The ledger lenses see during a bench run, and what is left afterwards.

    Cold: empty. After cold the ledger holds the real entries plus the oracle's answers, so a warm
    run can follow. Warm: only the oracle's answers (from the ledger, else from the archive
    context/ledger.bench-oracle.jsonl). After warm the oracle's answers move to that archive and
    the ledger holds the real entries only.

    Real (non-oracle) entries are first merged into context/ledger.cold-backup.jsonl, so nothing a
    person answered is lost even if the process dies mid-run. Oracle answers never stay in
    REVIEW_CONTEXT.md's decision list (lenses read the ledger, not that list).
    """

    def __init__(self, mode: str):
        self.mode = mode
        self.path = config.CONTEXT / "ledger.jsonl"
        self.backup = config.CONTEXT / "ledger.cold-backup.jsonl"
        self.archive = config.CONTEXT / f"ledger.bench-oracle{config.RUN_SUFFIX}.jsonl"
        self.real: list[str] = []

    def __enter__(self):
        lines = _lines(self.path)
        self.real = [ln for ln in lines if not _is_oracle(ln)]
        saved = _lines(self.backup)
        _write_lines(self.backup, saved + [ln for ln in self.real if ln not in saved])
        if self.mode == "cold":
            _write_lines(self.path, [])
        else:
            oracle = [ln for ln in lines if _is_oracle(ln)] or _lines(self.archive)
            _write_lines(self.path, oracle)
        _strip_oracle_decisions()
        return self

    def __exit__(self, *exc):
        now = [ln for ln in _lines(self.path) if ln not in self.real]
        if self.mode == "warm":
            _write_lines(self.archive, [ln for ln in now if _is_oracle(ln)])
            now = [ln for ln in now if not _is_oracle(ln)]
        _write_lines(self.path, self.real + now)
        _strip_oracle_decisions()
        return False


def _latest(change_id: str) -> str:
    f = config.RUNS / config.run_id(change_id) / "latest"
    return f.read_text().strip() if f.exists() else ""


def _json(path: Path, default=None):
    return json.loads(path.read_text()) if path.exists() else default


def _review_cmd(entry: dict) -> list[str]:
    return [sys.executable, "-m", "receipts.cli", "review", "--change", entry["id"],
            "--req", entry["requirement"], "--no-ab"]


def run_case(entry: dict, mode: str, seq: int, log_dir: Path, env: dict | None = None) -> dict:
    before = _latest(entry["id"])
    cmd = _review_cmd(entry)
    log = log_dir / f"{entry['id']}.log"
    rec: dict = {"id": entry["id"], "mode": mode, "seq": seq,
                 "log": str(log.relative_to(config.RUNS))}
    t0 = time.monotonic()
    try:
        with open(log, "w") as fh:
            proc = subprocess.run(cmd, cwd=str(config.PIPELINE), stdout=fh,
                                  stderr=subprocess.STDOUT, timeout=CASE_TIMEOUT_S,
                                  env={**os.environ, **(env or {})})
        rec["exit_code"] = proc.returncode
    except subprocess.TimeoutExpired:
        rec["exit_code"], rec["error"] = 124, f"timeout after {CASE_TIMEOUT_S}s"
    rec["wall_s"] = round(time.monotonic() - t0, 1)
    after = _latest(entry["id"])
    if not after or after == before:
        rec.setdefault("error", f"no new complete run (exit {rec['exit_code']}); see {rec['log']}")
        return rec
    run_dir = config.RUNS / config.run_id(entry["id"]) / after
    (run_dir / "bench.json").write_text(json.dumps(
        {"mode": mode, "seq": seq, "manifest_id": entry["id"], "wall_s": rec["wall_s"]}, indent=2))
    receipt = _json(run_dir / "receipt.json")
    routing = _json(run_dir / "routing.json", {})
    lenses = _json(run_dir / "lenses.json")
    rec.update(run=str(run_dir.relative_to(config.RUNS)), verdict=receipt["verdict"],
               findings=len(receipt.get("findings", [])), hunches=len(receipt.get("hunches", [])),
               questions=len(_json(run_dir / "questions.json", [])),
               provider=config.LLM_PROVIDER,
               router=routing.get("router", ""), router_usd=routing.get("cost_usd", 0.0),
               router_eur=routing.get("cost_eur", 0.0),
               lens_eur=lenses.get("total_cost_eur", 0.0) if lenses else None,
               lens_usd=lenses.get("total_cost_usd", 0.0) if lenses else None,
               lens_spend_usd=round(sum(x.get("cost_usd", 0.0) for x in lenses.get("lenses", [])
                                        if not x.get("from_fixture")), 6) if lenses else None)
    return rec


def answer_with_oracle(entry: dict, run_dir: Path) -> list[dict]:
    from . import context, oracle
    from .models import Question
    from .questions import load_ledger
    from .review import load_requirement

    questions = [Question.model_validate(q) for q in _json(run_dir / "questions.json", [])]
    if not questions:
        return []
    _, req_text, _ = load_requirement(entry["requirement"])
    return oracle.answer_questions(questions, entry["id"], req_text, load_ledger(),
                                   context.append_ledger)


def _remove_worktree(path: Path) -> None:
    from . import sandbox
    from .gitutil import GitError, git

    if not path.exists():
        return
    with sandbox._flock(config.WORK / ".locks" / "worktree-admin.lock"), sandbox.lock(path):
        try:
            git("worktree", "remove", "--force", str(path), cwd=config.MOCK_REPO)
        except GitError:
            shutil.rmtree(path, ignore_errors=True)
            git("worktree", "prune", cwd=config.MOCK_REPO, check=False)


def cleanup_worktrees(entry: dict, commit: str, keep: set[str]) -> list[str]:
    """Drop the case's private mutation worktree and its head worktree, unless the head is shared
    (the clean variant's head is the agent branch the sample reviews also use). Base stays."""
    names = [f"mut-{config.run_id(entry['id'])}"]
    if commit not in keep:
        names.append(f"head-{commit[:10]}")
    removed = []
    for n in names:
        if (config.WORK / n).exists():
            _remove_worktree(config.WORK / n)
            removed.append(n)
    return removed


def leak_scan(since: float, run_dirs: list[Path]) -> list[dict]:
    """Search what prompts are built from (intake hunks) and the prompt side of every LLM fixture
    written during this run for bench labels: branch names, manifest ids, variant words. Model
    replies are not scanned: a lens may say "drift" in its own words."""
    pats = [re.compile(p, re.I) for p in LEAK_PATTERNS]
    sources: list[tuple[str, str]] = []
    for f in config.FIXTURES_LLM.rglob("*.json"):
        if f.stat().st_mtime >= since:
            prompt = (_json(f, {}) or {}).get("prompt_preview", "")
            if prompt:
                sources.append((str(f.relative_to(config.ROOT)), prompt))
    ledger_text = "\n".join(f"{e.get('question', '')} {e.get('answer', '')}"
                            for e in map(json.loads, _lines(config.CONTEXT / "ledger.jsonl")))
    sources.append(("context/ledger.jsonl (questions and answers lenses see)", ledger_text))
    for d in run_dirs:
        data = _json(d / "intake.json")
        if data:
            text = "\n".join(h["file"] + "\n" + h["text"] for h in data.get("hunks", []))
            sources.append((str((d / "intake.json").relative_to(config.RUNS)), text))
    hits: list[dict] = []
    for name, text in sources:
        for p in pats:
            m = p.search(text)
            if m:
                hits.append({"source": name, "pattern": p.pattern,
                             "context": text[max(0, m.start() - 60): m.end() + 60]})
    return hits


def _write_summary(mode: str, records: list[dict], meta: dict) -> Path:
    """Merge by case id, so a partial (--only) re-run updates its cases and keeps the rest."""
    path = config.RUNS / f"bench-{mode}-summary{config.RUN_SUFFIX}.json"
    by_id = {r["id"]: r for r in (_json(path, {}) or {}).get("cases", [])}
    by_id.update({r["id"]: r for r in records})
    order = {e["id"]: i for i, e in enumerate(load_manifest())}
    cases = sorted(by_id.values(), key=lambda r: order.get(r["id"], 999))
    walls = [r["wall_s"] for r in cases if "wall_s" in r]
    summary = {
        "mode": mode, **meta, "cases": cases,
        "totals": {
            "cases": len(cases), "errors": sum(1 for r in cases if r.get("error")),
            "wall_s_sum": round(sum(walls), 1), "wall_s_max": max(walls, default=0),
            "router_usd": round(sum(r.get("router_usd") or 0 for r in cases), 6),
            "lens_usd": round(sum(r.get("lens_usd") or 0 for r in cases), 4),
            "lens_spend_usd": round(sum(r.get("lens_spend_usd") or 0 for r in cases), 4),
            "lens_eur": round(sum(r.get("lens_eur") or 0 for r in cases), 4),
            "verdicts": {v: sum(r.get("verdict") == v for r in cases)
                         for v in sorted({r["verdict"] for r in cases if r.get("verdict")})},
        },
    }
    config.RUNS.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(summary, indent=2) + "\n")
    return path


def prune_raw(run_dir: Path) -> int:
    """Drop raw TRX files and the compat harness build output (obj/, out/) from a bench run: most
    of its size, and no receipt. tests.json holds the parsed results, compat/*.json the byte and
    API results, and the dashboard reads the .log files only. Returns bytes freed."""
    freed = 0
    raw = [*run_dir.glob("logs/*-results"), *run_dir.glob("compat/harness-*/obj"),
           *run_dir.glob("compat/harness-*/out")]
    for d in raw:
        freed += sum(f.stat().st_size for f in d.rglob("*") if f.is_file())
        shutil.rmtree(d, ignore_errors=True)
    return freed


def _finish_case(fut, entry: dict, mode: str, seq: int, commit: str, keep: set[str],
                 oracle_log: list[dict], oracle: bool = True) -> dict:
    try:
        rec = fut.result()
    except Exception as e:  # noqa: BLE001 - one broken case must not stop the bench
        rec = {"id": entry["id"], "mode": mode, "seq": seq, "error": f"{type(e).__name__}: {e}"}
    if oracle and mode == "cold" and rec.get("run"):
        _oracle_case(entry, rec, oracle_log)
    rec["worktrees_removed"] = cleanup_worktrees(entry, commit, keep)
    if rec.get("run"):
        prune_raw(config.RUNS / rec["run"])
    return rec


def _oracle_case(entry: dict, rec: dict, oracle_log: list[dict]) -> None:
    answers = answer_with_oracle(entry, config.RUNS / rec["run"])
    rec["oracle_answers"] = len(answers)
    oracle_log += [{"id": entry["id"], **a} for a in answers]


# Batch mode ----------------------------------------------------------------------------------
#
# `bench run --batch` answers every case's LLM calls through the Message Batches API (50% price):
#   round r: every case runs `receipts review` with RECEIPTS_LLM_MODE=collect, which queues the
#            requests that miss a fixture (no builds; see review._collect); the queue is submitted
#            as one batch and the results are written as fixtures, exactly as live calls would be.
#   A prompt that depends on an earlier answer is queued in a later round: the six lenses and
#   three readers in round 1, the readers' orientation call in round 2 when two readers flag an
#   ambiguity (and the Haiku router, if it is the router, goes one round ahead of the lenses).
#   final:   every case runs in full with RECEIPTS_LLM_MODE=replay, so no live Anthropic call is
#            possible; a request that still has no fixture is reported under "not checked".
# In a cold batch run the oracle answers questions after the final pass, in manifest order: a case
# answered mid-run would change the ledger later cases' prompts are built from, and those prompts
# were already batched against the empty ledger. So every cold case sees the empty ledger.

MAX_ROUNDS = 4


def collect_case(entry: dict, log: Path) -> int:
    env = {**os.environ, "RECEIPTS_LLM_MODE": "collect", "RECEIPTS_BATCH_DIR": str(config.BATCH_DIR)}
    try:
        with open(log, "w") as fh:
            return subprocess.run(_review_cmd(entry), cwd=str(config.PIPELINE), stdout=fh,
                                  stderr=subprocess.STDOUT, timeout=CASE_TIMEOUT_S, env=env).returncode
    except subprocess.TimeoutExpired:
        return 124


def batch_rounds(entries: list[dict], jobs: int, log_dir: Path, max_usd: float | None,
                 dry_run: bool, client=None) -> tuple[int | None, list[dict]]:
    """Collect, estimate, batch, repeat until no case queues a request (at most MAX_ROUNDS
    batches). Returns (exit code to stop with, or None to go on to the final pass; round log)."""
    from . import estimate, llm

    config.BATCH_DIR.mkdir(parents=True, exist_ok=True)
    llm.failed_path().unlink(missing_ok=True)
    history = estimate.historical_output()
    rounds: list[dict] = []
    for rnd in range(1, MAX_ROUNDS + 1):
        llm.queue_path().unlink(missing_ok=True)
        with ThreadPoolExecutor(max_workers=max(1, jobs)) as pool:
            codes = dict(zip([e["id"] for e in entries], pool.map(
                lambda e: collect_case(e, log_dir / f"{e['id']}.collect-r{rnd}.log"), entries)))
        requests = llm.unique_requests(llm.read_jsonl(llm.queue_path()))
        info: dict = {"round": rnd, "requests": len(requests),
                      "collect_errors": sorted(i for i, c in codes.items() if c != 0)}
        if info["collect_errors"]:
            print(f"  round {rnd}: collect failed for {info['collect_errors']} (logs in {log_dir})")
        if not requests:
            print(f"  round {rnd}: no request left to batch")
            rounds.append(info)
            return None, rounds
        lines = estimate.estimate(requests, history)
        info["estimate"] = estimate.to_json(lines)
        t = info["estimate"]["totals"]
        print(f"  round {rnd}: {len(requests)} requests queued; estimate "
              f"${t['batch_usd']:.2f} batch vs ${t['standard_usd']:.2f} live")
        print(estimate.format_table(lines))
        rounds.append(info)
        if dry_run:
            print("bench run --dry-run: estimate only, nothing submitted. Requests that depend on "
                  "round 1 answers (an orientation call after a new reader vote) come in later "
                  "rounds and are not included.")
            return 0, rounds
        if max_usd is not None and t["batch_usd"] > max_usd:
            print(f"bench run: refusing to submit: estimated ${t['batch_usd']:.2f} exceeds "
                  f"--max-usd {max_usd:.2f}")
            return 2, rounds
        rep = llm.run_batch(requests, client=client)
        info.update(batch_id=rep.batch_id, succeeded=rep.succeeded, errored=rep.errored,
                    cost_usd=rep.cost_usd)
        print(f"  round {rnd}: batch {rep.batch_id}: {rep.succeeded} ok, {len(rep.errored)} failed, "
              f"${rep.cost_usd:.2f} at batch prices")
        for err in rep.errored:
            print(f"    failed {err['purpose']}/{err['custom_id']}: {err['reason']}")
    print(f"bench run: {MAX_ROUNDS} batch rounds done; any call still unanswered is reported as "
          "not checked in the final pass")
    return None, rounds


def _run_cases(entries: list[dict], mode: str, jobs: int, log_dir: Path, seq_of: dict,
               commits: dict, keep: set[str], oracle_log: list[dict], env: dict | None,
               oracle: bool) -> list[dict]:
    records: list[dict] = []
    with ThreadPoolExecutor(max_workers=max(1, jobs)) as pool:
        futures = {pool.submit(run_case, e, mode, seq_of[e["id"]], log_dir, env): e for e in entries}
        for fut in as_completed(futures):
            e = futures[fut]
            rec = _finish_case(fut, e, mode, seq_of[e["id"]], commits[e["id"]], keep, oracle_log,
                               oracle)
            records.append(rec)
            print(f"  [{len(records)}/{len(entries)}] {e['id']}: {rec.get('verdict', 'ERROR')} "
                  f"in {rec.get('wall_s', '?')}s" + (f" ({rec['error']})" if rec.get("error") else ""))
    return records


def run(warm: bool = False, only: str | None = None, jobs: int = DEFAULT_JOBS,
        batch: bool = False, max_usd: float | None = None, dry_run: bool = False) -> int:
    mode = "warm" if warm else "cold"
    if config.LLM_PROVIDER == "verda" and (batch or dry_run):
        print("bench run: Verda Inference has no batch API (docs.verda.com/inference/faq); "
              + ("--dry-run needs the batch collect path, nothing to estimate" if dry_run
                 else "--batch falls back to live calls, still capped by VERDA_MAX_EUR"))
        if dry_run:
            return 2
        batch, max_usd = False, None
    manifest = load_manifest()
    entries = select_entries(manifest, only)
    if not entries:
        print(f"bench run: no manifest entry matches {only!r}")
        return 1
    repo = mock_repo_path()
    seq_of = {e["id"]: i for i, e in enumerate(manifest, start=1)}
    commits = {e["id"]: alias_ref(repo, e) for e in entries}
    keep = {c for b in config.SAMPLE_BRANCHES
            if (c := resolve_commit(repo, f"refs/heads/{b}", f"refs/remotes/origin/{b}"))}
    log_dir = config.RUNS / "_bench-logs" / mode
    log_dir.mkdir(parents=True, exist_ok=True)
    started, t0 = time.time(), time.monotonic()
    oracle_log: list[dict] = []
    batch_log: list[dict] = []
    print(f"bench run [{mode}]: {len(entries)} cases, {jobs} at a time"
          + (" (batch)" if batch else " (dry run)" if dry_run else ""))
    with LedgerScope(mode):
        if batch or dry_run:
            stop, batch_log = batch_rounds(entries, jobs, log_dir, max_usd, dry_run)
            if stop is not None:
                return stop
        env = {"RECEIPTS_LLM_MODE": "replay", "RECEIPTS_BATCH_DIR": str(config.BATCH_DIR)} if batch else None
        records = _run_cases(entries, mode, jobs, log_dir, seq_of, commits, keep, oracle_log, env,
                             oracle=not batch)
        if batch and mode == "cold":
            by_id = {r["id"]: r for r in records}
            for e in entries:
                if by_id.get(e["id"], {}).get("run"):
                    _oracle_case(e, by_id[e["id"]], oracle_log)
    leaks = leak_scan(started, [config.RUNS / r["run"] for r in records if r.get("run")])
    meta = {"only": only, "jobs": jobs,
            "started_at": datetime.fromtimestamp(started, timezone.utc).isoformat(timespec="seconds"),
            "elapsed_s": round(time.monotonic() - t0, 1), "oracle_answers": oracle_log,
            "leak_scan": leaks, "lens_effort": config.LENS_EFFORT,
            "provider": config.LLM_PROVIDER, "models": {"deep": config.MODEL_DEEP,
                                                     "lens": config.MODEL_LENS,
                                                     "router": config.MODEL_ROUTER}}
    if batch:
        meta["batch"] = {"rounds": batch_log,
                         "cost_usd": round(sum(r.get("cost_usd", 0.0) for r in batch_log), 4)}
    path = _write_summary(mode, records, meta)
    errors = sum(1 for r in records if r.get("error"))
    print(f"bench run [{mode}]: {len(records) - errors} ok, {errors} errors in {meta['elapsed_s']}s; "
          f"{len(oracle_log)} oracle answers; leak scan: {len(leaks)} hit(s); summary {path}")
    return 1 if errors or leaks else 0
