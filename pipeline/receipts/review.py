"""`receipts review`: one change end to end, every artifact written under runs/<change>/<ts>/."""

from pathlib import Path

from . import config, runstore, sandbox, stages
from .intake import run_intake
from .models import Evidence, Interpretation, NotChecked, TestRun
from .routers.combine import route_all
from .verdict import VerdictInputs, compose


def load_requirement(req_id: str, directory: Path | None = None) -> tuple[str, str, bool]:
    """(title, text, missing). Title is the first markdown heading without a leading "<id>:"
    (the dashboard shows the id next to it), else the id."""
    path = (directory or config.REQUIREMENTS) / f"{req_id}.md"
    if not path.exists():
        rel = path.relative_to(config.ROOT) if path.is_relative_to(config.ROOT) else path
        return req_id, f"(requirement text for {req_id} not found at {rel})", True
    text = path.read_text().strip()
    title = next((ln.lstrip("#").strip() for ln in text.splitlines() if ln.startswith("#")), req_id)
    return title.removeprefix(f"{req_id}:").strip() or req_id, text, False


def _test_evidence(run: TestRun, side: str) -> Evidence:
    failed = [c.name for c in run.cases if c.outcome == "failed"]
    if not run.compiled:
        result, title = "error", f"Existing SourceGenerator tests on {side}: did not compile"
    else:
        result = "pass" if run.failed == 0 and run.total > 0 else "fail"
        title = (f"Existing SourceGenerator tests on {side}: {run.passed}/{run.total} passed, "
                 f"{run.failed} failed")
    return Evidence(
        id=f"existing-tests-{side}", kind="existing-tests", title=title, result=result,
        command_ids=[run.receipt_id],
        details={"total": run.total, "passed": run.passed, "failed": run.failed,
                 "skipped": run.skipped, "failed_tests": failed[:50]},
        excerpt="\n".join(failed[:5]),
    )


def run_review(change: str, req: str, base: str | None = None, router: str = "auto",
               skip_mutation: bool = False, req_tests_fixture: bool = False,
               head: str | None = None, ab_reading: Interpretation | None = None,
               no_ab: bool = False) -> int:
    """Review `change` (a branch, or `head` when given). `ab_reading` marks a per-branch run of an
    A/B decision (<change>__A / __B): it reviews that branch and never starts another A/B.
    `no_ab` keeps the lenses' interpretations and refusal but builds no A/B branches (bench runs)."""
    run_dir = runstore.new_run_dir(change)
    print(f"run dir: {run_dir}")
    title, req_text, req_missing = load_requirement(req)

    intake = run_intake(change, req, base, head=head)
    runstore.write(run_dir, "intake.json", intake)
    print(f"[1] intake: {intake.summary} ({len(intake.hunks)} hunks)")

    routing = route_all(intake.hunks, req_text,
                        {"files": intake.files, "change_id": change, "requirement_id": req},
                        router, files=intake.files)
    runstore.write(run_dir, "routing.json", routing)
    depth_counts = {d: sum(v == d for v in routing.depth_by_file.values())
                    for d in ("deep", "light", "regeneration")}
    print(f"[2] routing ({routing.router}): max risk "
          f"{max((r.risk for r in routing.routes), default=1)}, {depth_counts}")

    commands = []
    tests: list[TestRun] = []
    evidence: list[Evidence] = []
    base_wt = sandbox.worktree("base", intake.base)
    head_wt = sandbox.worktree("head", intake.head_sha)
    # Held from build to test: another review's generated tests rebuild a shared worktree, and
    # --no-build would then run their test assembly as if it were the existing suite.
    with sandbox.lock(base_wt), sandbox.lock(head_wt):
        b_base = sandbox.build(base_wt, run_dir, "build-base")
        b_head = sandbox.build(head_wt, run_dir, "build-head")
        commands += [b_base, b_head]
        for side, b in (("base", b_base), ("head", b_head)):
            evidence.append(Evidence(
                id=f"build-{side}", kind="build",
                title=f"Build {side} ({config.TEST_PROJECT.split('/')[1]}, net8.0)",
                result="pass" if b.exit_code == 0 else "fail", command_ids=[b.id],
                details={"duration_s": b.duration_s, "exit_code": b.exit_code}))
        print(f"[3] build base exit {b_base.exit_code} ({b_base.duration_s}s), "
              f"head exit {b_head.exit_code} ({b_head.duration_s}s)")

        runs: dict[str, TestRun | None] = {"base": None, "head": None}
        for side, wt, b in (("head", head_wt, b_head), ("base", base_wt, b_base)):
            if b.exit_code != 0:
                continue
            run = sandbox.test(wt, run_dir, f"test-{side}-existing", side, commands=commands,
                               no_build=True)
            runs[side] = run
            tests.append(run)
            evidence.append(_test_evidence(run, side))
            print(f"[3] tests {side}: {run.passed}/{run.total} passed, {run.failed} failed "
                  f"(compiled={run.compiled})")
    runstore.write(run_dir, "commands.json", commands)
    runstore.write(run_dir, "tests.json", tests)

    ctx = stages.ReviewContext(
        run_dir=run_dir, change_id=change, requirement_id=req, requirement_title=title,
        requirement_text=req_text, requirement_missing=req_missing, intake=intake, routing=routing,
        base_sha=intake.base, head_sha=intake.head_sha, base_wt=base_wt, head_wt=head_wt,
        base_build_ok=b_base.exit_code == 0, head_build_ok=b_head.exit_code == 0,
        base_tests=runs["base"], head_tests=runs["head"], skip_mutation=skip_mutation,
        commands=commands, tests=tests,
    )
    lens_res = stages.safe_run("lenses", stages.run_lenses, ctx)
    ctx.findings = list(lens_res.findings)
    if req_tests_fixture:
        from .checks import fixture_tests

        ctx.generated_tests += fixture_tests(req)
    check_res = stages.safe_run("receipt-checks", stages.run_receipt_checks, ctx)
    findings = ctx.findings + [f for f in check_res.findings if f not in ctx.findings]
    print(f"[4] lenses ran: {lens_res.ran or 'none'}; [5] checks ran: {check_res.ran or 'none'}")

    not_checked = [*lens_res.not_checked, *check_res.not_checked]
    if req_missing:
        not_checked.insert(0, NotChecked(
            what="requirement text missing",
            why=f"requirements/{req}.md does not exist; the review could not judge intent"))
    lens_why = ""
    if not lens_res.ran:
        if config.MOCK_LLM:
            lens_why = "MOCK_LLM=1 and no lens output was replayed for this change"
        elif not config.ANTHROPIC_API_KEY:
            lens_why = "LLM unavailable: ANTHROPIC_API_KEY not set and no recorded fixture"

    evidence += [*lens_res.evidence, *check_res.evidence]
    questions = [*lens_res.questions, *check_res.questions]
    mutants = [*lens_res.mutants, *check_res.mutants]
    interpretations = [*lens_res.interpretations, *check_res.interpretations]
    refused = [*lens_res.refused, *check_res.refused]
    narrative = ""
    ab_result = None

    def verdict_inputs() -> VerdictInputs:
        return VerdictInputs(
            intake=intake, routing=routing, requirement_title=title, requirement_text=req_text,
            run_ts=run_dir.name, commands=commands, tests=tests, evidence=evidence,
            findings=findings, questions=questions, interpretations=interpretations,
            refused=refused, not_checked=not_checked, ran=[*lens_res.ran, *check_res.ran],
            skip_mutation=skip_mutation, lens_unavailable_reason=lens_why)

    if ab_reading is not None:
        # The parent run owns the A/B decision and its questions; this run only reviews one branch.
        narrative = (f"This run reviews reading {ab_reading.label} of {req} on branch "
                     f"{ab_reading.branch}: {ab_reading.summary}")
        interpretations, questions = [], []
        refused = [r for r in refused if "(A or B)" not in r.what]
    elif interpretations and no_ab:
        not_checked.append(NotChecked(
            what="A/B review branches",
            why="--no-ab: the two readings are recorded, but branches A and B were not built or "
                "reviewed in this run"))
    elif interpretations:
        # Built even when a blocker exists: the reading is still the human's call, and each
        # branch carries its own receipts (the blocker may hold on one reading only).
        from . import ab

        print("[A/B] two readings found; building review branches")
        panel = next((r.why for r in refused if "(A or B)" in r.what), "")
        ab_result = ab.build_branches(change, req, req_text, interpretations, intake, run_dir,
                                      commands, panel)
        built_b = f" As built on the branch: {ab_result.summary_b}" if ab_result.summary_b else ""
        interpretations = [i.model_copy(update={
            "branch": ab_result.branches.get(i.label, ""),
            "summary": i.summary + (built_b if i.label == "B" and ab_result.branches.get("B") else "")})
            for i in interpretations]
        refused = [r for r in refused if "(A or B)" not in r.what] + [ab_result.refusal]
        evidence += ab_result.evidence
        not_checked += ab_result.not_checked
        runstore.write(run_dir, "ab.json", {
            "branches": ab_result.branches, "shas": ab_result.shas,
            "summary_b": ab_result.summary_b, "diff_stat": ab_result.diff_stat,
            "cost_usd": round(ab_result.cost_usd, 6), "tokens": ab_result.tokens})
        print(f"[A/B] branches: {ab_result.branches}")
    receipt = compose(verdict_inputs())

    if narrative:
        receipt.narrative = narrative
    runstore.write(run_dir, "commands.json", commands)
    runstore.write(run_dir, "tests.json", tests)
    runstore.write(run_dir, "evidence.json", evidence)
    runstore.write(run_dir, "findings.json", findings)
    runstore.write(run_dir, "mutants.json", mutants)
    runstore.write(run_dir, "questions.json", questions)
    runstore.write(run_dir, "receipt.json", receipt)
    runstore.mark_latest(run_dir)
    print(f"[7] verdict: {receipt.verdict} ({receipt.verdict_reason})")
    print(f"    checked {len(receipt.checked)}, not checked {len(receipt.not_checked)}, "
          f"findings {len(receipt.findings)}, hunches {len(receipt.hunches)}")
    print(f"receipt: {run_dir / 'receipt.json'}")
    if ab_result is not None:
        for interp in interpretations:
            if interp.branch:
                print(f"[A/B] reviewing reading {interp.label}: {interp.branch}")
                run_review(f"{change}__{interp.label}", req, base=base, router=router,
                           skip_mutation=skip_mutation, req_tests_fixture=req_tests_fixture,
                           head=interp.branch, ab_reading=interp)
    return 0
