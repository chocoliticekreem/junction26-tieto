"""Hook points between the sandbox and the verdict. Tracks C (lenses) and D (receipt checks) fill them.

Order inside review.run_review:
    intake -> routing -> worktrees -> build base/head -> existing tests head/base
    -> run_lenses(ctx)          [4]  returns StageResult; its findings land in ctx.findings
    -> run_receipt_checks(ctx)  [5]  may set verified=True + evidence_ids on ctx.findings
    -> verdict.compose(...)

Contract for a stage function `fn(ctx: ReviewContext) -> StageResult`:
  * Return new items in the StageResult; do not write runstore files yourself.
  * Every executed command: append its CommandReceipt to ctx.commands (use sandbox.run_cmd /
    sandbox.build / sandbox.test(commands=ctx.commands)); every TestRun to ctx.tests. Use ids that
    are unique within the run, e.g. "req-tests-base", "mutant-3-test".
  * Name what actually ran in StageResult.ran (lens names like "logic-edge", check names like
    "fail-before-pass-after", "mutation", "regeneration", "byte-diff"). The verdict lists anything
    not in `ran` under not_checked, so an honest omission costs nothing.
  * Put declared gaps in StageResult.not_checked with a concrete why ("LLM unavailable: ...").
  * Raising is allowed but discouraged: safe_run turns an exception into a not_checked entry and
    writes the traceback to logs/stage-<name>.log; the review never crashes.
  * Shared worktrees (ctx.base_wt / ctx.head_wt) must be left clean. For patch-build-test loops
    use a private worktree: sandbox.worktree("mut", ctx.head_sha, name=f"mut-{ctx.change_id}"),
    wrap the loop in `with sandbox.lock(wt):` and call sandbox.reset(wt) after each mutant.
"""

import re
import traceback
from dataclasses import dataclass, field
from pathlib import Path

from .models import (
    CommandReceipt, Evidence, Finding, Interpretation, IntakeReport, Mutant, NotChecked, Question,
    Refusal, RoutingReport, TestRun,
)


@dataclass
class ReviewContext:
    run_dir: Path
    change_id: str
    requirement_id: str
    requirement_title: str
    requirement_text: str
    requirement_missing: bool
    intake: IntakeReport
    routing: RoutingReport
    base_sha: str
    head_sha: str
    base_wt: Path | None
    head_wt: Path | None
    base_build_ok: bool
    head_build_ok: bool
    base_tests: TestRun | None  # existing SourceGenerator tests on base (None if not run)
    head_tests: TestRun | None  # existing SourceGenerator tests on head
    skip_mutation: bool
    commands: list[CommandReceipt] = field(default_factory=list)  # append every executed command
    tests: list[TestRun] = field(default_factory=list)  # append every TestRun
    findings: list[Finding] = field(default_factory=list)  # lens output, set before receipt checks
    # Tests a lens wrote, run by the receipt checks on base and head. run_lenses appends dicts:
    # {lens, path (relative to the test project dir), content, test_names,
    #  expects: "fail-on-base-pass-on-head" | "new-api" | "fail-on-head"}. See checks.py.
    generated_tests: list[dict] = field(default_factory=list)


@dataclass
class StageResult:
    findings: list[Finding] = field(default_factory=list)
    evidence: list[Evidence] = field(default_factory=list)
    questions: list[Question] = field(default_factory=list)
    not_checked: list[NotChecked] = field(default_factory=list)
    refused: list[Refusal] = field(default_factory=list)
    interpretations: list[Interpretation] = field(default_factory=list)
    mutants: list[Mutant] = field(default_factory=list)
    ran: list[str] = field(default_factory=list)


def _style_examples(ctx: ReviewContext) -> list[dict]:
    from .lenses import EXAMPLE_FILES

    out = []
    for rel in EXAMPLE_FILES:
        for wt in (ctx.head_wt, ctx.base_wt):
            if wt is not None and (wt / rel).exists():
                out.append({"path": rel, "content": (wt / rel).read_text()})
                break
    return out


def _tie_findings_to_tests(findings: list[Finding], tests: list) -> list[Finding]:
    """A finding whose receipt plan is a test from its own lens names that test in the plan detail,
    so the receipt checks can match it to the executed test run."""
    out = []
    for f in findings:
        own = [t for t in tests if t.lens == f.lens]
        if f.receipt_plan.kind != "test" or not own:
            out.append(f)
            continue
        text = f"{f.receipt_plan.detail} {f.location} {f.claim}"
        named = [t for t in own
                 if Path(t.path).stem in text or any(n.rsplit(".", 1)[-1] in text for n in t.test_names)]
        plans = sum(1 for g in findings if g.lens == f.lens and g.receipt_plan.kind == "test")
        tied = named or (own if len(own) == 1 and plans == 1 else [])
        names_any = any(n.rsplit(".", 1)[-1] in text for t in tied for n in t.test_names)
        names = [] if names_any else [n for t in tied for n in t.test_names]
        if names:
            detail = f"{f.receipt_plan.detail} (generated tests: {', '.join(names)})"
            f = f.model_copy(update={"receipt_plan": f.receipt_plan.model_copy(update={"detail": detail})})
        out.append(f)
    return out


def _citation_evidence(citations: list[Finding], ledger: list[dict]) -> list[Evidence]:
    from .lenses.base import render_ledger

    rendered = render_ledger(ledger).splitlines() if ledger else []
    out = []
    for f in citations:
        refs = [int(n) for n in re.findall(r"\[L(\d+)\]", f.receipt_plan.detail)]
        cited = [rendered[n - 1] for n in refs if 0 < n <= len(rendered)]
        out.append(Evidence(
            id=f"ledger-{f.id}", kind="ledger-citation",
            title=f"{f.lens}: deliberate per ledger, not a defect: {f.claim}",
            result="pass" if cited else "partial",
            details={"lens": f.lens, "claim": f.claim, "location": f.location,
                     "cited_entries": cited, "plan": f.receipt_plan.detail},
            excerpt="\n".join(cited)[:1000] or "cited ledger entry not found"))
    return out


def run_lenses(ctx: ReviewContext) -> StageResult:
    """[4] Six independent lenses (lenses.run_all_lenses), each with minimal context."""
    from . import context as review_context
    from . import runstore
    from .lenses import run_all_lenses
    from .lenses.harness import TEST_PROJECT_DIR

    areas_by_file = {f.path: f.area for f in ctx.intake.files}
    touched = sorted({f.area for f in ctx.intake.files if f.kind != "generated"})
    ledger = review_context.review_ledger(touched)
    out = run_all_lenses(ctx.requirement_id, ctx.requirement_text, ctx.intake.hunks,
                         ctx.routing.routes, ledger, _style_examples(ctx),
                         change_id=ctx.change_id, areas=areas_by_file)

    for t in out.generated_tests:
        rel = t.path.removeprefix(TEST_PROJECT_DIR + "/")
        ctx.generated_tests.append({"lens": t.lens, "path": rel, "content": t.content,
                                    "test_names": t.test_names, "expects": t.expects})
        dest = ctx.run_dir / "generated" / t.lens / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(t.content)

    ran = [r.lens for r in out.runs if r.status == "ok"]
    runstore.write(ctx.run_dir, "lenses.json", {
        "lenses": [{"lens": r.lens, "status": r.status, "detail": r.detail, "model": r.model,
                    "input_tokens": r.input_tokens, "output_tokens": r.output_tokens,
                    "cost_usd": r.cost_usd, "ms": r.ms, "from_fixture": r.from_fixture,
                    "hunks": len(r.hunk_ids)} for r in out.runs],
        "total_cost_usd": out.total_cost_usd,
        "ledger_entries": len(ledger),
        "ambiguity": out.ambiguity,
        "candidate_questions": len(out.candidate_questions),
        "questions_after_budget": len(out.questions),
        "generated_tests": [{"lens": t.lens, "path": t.path, "test_names": t.test_names,
                             "expects": t.expects} for t in out.generated_tests],
    })
    return StageResult(
        findings=_tie_findings_to_tests(out.findings + out.hunches, out.generated_tests),
        evidence=_citation_evidence(out.citations, ledger),
        questions=out.questions,
        not_checked=out.not_checked,
        refused=out.refusals,
        interpretations=out.interpretations,
        ran=ran,
    )


def run_receipt_checks(ctx: ReviewContext) -> StageResult:
    """[5] Receipt checks (Track D): requirement tests, mutation, regeneration, byte diff."""
    from .checks import run_receipt_checks as run

    return run(ctx)


def safe_run(name: str, fn, ctx: ReviewContext) -> StageResult:
    try:
        res = fn(ctx)
        return res if isinstance(res, StageResult) else StageResult()
    except Exception as e:  # noqa: BLE001 - a failing stage becomes a declared gap, never a crash
        log = ctx.run_dir / "logs" / f"stage-{name}.log"
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_text(traceback.format_exc())
        return StageResult(not_checked=[NotChecked(
            what=f"{name} stage",
            why=f"crashed with {type(e).__name__}: {e} (traceback in logs/stage-{name}.log)",
        )])
