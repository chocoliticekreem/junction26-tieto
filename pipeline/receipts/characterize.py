"""[5b] Characterization receipts for the blast radius (notes/blast-radius.md).

For at most 3 modified existing members with callers outside the change (most callers first), the
regression lens writes up to 3 xUnit tests per member from the BASE source and the callers' code only.
Then:
  base compile error or every test fails on base  -> invalid (it does not describe old behaviour)
  a test passes on base                           -> it is a characterization; run it on head:
      head pass      -> Evidence behaviour-preserved (pass)
      head fail      -> Evidence behaviour-changed (fail) + a verified high finding
      head compile error / not run -> invalid (recorded, never counted as a change)
"""

import json
import re

from . import llm, sandbox
from .checks import name_filter
from .impact import outside_callers, short_name
from .lenses import EXAMPLE_FILES
from .lenses import regression as lens
from .lenses.base import render_examples
from .models import Evidence, Finding, NotChecked, ReceiptPlan, TestRun
from .stages import ReviewContext, StageResult


def select_targets(raw: dict | None) -> list[dict]:
    members = [m for m in (raw or {}).get("members", [])
               if m["kind"] == "modified" and outside_callers(m) and m.get("base_span")]
    members.sort(key=lambda m: (-len(outside_callers(m)), m["name"]))
    return members[:lens.MAX_MEMBERS]


def _lines(wt, rel: str) -> list[str]:
    p = wt / rel
    return p.read_bytes().decode("utf-8", errors="replace").split("\n") if p.exists() else []


def build_targets(members: list[dict], base_wt, head_wt) -> list[dict]:
    out = []
    for i, m in enumerate(members, 1):
        lo, hi = m["base_span"]
        callers = []
        # Callers whose receiver is visibly the member's type first; name-only matches last.
        ranked = sorted(outside_callers(m), key=lambda c: c.get("receiver") == "unresolved")
        for c in ranked[:lens.MAX_CALLERS]:
            callers.append({"member": c["member"], "file": c["file"],
                            "name_only": c.get("receiver") == "unresolved",
                            "code": lens.caller_snippet(_lines(head_wt, c["file"]), c)})
        out.append({"id": f"M{i}", "name": m["name"], "type_name": m.get("type_name", ""),
                    "member_kind": m.get("member_kind", "member"), "file": m["file"],
                    "base_source": "\n".join(_lines(base_wt, m["file"])[lo - 1:hi]),
                    "callers": callers, "impact": m})
    return out


def qualify(content: str, names: list[str]) -> list[str]:
    cls = re.search(r"\bclass\s+(\w+)", content)
    return [n if "." in n or not cls else f"{cls.group(1)}.{n}" for n in names]


def outcomes(run: TestRun | None, names: list[str]) -> dict[str, str] | None:
    """name -> passed / failed / missing; None when the run did not compile (or did not happen)."""
    if run is None or not run.compiled:
        return None
    out = {}
    for n in names:
        cases = [c for c in run.cases if n in c.name]
        out[n] = ("missing" if not cases
                  else "failed" if any(c.outcome == "failed" for c in cases) else
                  "passed" if all(c.outcome == "passed" for c in cases) else "missing")
    return out


def classify(base: dict[str, str] | None, head: dict[str, str] | None,
             head_ran: bool) -> tuple[str, str, str, list[str]]:
    """(base, head, result, characterizing test names). `base`/`head`: outcomes() of the runs."""
    if base is None:
        return "compile-error", "not-run", "invalid", []
    valid = [n for n, o in base.items() if o == "passed"]
    if not valid:
        return "fail", "not-run", "invalid", []
    if not head_ran:
        return "pass", "not-run", "invalid", valid
    if head is None:
        return "pass", "compile-error", "invalid", valid
    if any(head.get(n) == "failed" for n in valid):
        return "pass", "fail", "changed", valid
    if all(head.get(n) == "passed" for n in valid):
        return "pass", "pass", "preserved", valid
    return "pass", "not-run", "invalid", valid


def _run_side(ctx: ReviewContext, side: str, wt, tests: list[dict],
              names: dict[int, list[str]]) -> dict[int, TestRun]:
    """One test run with every file; when it does not compile and there are several files, one run
    per file, so one bad file does not void the others."""
    if not tests:
        return {}
    files = {t["path"]: t["content"] for t in tests}
    every = [n for k in names for n in names[k]]
    run = sandbox.test(wt, ctx.run_dir, f"characterize-{side}", side, filter=name_filter(every),
                       extra_test_files=files, commands=ctx.commands)
    ctx.tests.append(run)
    if run.compiled or len(tests) == 1:
        return {t["k"]: run for t in tests}
    per: dict[int, TestRun] = {}
    for t in tests:
        r = sandbox.test(wt, ctx.run_dir, f"characterize-{side}-{t['k']}", side,
                         filter=name_filter(names[t["k"]]),
                         extra_test_files={t["path"]: t["content"]}, commands=ctx.commands)
        ctx.tests.append(r)
        per[t["k"]] = r
    return per


def run(ctx: ReviewContext, raw: dict | None) -> tuple[StageResult, list[dict]]:
    res = StageResult()
    members = select_targets(raw)
    if not members:
        return res, []
    if not ctx.base_build_ok or ctx.base_wt is None:
        res.not_checked.append(NotChecked(what="regression lens (characterization tests)",
                                          why="base does not build, so old behaviour cannot be pinned"))
        return res, []
    targets = build_targets(members, ctx.base_wt, ctx.head_wt)
    examples = [{"path": p, "content": "\n".join(_lines(ctx.base_wt, p))} for p in EXAMPLE_FILES]
    user = lens.build_user_prompt(targets, render_examples(examples))
    try:
        reply = llm.call_json(lens.PURPOSE, lens.MODEL, lens.SYSTEM, user, lens.SCHEMA,
                              effort=lens.EFFORT, max_tokens=lens.MAX_TOKENS)
        tests = lens.parse_output(reply.data, [t["id"] for t in targets])
    except (llm.LLMUnavailable, lens.RegressionOutputError) as e:
        res.not_checked.append(NotChecked(what="regression lens (characterization tests)",
                                          why=f"unavailable: {e}"))
        return res, []
    res.ran.append("regression")
    ctx.run_dir.joinpath("impact").mkdir(parents=True, exist_ok=True)
    ctx.run_dir.joinpath("impact", "regression-lens.json").write_text(json.dumps({
        "model": lens.MODEL, "effort": lens.EFFORT, "input_tokens": reply.input_tokens,
        "output_tokens": reply.output_tokens, "ms": reply.ms, "from_fixture": reply.from_fixture,
        "cost_usd": llm.cost_usd(lens.MODEL, reply.input_tokens, reply.output_tokens),
        "members": {t["id"]: t["name"] for t in targets},
        "tests": [{k: v for k, v in t.items() if k != "content"} for t in tests]}, indent=2))
    by_id = {t["id"]: t for t in targets}
    for k, t in enumerate(tests, 1):
        t["k"] = k
        t["names"] = qualify(t["content"], t["test_names"])
        dest = ctx.run_dir / "generated" / "regression" / t["path"]
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(t["content"])

    base_runs = _run_side(ctx, "base", ctx.base_wt, tests, {t["k"]: t["names"] for t in tests})
    base_out = {t["k"]: outcomes(base_runs.get(t["k"]), t["names"]) for t in tests}
    valid = {t["k"]: [n for n, o in (base_out[t["k"]] or {}).items() if o == "passed"]
             for t in tests}
    head_tests = [t for t in tests if valid[t["k"]]]
    head_runs = ({} if not (ctx.head_build_ok and ctx.head_wt) else
                 _run_side(ctx, "head", ctx.head_wt, head_tests,
                           {t["k"]: valid[t["k"]] for t in head_tests}))

    characterization: list[dict] = []
    tested_ids = {t["member"] for t in tests}
    for t in tests:
        target = by_id[t["member"]]
        m = target["impact"]
        n_out = len(outside_callers(m))
        head_ran = t["k"] in head_runs
        head_out = outcomes(head_runs.get(t["k"]), valid[t["k"]]) if head_ran else None
        base_s, head_s, result, names = classify(base_out[t["k"]], head_out, head_ran)
        runs = [r for r in (base_runs.get(t["k"]), head_runs.get(t["k"])) if r is not None]
        entry = {"member": m["name"], "test_names": names or t["names"], "base": base_s,
                 "head": head_s, "result": result, "evidence_id": ""}
        characterization.append(entry)
        discarded = [n for n in t["names"] if n not in names]
        if result == "invalid":
            why = {"compile-error": "its tests do not compile on base",
                   "fail": "every test fails on base, so none describes the old behaviour"}.get(
                base_s, "it passes on base but could not be run on head"
                        + (" (does not compile on head)" if head_s == "compile-error" else ""))
            res.not_checked.append(NotChecked(
                what=f"characterization of {short_name(m)} (regression lens)",
                why=f"discarded as invalid: {why}; tests: {', '.join(t['names'])}"))
            continue
        eid = f"characterize-{t['k']}"
        entry["evidence_id"] = eid
        changed = result == "changed"
        failed_head = [n for n in names if (head_out or {}).get(n) == "failed"]
        res.evidence.append(Evidence(
            id=eid, kind="behaviour-changed" if changed else "behaviour-preserved",
            title=(f"{short_name(m)}: {len(failed_head)}/{len(names)} characterization test(s) "
                   f"pass on base, fail on head" if changed else
                   f"{short_name(m)}: {len(names)} characterization test(s) pass on base and head"),
            result="fail" if changed else "pass",
            command_ids=[r.receipt_id for r in runs],
            details={"member": m["name"], "path": t["path"], "test_names": names,
                     "pins": t["pins"], "callers_outside_change": n_out,
                     "base": base_out[t["k"]], "head": head_out, "discarded_failed_on_base": discarded,
                     "failed_on_head": failed_head},
            excerpt="\n".join(f"[head] {c.name}: {c.message.splitlines()[0][:200] if c.message else ''}"
                              for r in runs if r.side == "head" for c in r.cases
                              if c.outcome == "failed")[:1500]))
        if changed:
            res.findings.append(Finding(
                id=f"regression-{t['k']}", lens="regression",
                claim=(f"changes the behaviour of {short_name(m)} that {n_out} other places rely "
                       f"on: {failed_head[0].rsplit('.', 1)[-1]} passes before, fails after"),
                location=f"{m['file']}:{(m.get('head_span') or m['base_span'])[0]}",
                severity="high",
                receipt_plan=ReceiptPlan(kind="test", detail=(
                    f"characterization tests {', '.join(failed_head)} ({t['path']}) pass on base "
                    "and fail on head")),
                verified=True, evidence_ids=[eid]))
    for t in targets:
        if t["id"] not in tested_ids:
            res.not_checked.append(NotChecked(
                what=f"characterization of {short_name(t['impact'])} (regression lens)",
                why="the regression lens wrote no test for this member"))
    return res, characterization
