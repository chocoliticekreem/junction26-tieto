"""[7] Verdict composer: builds the TrustReceipt from everything the run produced (SPEC 5.8).

Verdict, first match wins:
  blocked         a verified finding of severity high/critical, or head does not build,
                  or an existing test that passed on base fails on head
  decision-A-B    interpretations present (genuine ambiguity: the human picks A or B)
  auto-ready      max risk < 5, every command and evidence green, no high/critical hunch, no risky
                  area touched, every touched area at trust >= 3, and the AI lenses actually ran
  needs-sign-off  everything else
"""

import json
from dataclasses import dataclass, field

from . import config
from .models import (
    CheckedItem, CommandReceipt, Evidence, Finding, Interpretation, IntakeReport, NotChecked,
    Question, Refusal, RoutingReport, TestRun, TrustReceipt, now_iso,
)

RISKY_AREAS = {"public-api", "serializer-format", "serializer-runtime", "security"}
ALL_LENSES = ["requirement-tests", "logic-edge", "compatibility", "security", "product-context",
              "test-quality"]
AUTO_READY_MAX_RISK = 5
AUTO_READY_MIN_TRUST = 3
DEFAULT_TRUST = 1
GREEN = {"pass", "skipped"}
HIGH = {"high", "critical"}
# Measured in notes/sandbox.md.
KNOWN_UNBUILDABLE = {
    "tests/MessagePack.SourceGenerator.ExecutionTests": (
        "not run: it does not compile in this sandbox even on base (SDK 8.0.402 ships Roslyn 4.11; "
        "the source generator needs 4.12, CS9057, so no generated code exists); see notes/sandbox.md"
    ),
}


@dataclass
class VerdictInputs:
    intake: IntakeReport
    routing: RoutingReport
    requirement_title: str
    requirement_text: str
    run_ts: str
    commands: list[CommandReceipt] = field(default_factory=list)
    tests: list[TestRun] = field(default_factory=list)
    evidence: list[Evidence] = field(default_factory=list)
    findings: list[Finding] = field(default_factory=list)
    questions: list[Question] = field(default_factory=list)
    interpretations: list[Interpretation] = field(default_factory=list)
    refused: list[Refusal] = field(default_factory=list)
    not_checked: list[NotChecked] = field(default_factory=list)  # gaps declared upstream
    ran: list[str] = field(default_factory=list)  # lens / check names that executed
    skip_mutation: bool = False
    lens_unavailable_reason: str = ""


def load_trust() -> dict[str, int]:
    p = config.METRICS / "trust.json"
    if not p.exists():
        return {}
    try:
        data = json.loads(p.read_text())
        return {str(k): int(v) for k, v in data.items()}
    except (ValueError, TypeError, AttributeError):
        return {}


def touched_areas(intake: IntakeReport) -> list[str]:
    reviewed = {f.area for f in intake.files if f.kind not in ("generated", "docs")}
    return sorted(reviewed or {f.area for f in intake.files})


def _test_regressions(tests: list[TestRun]) -> list[str]:
    base = next((t for t in tests if t.side == "base" and t.receipt_id.endswith("existing")), None)
    head = next((t for t in tests if t.side == "head" and t.receipt_id.endswith("existing")), None)
    if not (base and head and base.compiled and head.compiled):
        return []
    passed_on_base = {c.name for c in base.cases if c.outcome == "passed"}
    return sorted(c.name for c in head.cases if c.outcome == "failed" and c.name in passed_on_base)


def _cmd(commands: list[CommandReceipt], cid: str) -> CommandReceipt | None:
    return next((c for c in commands if c.id == cid), None)


def _derived_not_checked(inp: VerdictInputs) -> list[NotChecked]:
    out: list[NotChecked] = []
    ran = set(inp.ran)
    mentioned = " ".join(n.what for n in inp.not_checked).lower()

    def add(what: str, why: str):
        if what.lower() not in mentioned:
            out.append(NotChecked(what=what, why=why))

    lens_why = inp.lens_unavailable_reason or "the lens stage did not run this lens in this build"
    for lens in ALL_LENSES:
        if lens not in ran:
            add(f"{lens} lens", lens_why)

    if "fail-before-pass-after" not in ran and "new-api" not in ran:
        add("requirement tests (fail on base, pass on head)",
            "no requirement-derived tests were generated and executed")
    if inp.skip_mutation:
        add("mutation testing", "skipped by --skip-mutation")
    elif "mutation" not in ran:
        deep = [p for p, d in inp.routing.depth_by_file.items() if d == "deep"]
        add("mutation testing",
            f"mutation check did not run ({len(deep)} file(s) routed deep would have been mutated)")
    gen = inp.intake.counts.get("generated", 0)
    if gen and "regeneration" not in ran:
        add(f"regeneration check of {gen} generated files",
            "no regeneration check mapped the generator snapshot tests to these files; the existing "
            "SourceGenerator tests ran on head (see existing-tests evidence) but are not counted as a "
            "per-file regeneration receipt")
    if "byte-diff" not in ran:
        add("compatibility byte diff (base vs head serialized bytes)",
            "compatibility harness did not run")

    head_build = _cmd(inp.commands, "build-head")
    if head_build is None:
        add("head build", "the head worktree was never built")
    elif head_build.exit_code != 0:
        add("existing tests on head", f"head build failed (exit {head_build.exit_code})")

    touched_projects = sorted({
        "/".join(f.path.split("/")[:2]) for f in inp.intake.files
        if f.path.startswith("tests/") and f.kind != "generated"
    } - {"/".join(config.TEST_PROJECT.split("/")[:2])})
    for proj in touched_projects:
        why = KNOWN_UNBUILDABLE.get(
            proj, f"not built or run: only {config.TEST_PROJECT.split('/')[1]} is in sandbox scope")
        add(f"{proj} (changed by this diff)", why)
    add("tests/MessagePack.Tests and other runtime test suites",
        "not built or run in the sandbox; only the source generator test project is in scope")
    add("NativeAOT, Unity and non-net8.0 target frameworks",
        "the sandbox builds and tests net8.0 only")
    return out


def _decide(inp: VerdictInputs, verified: list[Finding], hunches: list[Finding], risk: int,
            areas: list[str], trust: dict[str, int]) -> tuple[str, str]:
    blockers = [f for f in verified if f.severity in HIGH]
    if blockers:
        ids = ", ".join(f.id for f in blockers)
        return "blocked", f"verified {blockers[0].severity} finding(s) backed by receipts: {ids}"
    head_build = _cmd(inp.commands, "build-head")
    if head_build is not None and head_build.exit_code != 0:
        return "blocked", f"head does not build (receipt build-head, exit {head_build.exit_code})"
    regressions = _test_regressions(inp.tests)
    if regressions:
        return "blocked", (f"{len(regressions)} existing test(s) pass on base but fail on head: "
                           + ", ".join(regressions[:3]))
    if inp.interpretations:
        return "decision-A-B", (f"the requirement has {len(inp.interpretations)} plausible readings "
                                "that lead to different code; a named human must choose")

    reasons = []
    if risk >= AUTO_READY_MAX_RISK:
        reasons.append(f"max routed risk {risk} >= {AUTO_READY_MAX_RISK}")
    red_cmds = [c.id for c in inp.commands if c.exit_code != 0]
    red_ev = [e.id for e in inp.evidence if e.result not in GREEN]
    if red_cmds or red_ev:
        reasons.append("not every receipt is green (" + ", ".join((red_cmds + red_ev)[:4]) + ")")
    high_hunches = [h.id for h in hunches if h.severity in HIGH]
    if high_hunches:
        reasons.append(f"{len(high_hunches)} unverified high-severity hunch(es)")
    risky = sorted(set(areas) & RISKY_AREAS)
    if risky:
        reasons.append("risky area touched: " + ", ".join(risky))
    low_trust = sorted(a for a in areas if trust.get(a, DEFAULT_TRUST) < AUTO_READY_MIN_TRUST)
    if low_trust:
        reasons.append("area trust below 3: " + ", ".join(
            f"{a}={trust.get(a, DEFAULT_TRUST)}" for a in low_trust))
    if not any(lens in inp.ran for lens in ALL_LENSES):
        reasons.append("no AI lens ran")
    if reasons:
        return "needs-sign-off", "; ".join(reasons)
    return "auto-ready", "low risk, all receipts green, no risky area, area trust >= 3"


def _checked(inp: VerdictInputs) -> list[CheckedItem]:
    items: list[CheckedItem] = []
    test_by_receipt = {t.receipt_id: t for t in inp.tests}
    covered = {cid for e in inp.evidence for cid in e.command_ids}
    for c in inp.commands:
        if c.id in covered:
            continue
        t = test_by_receipt.get(c.id)
        if t is not None:
            if not t.compiled:
                result, what = "error", f"{c.id}: tests did not compile"
            else:
                result = "pass" if t.failed == 0 and c.exit_code == 0 else "fail"
                what = f"{c.id}: {t.passed}/{t.total} passed, {t.failed} failed ({t.side})"
        else:
            result = "pass" if c.exit_code == 0 else "fail"
            what = f"{c.id}: {' '.join(c.cmd[1:3])} exit {c.exit_code} in {c.duration_s}s"
        items.append(CheckedItem(what=what, by="sandbox", evidence_id=c.id, result=result))
    for e in inp.evidence:
        items.append(CheckedItem(what=e.title, by=e.kind, evidence_id=e.id, result=e.result))
    n = len(inp.routing.routes)
    items.append(CheckedItem(what=f"routed {n} hunks across {len(inp.intake.files)} files",
                             by=inp.routing.router, result="pass"))
    return items


def _residual(inp: VerdictInputs, verdict: str, risk: int, areas: list[str],
              hunches: list[Finding], not_checked: list[NotChecked]) -> str:
    c = inp.intake.counts
    depth = inp.routing.depth_by_file
    deep = sum(d == "deep" for d in depth.values())
    light = sum(d == "light" for d in depth.values())
    regen = sum(d == "regeneration" for d in depth.values())
    head = next((t for t in inp.tests if t.side == "head" and t.receipt_id.endswith("existing")),
                None)
    parts = [
        (f"This change touches {c.get('total', 0)} files ({c.get('generated', 0)} generated) in "
         f"{', '.join(areas) or 'no mapped area'}; the highest routed risk is {risk}/10."),
        (f"{deep} file(s) were routed for deep review, {light} for light review and {regen} to "
         "the regeneration check."),
    ]
    if head and head.compiled:
        parts.append(f"The existing source generator tests ran on head: {head.passed} of "
                     f"{head.total} passed, {head.failed} failed.")
    else:
        parts.append("No existing tests ran successfully on head.")
    if hunches:
        parts.append(f"{len(hunches)} unverified hunch(es) remain without an executed receipt.")
    risky = sorted(set(areas) & RISKY_AREAS)
    if risky:
        parts.append(f"It touches risky areas ({', '.join(risky)}), so a human must sign off.")
    parts.append(f"{len(not_checked)} thing(s) were not checked, including "
                 + "; ".join(n.what for n in not_checked[:3])
                 + ". By signing off, the signer accepts any defect that only those unchecked "
                   "paths would have revealed.")
    if verdict == "blocked":
        parts.append("The change is blocked; signing off is not offered until the blocker is fixed.")
    return " ".join(parts)


def compose(inp: VerdictInputs) -> TrustReceipt:
    verified = [f for f in inp.findings if f.verified and f.evidence_ids]
    hunches = [f for f in inp.findings if not (f.verified and f.evidence_ids)]
    risk = max((r.risk for r in inp.routing.routes), default=1)
    areas = touched_areas(inp.intake)
    trust = load_trust()
    not_checked = [*inp.not_checked, *_derived_not_checked(inp)]
    verdict, reason = _decide(inp, verified, hunches, risk, areas, trust)
    return TrustReceipt(
        change_id=inp.intake.change_id,
        requirement_id=inp.intake.requirement_id,
        requirement_title=inp.requirement_title,
        requirement_text=inp.requirement_text,
        run_ts=inp.run_ts or now_iso(),
        head_sha=inp.intake.head_sha,
        verdict=verdict,
        verdict_reason=reason,
        risk=risk,
        areas=areas,
        area_trust={a: trust.get(a, DEFAULT_TRUST) for a in areas},
        checked=_checked(inp),
        findings=verified,
        hunches=hunches,
        refused=inp.refused,
        not_checked=not_checked,
        residual_risk=_residual(inp, verdict, risk, areas, hunches, not_checked),
        routing=inp.routing.depth_by_file,
        counts=inp.intake.counts,
        questions=[q.id for q in inp.questions],
        interpretations=inp.interpretations,
        mock_llm=config.MOCK_LLM,
    )
