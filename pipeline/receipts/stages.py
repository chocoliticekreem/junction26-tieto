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


def run_lenses(ctx: ReviewContext) -> StageResult:
    """[4] Lenses (Track C). Default: nothing runs; the verdict declares every lens unchecked."""
    return StageResult()


def run_receipt_checks(ctx: ReviewContext) -> StageResult:
    """[5] Receipt checks (Track D). Default: nothing runs; the verdict declares each check unchecked."""
    return StageResult()


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
