"""Deleted and weakened tests (SPEC 5.5, test-quality): a change that removes the test covering
its own code. Deterministic, no LLM.

Detection walks the change's first-parent history (base, then every commit before head) over the
test files that history touches, and parses the xUnit test methods ([Fact]/[Theory]) of each
revision. A test is REMOVED when it exists in some revision and not on head, unless head has a test
with the same name in another file (moved) or the same body under another name (renamed). The
revision it is compared from is the last one that still had it: the base for a test the change
deletes outright, or an earlier commit of the change for a test the change added and then deleted
(an agent dropping a test to get green). A test kept on head with fewer assertions (Assert.* calls
plus {|diagnostic|} markup) than in that revision is WEAKENED.

Receipt: the removed test runs alone, with coverage (coverlet, as impact.collect_coverage), at
the last revision that had it, and PASSES there; it is absent on head; and no sentence of the
ticket asks to remove, delete, replace or obsolete tests. Deleting a passing test the ticket does
not ask for is itself the finding (lens test-quality, verified by that run). Severity depends on
what else still tests its code: the full suite runs with coverage on head, and changed source lines
(in files identical in both revisions) that the removed test executed and no remaining test
executes on head are orphaned, as are branch outcomes on those lines (coverlet's json report: line,
IL offset, path, ordinal) it took and no remaining test takes. Something orphaned -> high
("nothing else tests X now"); nothing orphaned -> medium ("other tests still run its lines, but
nothing asserts what it asserted"). A test that failed or did not compile at its last revision, or
a removal the ticket asks for, stays an unverified hunch; so does every weakened test.
"""

from __future__ import annotations

import re
import shutil
from dataclasses import dataclass
from pathlib import Path

from . import config, sandbox
from .gitutil import git
from .impact import COVERLET_ANCHOR, COVERLET_REF, changed_lines, parse_cobertura
from .models import CommandReceipt, Evidence, Finding, NotChecked, ReceiptPlan, TestRun

MAX_REMOVED_CHECKED = 6
COVERLET_FORMAT_SETTING = "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format"
TEST_ROOT = "tests/"
_TEST_ATTR = re.compile(r"^\s*\[\s*(?:Fact|Theory|SkippableFact|SkippableTheory)\b")
_SIGNATURE = re.compile(r"\b(\w+)\s*(?:<[^>()]*>)?\s*\(")
_CLASS = re.compile(r"\b(?:class|record|struct)\s+(\w+)")
_ASSERT = re.compile(r"\bAssert\.\w+\s*\(")


@dataclass(frozen=True)
class TestMethod:
    cls: str
    name: str
    file: str
    line: int  # 1-based line of the test attribute
    body: str  # method text from its signature to its closing brace

    @property
    def key(self) -> str:
        return f"{self.cls}.{self.name}"


@dataclass(frozen=True)
class RemovedTest:
    test: TestMethod
    before_sha: str  # last revision that still had the test
    on_base: bool


@dataclass(frozen=True)
class WeakenedTest:
    test: TestMethod  # head version
    before_sha: str
    before_count: int
    head_count: int


# Parsing --------------------------------------------------------------------------------------


def _skip_literal(text: str, i: int) -> int:
    """Index just past the string/char literal or comment starting at text[i], else i."""
    if text.startswith("//", i):
        j = text.find("\n", i)
        return len(text) if j < 0 else j
    if text.startswith("/*", i):
        j = text.find("*/", i + 2)
        return len(text) if j < 0 else j + 2
    j = i
    while j < len(text) and text[j] in "$@":
        j += 1
    if text.startswith('"""', j):
        n = 3
        while text.startswith('"', j + n):
            n += 1
        end = text.find('"' * n, j + n)
        return len(text) if end < 0 else end + n
    if j < len(text) and text[j] == '"':
        verbatim = "@" in text[i:j]
        k = j + 1
        while k < len(text):
            if verbatim and text.startswith('""', k):
                k += 2
            elif not verbatim and text[k] == "\\":
                k += 2
            elif text[k] == '"':
                return k + 1
            else:
                k += 1
        return len(text)
    if text[i] == "'":
        m = re.match(r"'(?:\\.|[^'\\])'", text[i:])
        if m:
            return i + m.end()
    return i


def _block_end(text: str, start: int) -> int:
    """Index just past the brace block opened at the first '{' at or after `start` (string
    literals and comments skipped), or len(text) for an expression-bodied/unterminated member."""
    depth = 0
    i = start
    while i < len(text):
        nxt = _skip_literal(text, i)
        if nxt != i:
            i = nxt
            continue
        c = text[i]
        if c == ";" and depth == 0:
            return i + 1
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return len(text)


def code_only(text: str) -> str:
    """`text` with string/char literals and comments blanked (newlines kept), so declarations
    inside test-fixture source strings are not taken for the file's own."""
    out = []
    i = 0
    while i < len(text):
        nxt = _skip_literal(text, i)
        if nxt != i:
            out.append(re.sub(r"[^\n]", " ", text[i:nxt]))
            i = nxt
        else:
            out.append(text[i])
            i += 1
    return "".join(out)


def parse_tests(path: str, text: str) -> list[TestMethod]:
    """xUnit test methods of one C# file: the method after each [Fact]/[Theory] attribute,
    attributed to the nearest class declared above it (literals and comments ignored)."""
    lines = code_only(text).split("\n")
    offsets = [0]
    for ln in lines:
        offsets.append(offsets[-1] + len(ln) + 1)
    out: list[TestMethod] = []
    cls = Path(path).stem
    i = 0
    while i < len(lines):
        m_cls = _CLASS.search(lines[i])
        if m_cls:
            cls = m_cls.group(1)
        if not _TEST_ATTR.match(lines[i]):
            i += 1
            continue
        attr_line = i
        j = i + 1
        while j < len(lines) and (lines[j].strip().startswith("[") or not lines[j].strip()):
            j += 1
        sig = _SIGNATURE.search(lines[j]) if j < len(lines) else None
        if sig is None:
            i += 1
            continue
        start = offsets[j]
        end = _block_end(text, start)
        out.append(TestMethod(cls=cls, name=sig.group(1), file=path, line=attr_line + 1,
                              body=text[start:end]))
        i = text.count("\n", 0, end) + 1
    return out


def _normalized_body(t: TestMethod) -> str:
    return re.sub(r"\s+", "", t.body.replace(t.name, "", 1))


def assertion_count(t: TestMethod) -> int:
    return len(_ASSERT.findall(t.body)) + t.body.count("{|")


# Detection ------------------------------------------------------------------------------------


def classify_removals(revisions: list[tuple[str, list[TestMethod]]], head: list[TestMethod],
                      base_sha: str) -> tuple[list[RemovedTest], list[WeakenedTest]]:
    """revisions: (sha, tests) from the base forward, head excluded."""
    head_keys = {t.key for t in head}
    head_names = {t.name for t in head}
    head_bodies = {_normalized_body(t) for t in head}
    on_base = {t.key for sha, ts in revisions if sha == base_sha for t in ts}
    last: dict[str, tuple[str, TestMethod]] = {}
    for sha, tests in revisions:
        for t in tests:
            last[t.key] = (sha, t)
    removed: list[RemovedTest] = []
    weakened: list[WeakenedTest] = []
    by_key = {t.key: t for t in head}
    for key, (sha, t) in last.items():
        if key in head_keys:
            h = by_key[key]
            before, now = assertion_count(t), assertion_count(h)
            if now < before:
                weakened.append(WeakenedTest(test=h, before_sha=sha, before_count=before,
                                             head_count=now))
            continue
        if t.name in head_names or _normalized_body(t) in head_bodies:
            continue  # moved to another class/file, or renamed with the same body
        removed.append(RemovedTest(test=t, before_sha=sha, on_base=key in on_base))
    return removed, weakened


def _exists(repo: Path, sha: str, path: str) -> bool:
    import subprocess

    return subprocess.run(["git", "cat-file", "-e", f"{sha}:{path}"], cwd=str(repo),
                          capture_output=True).returncode == 0


def history(repo: Path, base: str, head: str) -> tuple[list[str], list[str]]:
    """(revisions from base forward excluding head, test .cs files touched anywhere in base..head)."""
    commits = git("rev-list", "--first-parent", "--reverse", f"{base}..{head}", cwd=repo).split()
    revisions = [base, *commits[:-1]]
    names = git("log", "--first-parent", "--format=", "--name-only", f"{base}..{head}",
                cwd=repo).split("\n")
    names += git("diff", "--name-only", base, head, cwd=repo).split("\n")
    files = sorted({n.strip() for n in names
                    if n.strip().startswith(TEST_ROOT) and n.strip().endswith(".cs")})
    return revisions, files


def detect(repo: Path, base: str, head: str) -> tuple[list[RemovedTest], list[WeakenedTest]]:
    revisions, files = history(repo, base, head)

    def tests_at(sha: str) -> list[TestMethod]:
        out: list[TestMethod] = []
        for f in files:
            if _exists(repo, sha, f):
                out += parse_tests(f, git("show", f"{sha}:{f}", cwd=repo))
        return out

    return classify_removals([(sha, tests_at(sha)) for sha in revisions], tests_at(head), base)


# Receipt --------------------------------------------------------------------------------------


def orphaned_lines(changed: dict[str, set[int]], before_hits: dict[str, dict[int, int]],
                   head_hits: dict[str, dict[int, int]]) -> dict[str, list[int]]:
    """Changed lines the removed test executed and no remaining test executes on head."""
    out: dict[str, list[int]] = {}
    for f, lines in changed.items():
        b, h = before_hits.get(f, {}), head_hits.get(f, {})
        hit = sorted(n for n in lines if b.get(n, 0) > 0 and h.get(n, 0) == 0)
        if hit:
            out[f] = hit
    return out


BranchKey = tuple[int, int, int, int, int]  # line, IL offset, end offset, path, ordinal


def orphaned_branches(changed: dict[str, set[int]], before: dict[str, dict[BranchKey, int]],
                      head: dict[str, dict[BranchKey, int]]) -> dict[str, list[BranchKey]]:
    """Branch outcomes on changed lines the removed test took and no remaining test takes on head.
    Only outcomes head's report also lists (same file, same IL) count."""
    out: dict[str, list[BranchKey]] = {}
    for f, lines in changed.items():
        b, h = before.get(f, {}), head.get(f, {})
        hit = sorted(k for k, n in b.items() if k[0] in lines and n > 0 and h.get(k) == 0)
        if hit:
            out[f] = hit
    return out


def parse_coverlet_json(path: Path, root: Path) -> dict[str, dict[BranchKey, int]]:
    """coverlet's json report -> {repo-relative file: {branch key: hits}}."""
    import json

    out: dict[str, dict[BranchKey, int]] = {}
    prefix = str(root.resolve()).rstrip("/") + "/"
    for files in json.loads(path.read_text()).values():
        for fname, classes in files.items():
            rel = str(Path(fname).resolve()).replace("\\", "/").removeprefix(prefix)
            per = out.setdefault(rel, {})
            for methods in classes.values():
                for m in methods.values():
                    for br in m.get("Branches", []):
                        k = (br["Line"], br["Offset"], br["EndOffset"], br["Path"], br["Ordinal"])
                        per[k] = max(per.get(k, 0), br["Hits"])
    return out


_REMOVAL_WORDS = re.compile(r"\b(remov\w*|delet\w*|replac\w*|obsolete)\b", re.I)
_TEST_WORD = re.compile(r"\btests?\b", re.I)


def ticket_asks_for_test_removal(text: str) -> bool:
    """A sentence of the requirement names tests together with remove/delete/replace/obsolete."""
    return any(_REMOVAL_WORDS.search(s) and _TEST_WORD.search(s)
               for s in re.split(r"(?<=[.!?])\s+|\n\s*\n", text or ""))


def changed_source_lines(ctx) -> dict[str, set[int]]:
    out: dict[str, set[int]] = {}
    for h in ctx.intake.hunks:
        if h.kind == "source" and h.file.endswith(".cs"):
            out.setdefault(h.file, set()).update(changed_lines(h)[0])
    return out


def _coverage_run(sha: str, rid: str, flt: str | None, run_dir: Path,
                  commands: list[CommandReceipt]) -> tuple[dict | None, dict, TestRun | None, str]:
    """dotnet test with coverlet (added to a private worktree's csproj for this run only):
    (line hits, branch hits, test run, why no report)."""
    wt = sandbox.worktree("cov", sha, name=f"cov-{sha[:10]}")
    results = run_dir / "logs" / f"{rid}-results"
    trx = f"{rid}.trx"
    with sandbox.lock(wt):
        try:
            proj = wt / config.TEST_PROJECT
            text = proj.read_text()
            if "coverlet.collector" not in text:
                if COVERLET_ANCHOR not in text:
                    return None, {}, None, "could not add coverlet.collector to the test project"
                proj.write_text(text.replace(COVERLET_ANCHOR,
                                             f"{COVERLET_ANCHOR}\n    {COVERLET_REF}", 1))
            cmd = [config.DOTNET, "test", config.TEST_PROJECT, "-f", "net8.0",
                   "--collect:XPlat Code Coverage", "--results-directory", str(results),
                   "--logger", f"trx;LogFileName={trx}"]
            if flt:
                cmd += ["--filter", flt]
            cmd += ["--", f"{COVERLET_FORMAT_SETTING}=cobertura,json"]
            r = sandbox.run_cmd(rid, cmd, wt, run_dir, sandbox.TEST_TIMEOUT_S)
            commands.append(r)
        finally:
            sandbox.reset(wt)
    run = sandbox.parse_trx(results / trx, rid, "head", flt or "") if (results / trx).exists() \
        else None
    reports = sorted(results.glob("*/coverage.cobertura.xml"))
    hits = parse_cobertura(reports[0]) if reports else None
    js = sorted(results.glob("*/coverage.json"))
    branches = parse_coverlet_json(js[0], wt) if js else {}
    shutil.rmtree(results, ignore_errors=True)
    if hits is None:
        return None, {}, run, f"coverlet wrote no report ({rid} exit {r.exit_code})"
    return hits, branches, run, ""


def _members(ctx, file: str, lines: list[int]) -> list[str]:
    from .impact import short_name

    names = []
    for m in (ctx.impact or {}).get("members", []):
        span = m.get("head_span")
        if m["file"] == file and span and any(span[0] <= n <= span[1] for n in lines):
            names.append(short_name(m))
    return list(dict.fromkeys(names))


def _where(orphans: dict[str, list[int]]) -> str:
    return "; ".join(f"{f}:{', '.join(str(n) for n in ns)}" for f, ns in orphans.items())


def _same_file(repo: Path, a: str, b: str, path: str) -> bool:
    return (git("rev-parse", f"{a}:{path}", cwd=repo, check=False).strip()
            == git("rev-parse", f"{b}:{path}", cwd=repo, check=False).strip())


def _deleted_hunch(n: int, r: RemovedTest) -> Finding:
    t = r.test
    return Finding(
        id=f"deleted-test-{n}", lens="test-quality",
        claim=f"this change deletes the test {t.key} ({t.file}:{t.line} at {r.before_sha[:10]})",
        location=f"{t.file}:{t.line}", severity="medium",
        receipt_plan=ReceiptPlan(kind="code-citation", detail=f"FullyQualifiedName~{t.key}"))


def run(ctx) -> tuple[list[Evidence], list[Finding], list[NotChecked], bool]:
    """Stage entry: (evidence, findings incl. hunches, gaps, ran)."""
    repo = config.MOCK_REPO
    removed, weakened = detect(repo, ctx.base_sha, ctx.head_sha)
    findings: list[Finding] = [Finding(
        id=f"weakened-test-{n}", lens="test-quality",
        claim=(f"test {w.test.key} keeps fewer assertions on head ({w.head_count}) than at "
               f"{w.before_sha[:10]} ({w.before_count}); no receipt shows what it no longer checks"),
        location=f"{w.test.file}:{w.test.line}", severity="medium",
        receipt_plan=ReceiptPlan(kind="code-citation", detail="assertion count drop"))
        for n, w in enumerate(weakened, start=1)]
    if not removed:
        return [], findings, [], True
    gaps: list[NotChecked] = []
    unchecked = removed[MAX_REMOVED_CHECKED:]
    if unchecked:
        gaps.append(NotChecked(
            what=f"coverage receipt for {len(unchecked)} deleted test(s)",
            why=f"only the first {MAX_REMOVED_CHECKED} deleted tests get a coverage run"))
    if not ctx.head_build_ok:
        gaps.append(NotChecked(what="coverage receipt for deleted tests", why="head does not build"))
        unchecked = removed
    numbered = list(enumerate(removed, start=1))
    findings += [_deleted_hunch(n, r) for n, r in numbered if r in unchecked]
    checked = [(n, r) for n, r in numbered if r not in unchecked]
    if not checked:
        return [], findings, gaps, True
    asks = ticket_asks_for_test_removal(getattr(ctx, "requirement_text", ""))
    changed = changed_source_lines(ctx)
    head_hits, head_br, _, head_why = _coverage_run(
        ctx.head_sha, "test-removal-head-coverage", None, ctx.run_dir, ctx.commands)
    evidence: list[Evidence] = []
    for n, r in checked:
        t = r.test
        eid, rid = f"test-removal-{n}", f"test-removal-{n}-before"
        where_test = f"{t.file}:{t.line} at {r.before_sha[:10]}"
        flt = f"FullyQualifiedName~{t.key}"
        before_hits, before_br, trun, why = _coverage_run(r.before_sha, rid, flt, ctx.run_dir,
                                                          ctx.commands)
        ids = [c for c in (rid, "test-removal-head-coverage")
               if any(x.id == c for x in ctx.commands)]
        if trun is not None:
            ctx.tests.append(trun)
        passed = bool(trun and trun.compiled and trun.passed and not trun.failed)
        same = {f: ls for f, ls in changed.items() if _same_file(repo, r.before_sha, ctx.head_sha, f)}
        details = {"test": t.key, "file": t.file, "line": t.line, "before_sha": r.before_sha,
                   "existed_on_base": r.on_base, "filter": flt, "deleted_test_passed": passed,
                   "files_compared": sorted(same),
                   "files_skipped_changed_since": sorted(set(changed) - set(same))}
        hunch = _deleted_hunch(n, r)
        if not passed:
            reason = why or "the deleted test did not run and pass at its last revision"
            evidence.append(Evidence(id=eid, kind="test-removal", result="error",
                                     title=f"Deleted test {t.key}: no receipt ({reason})",
                                     command_ids=ids, details=details))
            findings.append(hunch.model_copy(update={"claim": hunch.claim + f"; not verified: {reason}"}))
            continue
        lines: dict[str, list[int]] = {}
        branches: dict[str, list[BranchKey]] = {}
        if before_hits is not None and head_hits is not None:
            lines = orphaned_lines(same, before_hits, head_hits)
            branches = orphaned_branches(same, before_br, head_br)
            covered = {f: sorted(n for n in ls if before_hits.get(f, {}).get(n, 0) > 0)
                       for f, ls in same.items()}
            details["changed_lines_covered_by_deleted_test"] = {f: v for f, v in covered.items()
                                                                if v}
        else:
            details["coverage_unavailable"] = why or head_why
        details["orphaned_lines"] = lines
        details["orphaned_branches"] = {f: [dict(zip(("line", "offset", "end_offset", "path",
                                                       "ordinal"), k)) for k in ks]
                                        for f, ks in branches.items()}
        details["branch_report"] = bool(head_br and before_br)
        details["ticket_asks_for_test_removal"] = asks
        if asks:
            evidence.append(Evidence(
                id=eid, kind="test-removal", result="partial", command_ids=ids, details=details,
                title=f"Deleted test {t.key} passed at its last revision; the ticket asks for "
                      "test removal or replacement, so this may be intended"))
            findings.append(hunch.model_copy(update={
                "claim": hunch.claim + "; the ticket mentions removing or replacing tests"}))
            continue
        excerpt = f"{rid}: {trun.passed}/{trun.total} passed at {r.before_sha[:10]}"
        detail = f"{t.key} passed at {r.before_sha[:10]} ({rid}) and is absent on head"
        if not lines and not branches:
            no_gap = ("every changed line and branch it executed is still executed by a remaining "
                      "test on head" if "coverage_unavailable" not in details
                      else f"coverage not compared ({details['coverage_unavailable']})")
            evidence.append(Evidence(
                id=eid, kind="test-removal", result="fail", command_ids=ids, details=details,
                title=f"Deleted passing test {t.key}; {no_gap}", excerpt=excerpt))
            findings.append(Finding(
                id=f"deleted-test-{n}", lens="test-quality",
                claim=(f"This change deletes a passing test ({t.key}, {where_test}) that the "
                       "ticket doesn't ask to remove. Other tests still run its lines, but "
                       "nothing asserts what it asserted."),
                location=f"{t.file}:{t.line}", severity="medium",
                receipt_plan=ReceiptPlan(kind="code-citation", detail=detail),
                verified=True, evidence_ids=[eid]))
            continue
        spots = {f: sorted(set(lines.get(f, [])) | {k[0] for k in branches.get(f, [])})
                 for f in {*lines, *branches}}
        members = list(dict.fromkeys(m for f, ns in spots.items() for m in _members(ctx, f, ns)))
        what = ", ".join(members) if members else _where(spots)
        gap = []
        if lines:
            gap.append(f"lines {_where(lines)} are executed by no remaining test on head")
        if branches:
            gap.append(f"{sum(map(len, branches.values()))} branch outcome(s) at "
                       f"{_where({f: sorted({k[0] for k in ks}) for f, ks in branches.items()})} "
                       "are taken by no remaining test on head")
        evidence.append(Evidence(
            id=eid, kind="test-removal", result="fail", command_ids=ids, details=details,
            title=f"Deleted passing test {t.key}: " + "; ".join(gap),
            excerpt=excerpt + f"; head coverage: full {Path(config.TEST_PROJECT).parent}"))
        findings.append(Finding(
            id=f"deleted-test-{n}", lens="test-quality",
            claim=(f"This change deletes the passing test that covered {what}: {t.key} "
                   f"({where_test}), and the ticket doesn't ask to remove it. Nothing else tests "
                   f"it now: {'; '.join(gap)}."),
            location=f"{t.file}:{t.line}", severity="high",
            receipt_plan=ReceiptPlan(kind="code-citation",
                                     detail=detail + "; coverage vs the full suite on head"),
            verified=True, evidence_ids=[eid]))
    return evidence, findings, gaps, True
