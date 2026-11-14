"""`receipts review-sql`: one SQL-migration change through the SQL sandbox adapter.

Writes the same run folder as `receipts review` (runstore layout), so the dashboard reads it like
any other change. Deterministic: no LLM call, no lens; the verdict composer says so in not_checked.
"""

import time
from pathlib import Path

from . import adapters, config, context, runstore
from .gitutil import rev_parse
from .intake import run_intake
from .models import ContextUsed, NotChecked, RouteResult, RoutingReport
from .review import load_requirement
from .verdict import VerdictInputs, compose

SQL_REQUIREMENTS = config.ROOT / "examples" / "sql-repo" / "requirements"
LENS_WHY = "not run: deterministic adapter demo (no LLM call on SQL changes in this build)"
# (path test, risk, category, reason). A schema change is risky by nature; first match wins.
RISK_RULES = [
    (lambda p: p.startswith("migrations/"), 7, "compatibility", "schema migration"),
    (lambda p: p.startswith("queries/"), 4, "correctness", "query the app depends on"),
    (lambda p: True, 2, "docs", "not executed by the database"),
]


def route(intake) -> RoutingReport:
    routes, depth = [], {}
    for h in intake.hunks:
        _, risk, cat, why = next(r for r in RISK_RULES if r[0](h.file))
        routes.append(RouteResult(hunk_id=h.id, risk=risk, category=cat, confidence=1.0,
                                  router="sql-rule", reason=why))
        d = "deep" if risk >= config.ROUTER_DEEP_THRESHOLD else "light"
        if depth.get(h.file) != "deep":
            depth[h.file] = d
    return RoutingReport(router="sql-rule", routes=routes, depth_by_file=depth)


def sql_context_used() -> ContextUsed:
    return ContextUsed(
        status="confirmed" if context.is_confirmed() else "draft",
        confirmed_by=context.confirmed_by(), areas=[], entries=[],
        note="The SQL adapter runs no lens, so no product context was looked up or used.")


def run_review_sql(repo: Path, change: str, req: str, base: str = "main") -> int:
    t0 = time.monotonic()
    repo = repo.resolve()
    run_dir = runstore.new_run_dir(change)
    print(f"run dir: {run_dir}")
    title, req_text, req_missing = load_requirement(req, SQL_REQUIREMENTS)

    intake = run_intake(change, req, base, head=change, repo=repo)
    intake = intake.model_copy(update={"files": [
        f.model_copy(update={"area": adapters.sql.area_for(f.path)}) for f in intake.files]})
    runstore.write(run_dir, "intake.json", intake)
    kind = adapters.change_type([f.path for f in intake.files])
    print(f"[1] intake: {len(intake.files)} files, {len(intake.hunks)} hunks; change type {kind}")
    if kind is None:
        print("no registered adapter for this change (no migrations/*.sql touched)")
        return 2

    routing = route(intake)
    runstore.write(run_dir, "routing.json", routing)

    result = adapters.get(kind)(adapters.AdapterInput(
        repo=repo, base_sha=rev_parse(repo, base), head_sha=intake.head_sha, run_dir=run_dir,
        requirement_text=req_text))
    for e in result.evidence:
        print(f"[3] {e.result:>7}  {e.id}: {e.title}")

    not_checked = list(result.not_checked)
    if req_missing:
        not_checked.insert(0, NotChecked(what="requirement text missing",
                                         why="the review could not judge intent"))
    receipt = compose(VerdictInputs(
        intake=intake, routing=routing, requirement_title=title, requirement_text=req_text,
        run_ts=run_dir.name, commands=result.commands, evidence=result.evidence,
        findings=result.findings, not_checked=not_checked, ran=result.ran,
        lens_unavailable_reason=LENS_WHY, adapter=kind))
    receipt.mock_llm = False  # no lens ran, so nothing was replayed either
    wall = round(time.monotonic() - t0, 2)

    runstore.write(run_dir, "commands.json", result.commands)
    runstore.write(run_dir, "tests.json", [])
    runstore.write(run_dir, "evidence.json", result.evidence)
    runstore.write(run_dir, "findings.json", result.findings)
    runstore.write(run_dir, "mutants.json", [])
    runstore.write(run_dir, "questions.json", [])
    runstore.write(run_dir, "context.json", sql_context_used())
    runstore.write(run_dir, "adapter.json", {"adapter": kind, "wall_s": wall, **result.extra})
    runstore.write(run_dir, "receipt.json", receipt)
    runstore.mark_latest(run_dir)
    print(f"[7] verdict: {receipt.verdict} ({receipt.verdict_reason})")
    print(f"    findings {len(receipt.findings)}, not checked {len(receipt.not_checked)}, "
          f"wall {wall}s")
    return 0
