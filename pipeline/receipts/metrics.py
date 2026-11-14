"""`receipts metrics`: bench runs + the four sample runs -> metrics/T1..T6.json, summary, trust (SPEC 7, 5.9).

Inputs: bench/manifest.json (ground truth, never shown to the reviewer), runs/<manifest id>/<ts>/ with a
bench.json ({mode, seq, manifest_id}) written by `receipts bench run`, and runs/ju26-issue-*/latest.
Every table is the dashboard shape {columns, rows, note}; definitions live in each note.
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path

from . import config, oracle

NON_PLANTED = {"clean", "control", "bait"}
LENSES = ["requirement-tests", "logic-edge", "compatibility", "security", "product-context",
          "test-quality"]
BUILD_TESTS = "receipts (build/tests)"
VERDICT_ONLY = "verdict only (finding elsewhere)"
CHECKS = ["fail-before-pass-after", "mutation", "byte-diff"]
CHECK_SCOPE = {  # check row -> expected lenses that make a planted case in scope for it
    "fail-before-pass-after": {"requirement-tests", "logic-edge"},
    "mutation": {"test-quality"},
    "byte-diff": {"compatibility"},
}
EVIDENCE_CHECK = {"fail-before-pass-after": "fail-before-pass-after",
                  "new-api": "fail-before-pass-after", "mutation": "mutation",
                  "byte-diff": "byte-diff"}
LINE_TOLERANCE = 10
ROUTE_DEPENDENT_LENSES = {"logic-edge", "security"}  # lenses/scope.py risky_code_scope
DEEP_LENS_MODELS = {"logic-edge": config.MODEL_DEEP, "security": config.MODEL_LENS}
CODE_KINDS = {"source", "template"}
ROUTERS = ["jev", "llm", "rule", "none"]
TRUST_START, TRUST_MAX, TRUST_STREAK = 1, 3, 5
MIN_PER_100_LINES = 2.0  # reading speed assumption for T4
SKIM_FACTOR = 0.5  # light-routed files are skimmed at half the reading time
ORACLE_NOTE = (f"Cold->warm answers come from `{oracle.ORACLE}`: a script that answers from the "
               "bench ground truth (bait conventions, else the option closest to the requirement). "
               "It is not a person.")


# Loading -------------------------------------------------------------------------------------


@dataclass
class Run:
    change_id: str
    dir: Path
    receipt: dict
    mode: str = ""
    seq: int = 0
    case: dict | None = None
    findings_all: list = field(default_factory=list)
    evidence: list = field(default_factory=list)
    questions: list = field(default_factory=list)
    intake: dict | None = None
    routing: dict | None = None
    lenses: dict | None = None

    @property
    def verified(self) -> list[dict]:
        return self.receipt.get("findings", [])

    @property
    def hunches(self) -> list[dict]:
        return self.receipt.get("hunches", [])

    @property
    def verdict(self) -> str:
        return self.receipt.get("verdict", "")


def _read(path: Path, default=None):
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return default


def load_run(run_dir: Path, case: dict | None = None) -> Run | None:
    receipt = _read(run_dir / "receipt.json")
    if not isinstance(receipt, dict):
        return None
    meta = _read(run_dir / "bench.json", {}) or {}
    return Run(change_id=receipt.get("change_id", run_dir.parent.name), dir=run_dir,
               receipt=receipt, mode=meta.get("mode", ""), seq=int(meta.get("seq", 0) or 0),
               case=case, findings_all=_read(run_dir / "findings.json", []) or [],
               evidence=_read(run_dir / "evidence.json", []) or [],
               questions=_read(run_dir / "questions.json", []) or [],
               intake=_read(run_dir / "intake.json"), routing=_read(run_dir / "routing.json"),
               lenses=_read(run_dir / "lenses.json"))


def load_bench_runs(manifest: list[dict], runs_root: Path) -> dict[tuple[str, str], Run]:
    """(manifest id, mode) -> the newest run of that mode."""
    out: dict[tuple[str, str], Run] = {}
    for case in manifest:
        root = runs_root / case["id"]
        if not root.is_dir():
            continue
        for d in sorted(p for p in root.iterdir() if p.is_dir()):
            if not (d / "bench.json").exists():
                continue
            run = load_run(d, case)
            if run is not None and run.mode in ("cold", "warm"):
                out[(case["id"], run.mode)] = run  # sorted by ts: the last one wins
    return out


def load_sample_runs(runs_root: Path) -> list[Run]:
    out = []
    for branch in config.SAMPLE_BRANCHES:
        latest = runs_root / branch / "latest"
        if latest.exists():
            run = load_run(runs_root / branch / latest.read_text().strip())
            if run is not None:
                out.append(run)
    return out


# Classification ------------------------------------------------------------------------------


def parse_location(loc: str) -> tuple[str, int | None]:
    m = re.match(r"^(.*?):(\d+)", loc or "")
    if m:
        return m.group(1), int(m.group(2))
    return (loc or "").split(":", 1)[0], None


def planted_ranges(case: dict) -> dict[str, list[tuple[int, int]]]:
    out: dict[str, list[tuple[int, int]]] = {f: [] for f in case.get("files", [])}
    for spec in case.get("lines", []):
        m = re.match(r"^(.*?):(\d+)(?:-(\d+))?$", spec)
        if m:
            a = int(m.group(2))
            out.setdefault(m.group(1), []).append((a, int(m.group(3) or a)))
    return out


def hits_plant(finding: dict, case: dict) -> bool:
    path, line = parse_location(finding.get("location", ""))
    ranges = planted_ranges(case)
    if path not in ranges:
        return False
    if not ranges[path] or line is None:
        return True
    return any(a - LINE_TOLERANCE <= line <= b + LINE_TOLERANCE for a, b in ranges[path])


def is_planted(case: dict) -> bool:
    return case["variant"] not in NON_PLANTED


def _is_mutation(f: dict) -> bool:
    return "mutation" in f.get("evidence_ids", []) or f.get("receipt_plan", {}).get("kind") == "mutation"


def _finding_key(f: dict) -> tuple[str, ...]:
    """Identity for baseline comparison. Lens findings: lens + file (SPEC). Mutation survivors:
    also the mutant text, because one file usually carries both the agent change and the plant and
    line numbers shift between variants."""
    key = (f.get("lens", ""), parse_location(f.get("location", ""))[0])
    if _is_mutation(f):
        return (*key, f.get("receipt_plan", {}).get("detail", ""))
    return key


def finding_checks(f: dict, run: Run) -> set[str]:
    kinds = {e.get("id"): e.get("kind") for e in run.evidence}
    return {EVIDENCE_CHECK[kinds[i]] for i in f.get("evidence_ids", [])
            if kinds.get(i) in EVIDENCE_CHECK}


def _evidence(run: Run, eid: str) -> dict | None:
    return next((e for e in run.evidence if e.get("id") == eid), None)


def build_tests_red(run: Run) -> bool:
    """Head build failed, or an existing test failed on head that passed on base (the verdict's rule)."""
    if "head does not build" in run.receipt.get("verdict_reason", ""):
        return True
    return "pass on base but fail on head" in run.receipt.get("verdict_reason", "")


@dataclass
class CaseResult:
    run: Run
    planted: bool
    caught: bool = False
    rows: set[str] = field(default_factory=set)  # lenses / checks / pseudo-lenses credited
    matching: list[dict] = field(default_factory=list)  # verified findings at the planted location
    false_alarms: list[dict] = field(default_factory=list)
    baseline: list[dict] = field(default_factory=list)
    bait_false_alarms: list[dict] = field(default_factory=list)
    off_target: list[dict] = field(default_factory=list)  # planted case, elsewhere, not baseline


def classify(run: Run, clean: Run | None) -> CaseResult:
    case = run.case or {}
    res = CaseResult(run=run, planted=is_planted(case))
    base_keys = {_finding_key(f) for f in clean.verified} if clean else set()
    base_files = {_finding_key(f)[:2] for f in clean.verified} if clean else set()
    if case.get("variant") == "clean":
        res.baseline = list(run.verified)
        return res

    def is_baseline(f: dict) -> bool:
        # Away from the plant, same lens+file as clean = about the agent's change. At the plant,
        # only an identical finding (same mutant text) is baseline.
        if _finding_key(f) in base_keys:
            return True
        if case.get("category") == "tests" and _is_mutation(f):
            return False  # a deleted test shows up as a new survivor anywhere in the file
        return _finding_key(f)[:2] in base_files and not hits_plant(f, case)

    for f in run.verified:
        if is_baseline(f):
            res.baseline.append(f)
        elif res.planted and (hits_plant(f, case)
                              or (case.get("category") == "tests" and _is_mutation(f))):
            res.matching.append(f)
        elif res.planted:
            res.off_target.append(f)
        else:
            res.false_alarms.append(f)
    if case.get("variant") == "bait":
        files = set(case.get("files", []))
        res.bait_false_alarms = [f for f in run.verified
                                 if parse_location(f.get("location", ""))[0] in files
                                 and not is_baseline(f)]
    if not res.planted:
        return res
    for f in res.matching:
        res.rows.add(f.get("lens", ""))
        res.rows |= finding_checks(f, run)
    if build_tests_red(run):
        res.rows.add(BUILD_TESTS)
    res.caught = run.verdict == "blocked" or bool(res.matching)
    if res.caught and not res.rows:
        res.rows.add(VERDICT_ONLY)
    return res


def classify_all(runs: dict[tuple[str, str], Run], mode: str) -> list[CaseResult]:
    out = []
    for (cid, m), run in sorted(runs.items(), key=lambda kv: kv[1].seq):
        if m != mode:
            continue
        clean = runs.get((f"mock-{run.case['issue']}-clean", mode)) or runs.get(
            (f"mock-{run.case['issue']}-clean", "cold"))
        out.append(classify(run, clean))
    return out


def primary_mode(runs: dict[tuple[str, str], Run]) -> str:
    return "cold" if any(m == "cold" for _, m in runs) else "warm"


# Tables --------------------------------------------------------------------------------------


def _ratio(n: int, d: int) -> float | None:
    return round(n / d, 3) if d else None


def _in_scope(row: str, case: dict) -> bool:
    expected = set(case.get("expected_lens", []))
    if row == BUILD_TESTS:
        return True
    if row in CHECK_SCOPE:
        return bool(expected & CHECK_SCOPE[row])
    return row in expected


def _row_fa(row: str, r: CaseResult) -> int:
    if row == BUILD_TESTS:
        return int(not r.planted and r.run.case["variant"] != "clean" and build_tests_red(r.run))
    if row in CHECKS:
        return sum(row in finding_checks(f, r.run) for f in r.false_alarms)
    return sum(f.get("lens") == row for f in r.false_alarms)


def _row_tp(row: str, r: CaseResult) -> int:
    if row == BUILD_TESTS:
        return int(row in r.rows)
    if row in CHECKS:
        return sum(row in finding_checks(f, r.run) for f in r.matching)
    return sum(f.get("lens") == row for f in r.matching)


def table_t1(results: list[CaseResult], mode: str) -> dict:
    rows = []
    for row in [*LENSES, BUILD_TESTS, *CHECKS]:
        scope = [r for r in results if r.planted and _in_scope(row, r.run.case)]
        caught = sum(row in r.rows for r in scope)
        outside = sum(row in r.rows for r in results if r.planted and r not in scope)
        fa = sum(_row_fa(row, r) for r in results)
        tp = sum(_row_tp(row, r) for r in results)
        kind = "check" if row in CHECKS else "pseudo-lens" if row == BUILD_TESTS else "lens"
        rows.append([row, kind, len(scope), caught, len(scope) - caught, outside, fa,
                     _ratio(caught, len(scope)), _ratio(tp, tp + fa)])
    return {
        "columns": ["lens", "kind", "planted_in_scope", "caught", "missed", "caught_outside_scope",
                    "false_alarms", "recall", "precision"],
        "rows": rows,
        "note": (f"{mode} run, {len(results)} bench cases. A planted case is caught when the verdict "
                 "is blocked or a verified finding sits in a planted file (within "
                 f"±{LINE_TOLERANCE} lines of the manifest range when lines are given); the catch is "
                 "credited to that finding's lens and to the check that verified it (mutation, "
                 "byte-diff, fail-before-pass-after). An existing test that passed on base and fails "
                 f"on head, or a head build failure, is credited to '{BUILD_TESTS}'. In scope = the "
                 "manifest's expected_lens for the plant (every planted case for build/tests). For "
                 "missing-test plants (the plant is a deleted test) a new surviving mutant anywhere "
                 "counts as the catch. Baseline finding = same lens+file as a verified finding on the "
                 "clean run of the same issue, outside the planted lines; at the planted lines only "
                 "an identical finding (mutation survivors: same mutant text) is baseline. Baseline "
                 "findings are never catches or false alarms (reported in T2). False alarm = a "
                 "non-baseline verified finding on a control or bait case. Test-quality "
                 "(mutation) findings are medium severity: they point at the line but do not block. "
                 "Precision = matching findings / (matching findings + false alarms)."),
    }


def _verdicts(results: list[CaseResult]) -> str:
    counts: dict[str, int] = {}
    for r in results:
        counts[r.run.verdict] = counts.get(r.run.verdict, 0) + 1
    return ", ".join(f"{k} {v}" for k, v in sorted(counts.items()))


def _caught_by(results: list[CaseResult]) -> str:
    counts: dict[str, int] = {}
    for r in results:
        for row in r.rows:
            counts[row] = counts.get(row, 0) + 1
    return ", ".join(f"{k} {v}" for k, v in sorted(counts.items(), key=lambda kv: (-kv[1], kv[0])))


def table_t2(by_mode: dict[str, list[CaseResult]], variants: list[str]) -> dict:
    rows = []
    for mode, results in by_mode.items():
        for v in variants:
            rs = [r for r in results if r.run.case["variant"] == v]
            if not rs:
                continue
            planted = rs[0].planted
            rows.append([v, rs[0].run.case["category"], mode, len(rs),
                         sum(r.caught for r in rs) if planted else None,
                         _caught_by(rs) if planted else "",
                         _verdicts(rs), sum(len(r.false_alarms) for r in rs),
                         sum(len(r.baseline) for r in rs),
                         sum(len(r.off_target) for r in rs)])
    return {
        "columns": ["variant", "category", "mode", "cases", "caught", "caught_by", "verdicts",
                    "false_alarms", "baseline_findings", "off_target_findings"],
        "rows": rows,
        "note": ("Per plant variant, cold and warm. caught_by counts cases per credited lens/check "
                 "(a case can credit several). baseline_findings = verified findings whose lens+file "
                 "also appear on the clean run: real findings about the agent's original change, "
                 "not false alarms. off_target_findings = verified findings on a planted case "
                 "outside the planted location that are not baseline (neither a catch nor counted "
                 "as a false alarm). " + ORACLE_NOTE),
    }


def table_t4(runs: list[Run]) -> dict:
    rows = []
    for run in runs:
        files = (run.intake or {}).get("files", [])
        depth = run.receipt.get("routing", {})
        lines = {f["path"]: f.get("additions", 0) + f.get("deletions", 0) for f in files}
        deep = [p for p, d in depth.items() if d == "deep"]
        light = [p for p, d in depth.items() if d == "light"]
        gen = [p for p, d in depth.items() if d == "regeneration"]
        regen = _evidence(run, "regeneration")
        gen_verified = (regen or {}).get("details", {}).get("covered", 0) if regen else 0
        total_lines = sum(lines.values())
        baseline = total_lines * MIN_PER_100_LINES / 100
        with_receipts = (sum(lines.get(p, 0) for p in deep)
                         + SKIM_FACTOR * sum(lines.get(p, 0) for p in light)) * MIN_PER_100_LINES / 100
        rows.append([run.change_id, len(depth), len(deep) + len(light), len(deep), len(light),
                     len(gen), gen_verified, total_lines, round(baseline, 1),
                     round(with_receipts, 1), round(baseline - with_receipts, 1)])
    return {
        "columns": ["change_id", "files_total", "files_to_human", "deep", "light",
                    "generated", "generated_verified_by_regeneration", "changed_lines",
                    "minutes_read_all", "minutes_with_receipts", "minutes_saved"],
        "rows": rows,
        "note": (f"Assumption: reading a diff costs {MIN_PER_100_LINES:g} minutes per 100 changed "
                 f"lines. With receipts, deep files are read fully, light files skimmed at "
                 f"{SKIM_FACTOR:g}x, generated files not read (verified as a block by the "
                 "regeneration check). files_to_human = deep + light (every non-generated file). "
                 "Latest run per change: the four sample changes, then the bench."),
    }


def table_t5(by_mode: dict[str, list[CaseResult]], ledger: list[dict]) -> dict:
    rows = []
    for mode in ("cold", "warm"):
        for r in by_mode.get(mode, []):
            qs = r.run.questions
            repeats = 0
            if mode == "warm":
                prior = [(e.get("area"), e.get("question", "")) for e in ledger
                         if e.get("answered_by") == oracle.ORACLE]
                from .questions import same_question

                repeats = sum(any(a == q.get("area") and same_question(t, q.get("text", ""))
                                  for a, t in prior) for q in qs)
            citations = sum(f.get("receipt_plan", {}).get("kind") == "ledger-citation"
                            for f in r.run.findings_all)
            citations += len((r.run.lenses or {}).get("citations", []))
            rows.append([r.run.seq, r.run.change_id, mode, len(qs), len(r.bait_false_alarms),
                         citations, repeats])
    return {
        "columns": ["seq", "change_id", "mode", "questions", "bait_false_alarms",
                    "ledger_citations", "repeat_questions"],
        "rows": rows,
        "note": ("Cold = ledger emptied before the run (real entries backed up to "
                 "context/ledger.cold-backup.jsonl and restored after). After each cold case the "
                 "oracle answers its questions into the ledger; warm re-runs every case with that "
                 "ledger. repeat_questions = warm questions whose area and wording match an "
                 "answered one (should be 0). bait_false_alarms = non-baseline verified findings on "
                 "the bait case's planted file. " + ORACLE_NOTE),
    }


def table_t6(runs: list[Run]) -> dict:
    rows = []
    for lens in [*LENSES, "all"]:
        shown = executed = withheld = cites = 0
        for run in runs:
            ev_ok = {e.get("id") for e in run.evidence if e.get("command_ids")}
            vs = [f for f in run.verified if lens == "all" or f.get("lens") == lens]
            shown += len(vs)
            executed += sum(any(i in ev_ok for i in f.get("evidence_ids", [])) for f in vs)
            withheld += sum(lens == "all" or h.get("lens") == lens for h in run.hunches)
            cites += sum((lens == "all" or f.get("lens") == lens)
                         and f.get("receipt_plan", {}).get("kind") == "ledger-citation"
                         for f in run.findings_all)
        rows.append([lens, shown, executed, withheld, cites])
    return {
        "columns": ["lens", "findings_shown", "with_executed_receipt", "hunches_withheld",
                    "ledger_citations"],
        "rows": rows,
        "note": ("Over the latest run of every change (sample changes + bench). findings_shown = "
                 "verified findings in receipt.json; with_executed_receipt = those citing evidence "
                 "backed by at least one executed command; hunches_withheld = unverified findings "
                 "shown only as hunches."),
    }


# Routing (T3) --------------------------------------------------------------------------------


def _hunk_range(h) -> tuple[int, int]:
    return h.new_start, h.new_start + max(h.new_lines, 1) - 1


def planted_hunks(hunks, case: dict) -> list:
    ranges = planted_ranges(case)
    out = []
    for h in hunks:
        if h.file not in ranges or h.kind == "generated":
            continue
        if not ranges[h.file]:
            out.append(h)
            continue
        a, b = _hunk_range(h)
        if any(a <= hi + LINE_TOLERANCE and b >= lo - LINE_TOLERANCE for lo, hi in ranges[h.file]):
            out.append(h)
    return out


def _risk_at(f: dict, hunks, risk: dict[str, int]) -> int:
    path, line = parse_location(f.get("location", ""))
    hs = [h for h in hunks if h.file == path]
    if line is not None:
        inside = [h for h in hs if _hunk_range(h)[0] <= line <= _hunk_range(h)[1]]
        hs = inside or hs
    return max((risk.get(h.id, 1) for h in hs), default=1)


def survives(r: CaseResult, hunks, risk: dict[str, int]) -> bool:
    """Would this catch survive if the router's low scores skipped the route-dependent checks?"""
    if not r.caught:
        return False
    if r.rows & {BUILD_TESTS, VERDICT_ONLY}:
        return True
    for f in r.matching:
        dependent = f.get("lens") in ROUTE_DEPENDENT_LENSES or "mutation" in f.get("evidence_ids", [])
        threshold = (config.MUTATION_RISK_THRESHOLD if "mutation" in f.get("evidence_ids", [])
                     else config.ROUTER_DEEP_THRESHOLD)
        if not dependent or _risk_at(f, hunks, risk) >= threshold:
            return True
    return False


def _deep_lens_usd(hunks, risk: dict[str, int]) -> float:
    from .llm import PRICES_PER_MTOK

    chars = sum(len(h.text) for h in hunks
                if h.kind in CODE_KINDS and risk.get(h.id, 1) >= config.ROUTER_DEEP_THRESHOLD)
    return sum(chars / 4 * PRICES_PER_MTOK[m][0] / 1e6 for m in DEEP_LENS_MODELS.values())


def route_case(run: Run, router: str):
    from .models import IntakeReport
    from .review import load_requirement
    from .routers.combine import route_all

    intake = IntakeReport.model_validate(run.intake)
    _, req_text, _ = load_requirement(intake.requirement_id)
    rep = route_all(intake.hunks, req_text, {"files": intake.files}, router, files=intake.files)
    return intake, rep


def table_t3(results: list[CaseResult]) -> dict:
    rows, errors = [], []
    for router in ROUTERS:
        acc = {"hunks": 0, "tokens": 0, "usd": 0.0, "ms": 0, "planted": 0, "low": 0, "deep": 0,
               "caught": 0, "planted_cases": 0, "lens_usd": 0.0}
        label = router
        misses: list[str] = []
        for r in results:
            if not r.run.intake:
                continue
            try:
                intake, rep = route_case(r.run, router)
            except Exception as e:  # noqa: BLE001 - one router failing must not drop the table
                errors.append(f"{router} on {r.run.change_id}: {type(e).__name__}: {e}")
                continue
            label = rep.router
            risk = {x.hunk_id: x.risk for x in rep.routes}
            scored = [h for h in intake.hunks if h.kind != "generated"]
            acc["hunks"] += len(scored)
            acc["tokens"] += rep.total_tokens
            acc["usd"] += rep.cost_usd
            acc["ms"] += sum(x.ms for x in rep.routes)
            acc["deep"] += sum(risk.get(h.id, 1) >= config.ROUTER_DEEP_THRESHOLD for h in scored)
            acc["lens_usd"] += _deep_lens_usd(intake.hunks, risk)
            if r.planted:
                ph = planted_hunks(intake.hunks, r.run.case)
                low = [h for h in ph if risk.get(h.id, 1) < config.ROUTER_DEEP_THRESHOLD]
                acc["planted"] += len(ph)
                acc["low"] += len(low)
                misses += [f"{r.run.change_id}:{h.id}" for h in low]
                acc["planted_cases"] += 1
                acc["caught"] += survives(r, intake.hunks, risk)
        rows.append([label, acc["hunks"], acc["deep"], acc["tokens"], round(acc["usd"], 6),
                     round(acc["ms"] / 1000, 2), acc["planted"], acc["low"],
                     _ratio(acc["caught"], acc["planted_cases"]), round(acc["lens_usd"], 4),
                     "; ".join(misses[:6])])
    note = (f"Only the routers ran here (routers.combine.route_all over each bench case's hunks); "
            f"seconds = recorded per-hunk latency (replayed fixtures keep the original ms). "
            f"planted_routed_low = planted hunks (manifest files/lines, ±{LINE_TOLERANCE}) scored "
            f"below the deep threshold {config.ROUTER_DEEP_THRESHOLD} after rule floors: router "
            "misses (SPEC 5.2: Jev recall is reported near 75%). end_to_end_recall approximates the "
            "pipeline's recall under that router: a catch is lost when every finding behind it came "
            "from a route-dependent check (logic-edge, security, mutation) on a hunk the router "
            "scored low; build/tests and route-independent lenses always run. It reuses the "
            "findings of the bench run, so it cannot show what a deeper route would add. "
            "deep_lens_usd_est = input-token estimate (chars/4) for the route-dependent lenses on "
            "the hunks routed deep; 'none' routes every non-generated hunk deep (full review).")
    if errors:
        note += " Errors: " + " | ".join(errors[:4])
    return {
        "columns": ["router", "hunks_scored", "hunks_deep", "tokens", "cost_usd", "seconds",
                    "planted_hunks", "planted_routed_low", "end_to_end_recall",
                    "deep_lens_usd_est", "router_misses"],
        "rows": rows,
        "note": note,
    }


# Trust (SPEC 5.9) ----------------------------------------------------------------------------


def correct_verdict(r: CaseResult) -> bool:
    if r.planted:
        return r.caught and r.run.verdict != "auto-ready"
    return r.run.verdict != "blocked"


def trust_history(results: list[CaseResult]) -> tuple[dict[str, int], list[dict]]:
    level: dict[str, int] = {}
    streak: dict[str, int] = {}
    history: list[dict] = []
    for r in sorted(results, key=lambda x: x.run.seq):
        escape = r.planted and r.run.verdict == "auto-ready"
        ok = correct_verdict(r)
        for area in r.run.receipt.get("areas", []):
            lv = level.setdefault(area, TRUST_START)
            if escape:
                level[area], streak[area], event = 0, 0, "escape: reset to 0"
            elif ok:
                streak[area] = streak.get(area, 0) + 1
                event = f"correct ({streak[area]}/{TRUST_STREAK})"
                if streak[area] >= TRUST_STREAK:
                    level[area], streak[area] = min(TRUST_MAX, lv + 1), 0
                    event += f": level {lv} -> {level[area]}"
            else:
                streak[area], event = 0, "incorrect: streak reset"
            history.append({"seq": r.run.seq, "change_id": r.run.change_id, "area": area,
                            "level": level[area], "event": event})
    return level, history


# Entry point ---------------------------------------------------------------------------------


def write_json(name: str, obj) -> Path:
    config.METRICS.mkdir(parents=True, exist_ok=True)
    p = config.METRICS / name
    p.write_text(json.dumps(obj, indent=2, ensure_ascii=False) + "\n")
    return p


def headline(by_mode: dict[str, list[CaseResult]], samples: list[Run], t3: dict) -> dict:
    out: dict = {"oracle": ORACLE_NOTE}
    for mode, rs in by_mode.items():
        planted = [r for r in rs if r.planted]
        neutral = [r for r in rs if not r.planted and r.run.case["variant"] != "clean"]
        out[mode] = {
            "cases": len(rs),
            "planted": len(planted),
            "caught": sum(r.caught for r in planted),
            "recall": _ratio(sum(r.caught for r in planted), len(planted)),
            "control_bait_cases": len(neutral),
            "false_alarm_cases": sum(bool(r.false_alarms) for r in neutral),
            "false_alarm_rate": _ratio(sum(bool(r.false_alarms) for r in neutral), len(neutral)),
            "bait_false_alarms": sum(len(r.bait_false_alarms) for r in rs),
            "questions": sum(len(r.run.questions) for r in rs),
            "questions_per_change": _ratio(sum(len(r.run.questions) for r in rs), len(rs)),
            "escapes": sum(r.planted and r.run.verdict == "auto-ready" for r in rs),
            "off_target_findings": sum(len(r.off_target) for r in rs),
            "lens_cost_usd": round(sum((r.run.lenses or {}).get("total_cost_usd", 0.0)
                                       for r in rs), 4),
            "router_cost_usd": round(sum((r.run.routing or {}).get("cost_usd", 0.0)
                                         for r in rs), 6),
        }
    s2297 = next((s for s in samples if s.change_id == "ju26-issue-2297"), None)
    if s2297 is not None:
        depth = s2297.receipt.get("routing", {})
        gen = sum(d == "regeneration" for d in depth.values())
        out["ju26-issue-2297_routed_away_from_humans"] = {
            "generated_files": gen, "files_total": len(depth),
            "share": _ratio(gen, len(depth))}
    cols = t3["columns"]
    out["routing"] = {row[0]: dict(zip(cols[1:], row[1:])) for row in t3["rows"]}
    return out


def run_metrics() -> int:
    from .bench import load_manifest

    manifest = load_manifest()
    runs = load_bench_runs(manifest, config.RUNS)
    samples = load_sample_runs(config.RUNS)
    if not runs:
        print(f"metrics: no bench runs with bench.json under {config.RUNS}; run `receipts bench run`")
        return 1
    by_mode = {m: classify_all(runs, m) for m in ("cold", "warm")
               if any(k[1] == m for k in runs)}
    primary = primary_mode(runs)
    variants = list(dict.fromkeys(c["variant"] for c in manifest))
    from .questions import load_ledger

    ledger = load_ledger()
    latest_bench = [r for r in (runs.get((c["id"], "warm")) or runs.get((c["id"], "cold"))
                                for c in manifest) if r is not None]
    t3 = table_t3(by_mode[primary])
    tables = {
        "T1.json": table_t1(by_mode[primary], primary),
        "T2.json": table_t2(by_mode, variants),
        "T3.json": t3,
        "T4.json": table_t4([*samples, *latest_bench]),
        "T5.json": table_t5(by_mode, ledger),
        "T6.json": table_t6([*samples, *latest_bench]),
    }
    for name, table in tables.items():
        write_json(name, table)
    levels, history = trust_history(by_mode[primary])
    write_json("trust.json", levels)
    write_json("trust-history.json", history)
    summary = headline(by_mode, samples, t3)
    write_json("summary.json", summary)
    for mode in by_mode:
        s = summary[mode]
        print(f"metrics [{mode}]: {s['caught']}/{s['planted']} planted caught, "
              f"{s['false_alarm_cases']}/{s['control_bait_cases']} control/bait with false alarms, "
              f"{s['questions']} questions over {s['cases']} cases")
    print(f"metrics: wrote T1-T6, trust.json, trust-history.json, summary.json to {config.METRICS}")
    return 0
