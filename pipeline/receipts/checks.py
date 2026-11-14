"""[5] Receipt checks (SPEC 5.5): every check executes something and returns Evidence.

  requirement tests  ctx.generated_tests run on base and head (fail-before-pass-after / new-api)
  mutation           mutants on added lines of risky source hunks vs the change's own tests
  regeneration       generated snapshot files mapped to the SourceGenerator tests that verify them
  byte-diff          compat.run_compat when the runtime/annotations or a compatibility hunk changed

Then lens findings whose receipt_plan matches a positive artifact become verified. A finding is
never verified without an executed artifact that shows the problem.

generated_tests contract (filled by run_lenses, or by --req-tests-fixture):
    {"lens": "requirement-tests" | "logic-edge",
     "path": "<path relative to the test project dir>.cs",
     "content": "<C# source>",
     "test_names": ["<method name or Class.Method>", ...],
     "expects": "fail-on-base-pass-on-head" | "new-api" | "fail-on-head"}
"""

import re
from pathlib import Path

from . import compat, config, sandbox
from .models import Evidence, Finding, Mutant, NotChecked, ReceiptPlan, TestRun
from .mutants import MutantSpec, generate
from .stages import ReviewContext, StageResult

MUTANT_TIMEOUT_S = 180
FIXTURES_REQ_TESTS = config.ROOT / "fixtures" / "requirement-tests"
TEST_PROJECT_DIR = str(Path(config.TEST_PROJECT).parent)
RESOURCES_PREFIX = f"{TEST_PROJECT_DIR}/Resources/"
COMPAT_PREFIXES = ("src/MessagePack/", "src/MessagePack.Annotations/")
COMPAT_NOT_CHECKED = "compatibility byte diff (base vs head serialized bytes)"


# Requirement tests ---------------------------------------------------------------------------


def fixture_tests(req_id: str) -> list[dict]:
    """Hand-written requirement tests kept as a fixture (fixtures/requirement-tests/<REQ>.cs)."""
    path = FIXTURES_REQ_TESTS / f"{req_id}.cs"
    if not path.exists():
        return []
    content = path.read_text()
    cls = re.search(r"^public\s+(?:sealed\s+)?class\s+(\w+)", content, re.M)
    names = re.findall(r"\[Fact\]\s*\n\s*public\s+(?:async\s+)?\w+(?:<\w+>)?\s+(\w+)\(", content)
    prefix = f"{cls.group(1)}." if cls else ""
    return [{"lens": "requirement-tests", "path": f"ReceiptsGenerated/{path.name}",
             "content": content, "test_names": [prefix + n for n in names],
             "expects": "fail-on-base-pass-on-head"}]


def name_filter(names: list[str]) -> str:
    return "|".join(f"FullyQualifiedName~{n}" for n in names)


def _matching(run: TestRun | None, names: list[str]) -> list:
    if run is None:
        return []
    return [c for c in run.cases if any(n in c.name for n in names)]


def compile_errors(run_dir: Path, log_rel: str, limit: int = 8) -> list[str]:
    path = run_dir / log_rel
    if not path.exists():
        return []
    seen: list[str] = []
    for ln in path.read_text(errors="replace").splitlines():
        if ": error " in ln:
            msg = re.sub(r"^.*?/(tests|src)/", r"\1/", ln.strip())
            msg = re.sub(r"\s*\[[^\]]*\.csproj[^\]]*\]$", "", msg)
            if msg not in seen:
                seen.append(msg)
        if len(seen) >= limit:
            break
    return seen


def classify_generated_test(base: TestRun | None, head: TestRun | None, names: list[str],
                            expects: str) -> tuple[str, str, str]:
    """(evidence kind, result, title) for one generated test file run on base and head."""
    tag = "edge-case test" if expects == "fail-on-head" else "requirement test"
    if head is None or not head.compiled:
        return "fail-before-pass-after", "error", f"{tag} does not compile on head"
    head_cases = _matching(head, names)
    if not head_cases:
        return "fail-before-pass-after", "error", f"{tag}: no test matched on head"
    head_failed = [c.name for c in head_cases if c.outcome == "failed"]
    if head_failed:
        return "fail-before-pass-after", "fail", (
            f"{tag} fails on head: {len(head_failed)}/{len(head_cases)} failed")
    if expects == "fail-on-head":
        return "fail-before-pass-after", "pass", (
            f"{tag} passes on head ({len(head_cases)} tests): no defect shown")
    if base is None or not base.compiled:
        return "new-api", "pass", (
            f"{tag} does not compile on base, passes on head ({len(head_cases)} tests): "
            "new-API evidence (weaker)")
    base_failed = [c.name for c in _matching(base, names) if c.outcome == "failed"]
    if base_failed:
        return "fail-before-pass-after", "pass", (
            f"{tag} fails on base ({len(base_failed)}/{len(head_cases)}), passes on head "
            f"({len(head_cases)}/{len(head_cases)})")
    return "fail-before-pass-after", "partial", (
        f"{tag} does not discriminate: passes before the change ({len(head_cases)} tests pass on "
        "base and head); weak receipt")


def run_generated_tests(ctx: ReviewContext) -> tuple[list[Evidence], list[NotChecked], set[str]]:
    evidence: list[Evidence] = []
    gaps: list[NotChecked] = []
    ran: set[str] = set()
    if not ctx.generated_tests:
        return evidence, gaps, ran
    if not ctx.head_build_ok or ctx.head_wt is None:
        gaps.append(NotChecked(what="requirement tests (fail on base, pass on head)",
                               why="head does not build"))
        return evidence, gaps, ran
    for i, gt in enumerate(ctx.generated_tests, start=1):
        names = list(gt.get("test_names") or [])
        expects = gt.get("expects", "fail-on-base-pass-on-head")
        lens = gt.get("lens", "requirement-tests")
        files = {gt["path"]: gt["content"]}
        flt = name_filter(names) if names else None
        rid = f"gentest-{i}"
        head = sandbox.test(ctx.head_wt, ctx.run_dir, f"{rid}-head", "head", filter=flt,
                            extra_test_files=files, commands=ctx.commands)
        ctx.tests.append(head)
        base = None
        if expects != "fail-on-head" and ctx.base_wt is not None and ctx.base_build_ok:
            base = sandbox.test(ctx.base_wt, ctx.run_dir, f"{rid}-base", "base", filter=flt,
                                extra_test_files=files, commands=ctx.commands)
            ctx.tests.append(base)
        kind, result, title = classify_generated_test(base, head, names, expects)
        ran.add(kind)
        per_test = {}
        for side, run in (("base", base), ("head", head)):
            if run is None:
                continue
            if not run.compiled:
                per_test[side] = "did not compile"
                continue
            per_test[side] = {c.name: c.outcome for c in _matching(run, names)}
        errors = {}
        for side, run in (("base", base), ("head", head)):
            if run is not None and not run.compiled:
                errors[side] = compile_errors(ctx.run_dir, f"logs/{rid}-{side}.log")
        failures = [f"[{side}] {c.name}: {c.message.splitlines()[0][:200] if c.message else ''}"
                    for side, run in (("base", base), ("head", head)) if run
                    for c in _matching(run, names) if c.outcome == "failed"]
        excerpt = "\n".join(failures[:6] + [f"[{s}] {e}" for s, es in errors.items()
                                             for e in es[:3]])
        evidence.append(Evidence(
            id=rid, kind=kind, title=f"{gt['path']}: {title}", result=result,
            command_ids=[r.receipt_id for r in (base, head) if r is not None],
            details={"lens": lens, "path": gt["path"], "expects": expects, "test_names": names,
                     "outcomes": per_test, "compile_errors": errors,
                     "failed_on_head": [c.name for c in _matching(head, names)
                                        if c.outcome == "failed"]},
            excerpt=excerpt))
    return evidence, gaps, ran


# Mutation ------------------------------------------------------------------------------------


def changed_test_classes(ctx: ReviewContext) -> list[str]:
    """Test classes the change added or modified in the sandboxed test project (stem = class)."""
    out: list[str] = []
    for f in ctx.intake.files:
        if (f.kind != "test" or f.status == "deleted" or not f.path.endswith(".cs")
                or not f.path.startswith(TEST_PROJECT_DIR + "/")):
            continue
        stem = Path(f.path).stem
        src = (ctx.head_wt / f.path) if ctx.head_wt else None
        if src and src.exists() and re.search(rf"\bclass\s+{re.escape(stem)}\b", src.read_text()):
            out.append(stem)
    return sorted(set(out))


def mutation_targets(ctx: ReviewContext) -> list[MutantSpec]:
    risk = {r.hunk_id: r.risk for r in ctx.routing.routes}
    specs: list[MutantSpec] = []
    cache: dict[str, list[str]] = {}
    for h in ctx.intake.hunks:
        if h.kind != "source" or risk.get(h.id, 1) < config.MUTATION_RISK_THRESHOLD:
            continue
        if not h.file.endswith(".cs"):
            continue
        if h.file not in cache:
            p = ctx.head_wt / h.file
            cache[h.file] = p.read_bytes().decode("utf-8").split("\n") if p.exists() else []
        specs += generate(h.file, cache[h.file], h.text, h.new_start, config.MUTANTS_PER_HUNK)
    return specs


def _apply(wt: Path, spec: MutantSpec) -> None:
    p = wt / spec.file
    lines = p.read_bytes().decode("utf-8").split("\n")
    if lines[spec.line - 1] != spec.original:
        raise RuntimeError(f"{spec.file}:{spec.line} does not match the mutant's original line")
    lines[spec.line - 1] = spec.mutated
    p.write_bytes("\n".join(lines).encode("utf-8"))


def mutant_status(run: TestRun, exit_code: int, log_text: str) -> tuple[str, list[str]]:
    if exit_code == 124:
        return "timeout", []
    if not run.compiled:
        if ": error " in log_text:
            return "compile-error", []
        return "killed", ["(test host crashed)"]
    killing = [c.name for c in run.cases if c.outcome == "failed"]
    if killing:
        return "killed", killing
    if run.total == 0:
        return "compile-error", []
    return "survived", []


def run_mutation(ctx: ReviewContext) -> tuple[list[Evidence], list[Mutant], list[Finding],
                                              list[NotChecked], bool]:
    if ctx.skip_mutation:
        return [], [], [], [], False
    if not ctx.head_build_ok or ctx.head_wt is None:
        return [], [], [], [NotChecked(what="mutation testing", why="head does not build")], False
    specs = mutation_targets(ctx)
    if not specs:
        risky = sum(1 for r in ctx.routing.routes if r.risk >= config.MUTATION_RISK_THRESHOLD
                    and r.category not in ("generated", "docs", "tests"))
        why = (f"{risky} source hunk(s) routed at risk >= {config.MUTATION_RISK_THRESHOLD}, but "
               "none has an added line the mutation operators apply to" if risky else
               f"no changed source hunk routed at risk >= {config.MUTATION_RISK_THRESHOLD} "
               "(mutation threshold)")
        return [], [], [], [NotChecked(what="mutation testing", why=why)], False

    classes = changed_test_classes(ctx)
    head_cases = ctx.head_tests.cases if ctx.head_tests else []
    classes = [c for c in classes if any(t.name.startswith(c + ".") for t in head_cases)]
    flt = name_filter(classes) if classes else None
    scope = (f"tests in the change's own test classes: {', '.join(classes)}" if classes else
             f"full {TEST_PROJECT_DIR} (the change adds no runnable test class there)")
    baseline_failed = [t.name for t in head_cases if t.outcome == "failed"
                       and (not classes or any(t.name.startswith(c + ".") for c in classes))]
    if baseline_failed:
        ev = Evidence(id="mutation", kind="mutation", result="error",
                      title="Mutation not meaningful: the selected tests already fail on head",
                      details={"test_scope": scope, "baseline_failed": baseline_failed[:20]})
        return [ev], [], [], [], True

    wt = sandbox.worktree("mut", ctx.head_sha, name=f"mut-{ctx.change_id}")
    mutants: list[Mutant] = []
    with sandbox.lock(wt):
        sandbox.reset(wt)
        for n, spec in enumerate(specs, start=1):
            rid = f"mutant-{n}"
            try:
                _apply(wt, spec)
                run = sandbox.test(wt, ctx.run_dir, rid, "head", filter=flt,
                                   commands=ctx.commands, timeout=MUTANT_TIMEOUT_S)
            finally:
                sandbox.reset(wt)
            ctx.tests.append(run)
            receipt = next(c for c in reversed(ctx.commands) if c.id == rid)
            log = (ctx.run_dir / receipt.log_path).read_text(errors="replace")
            status, killing = mutant_status(run, receipt.exit_code, log)
            mutants.append(Mutant(id=f"m{n}", file=spec.file, line=spec.line,
                                  operator=spec.operator, original=spec.original.strip(),
                                  mutated=spec.mutated.strip(), status=status,
                                  killing_tests=killing[:10]))
        sandbox.reset(wt)

    counts = {s: sum(m.status == s for m in mutants)
              for s in ("killed", "survived", "compile-error", "timeout")}
    if counts["killed"] + counts["survived"] == 0:
        result = "error"
    elif counts["survived"]:
        result = "partial"
    else:
        result = "pass"
    files = sorted({m.file for m in mutants})
    title = (f"Mutation: {counts['killed']} killed, {counts['survived']} survived, "
             f"{counts['compile-error']} compile errors, {counts['timeout']} timeouts "
             f"({len(mutants)} mutants on {', '.join(Path(f).name for f in files)})")
    survivors = [m for m in mutants if m.status == "survived"]
    ev = Evidence(
        id="mutation", kind="mutation", title=title, result=result,
        command_ids=[f"mutant-{i}" for i in range(1, len(mutants) + 1)],
        details={**counts, "total": len(mutants), "test_scope": scope, "test_filter": flt or "",
                 "risk_threshold": config.MUTATION_RISK_THRESHOLD,
                 "mutants_per_hunk_cap": config.MUTANTS_PER_HUNK,
                 "survivors": [f"{m.file}:{m.line} {m.operator}" for m in survivors]},
        excerpt="\n".join(f"survived {m.operator} at {Path(m.file).name}:{m.line}: "
                          f"{m.original} -> {m.mutated or '(deleted)'}" for m in survivors[:5]))
    findings = [Finding(
        id=f"mutation-survivor-{m.id}", lens="test-quality",
        claim=f"the change's tests do not detect {m.operator} at {m.file}:{m.line}",
        location=f"{m.file}:{m.line}", severity="medium",
        receipt_plan=ReceiptPlan(kind="mutation",
                                 detail=f"{m.original} -> {m.mutated or '(statement deleted)'}"),
        verified=True, evidence_ids=["mutation"]) for m in survivors]
    return [ev], mutants, findings, [], True


# Regeneration --------------------------------------------------------------------------------


def _method_and_args(test_name: str) -> tuple[str, str]:
    head, _, args = test_name.partition("(")
    method = head.rsplit(".", 1)[-1]
    if args:
        args = ", ".join(a.split(":", 1)[-1].strip() for a in args.rstrip(")").split(","))
    return method, args.strip('"')


def map_generated(generated: list[str], head: TestRun) -> dict[str, list]:
    """Generated file -> head test cases that verify it (Resources/<testMethod>/...)."""
    by_method: dict[str, list] = {}
    for c in head.cases:
        by_method.setdefault(_method_and_args(c.name)[0], []).append(c)
    out: dict[str, list] = {}
    for path in generated:
        if not path.startswith(RESOURCES_PREFIX):
            out[path] = []
            continue
        folder = path[len(RESOURCES_PREFIX):].split("/", 1)[0]
        method, _, args = folder.partition("(")
        args = args.rstrip(")")
        cases = by_method.get(method, [])
        exact = [c for c in cases if _method_and_args(c.name)[1] == args] if args else cases
        out[path] = exact or cases
    return out


def run_regeneration(ctx: ReviewContext) -> tuple[list[Evidence], bool]:
    generated = [f.path for f in ctx.intake.files
                 if f.kind == "generated" and f.status != "deleted"]
    if not generated:
        return [], False
    head = ctx.head_tests
    if head is None or not head.compiled:
        return [Evidence(
            id="regeneration", kind="regeneration", result="error",
            title=f"Regeneration of {len(generated)} generated files: head tests did not run",
            details={"generated_files": len(generated)})], True
    mapping = map_generated(generated, head)
    unmapped = sorted(p for p, cs in mapping.items() if not cs)
    failing_mapped = sorted({c.name for cs in mapping.values() for c in cs
                             if c.outcome != "passed"})
    covered = [p for p, cs in mapping.items() if cs and all(c.outcome == "passed" for c in cs)]
    head_failed = [c.name for c in head.cases if c.outcome == "failed"]
    tests_used = sorted({c.name for cs in mapping.values() for c in cs})
    if head_failed:
        result = "fail"
    elif unmapped:
        result = "partial"
    else:
        result = "pass"
    title = (f"Regeneration: {len(covered)}/{len(generated)} generated files verified by "
             f"{len(tests_used)} passing snapshot tests on head")
    if head_failed:
        title += f"; {len(head_failed)} head test(s) fail"
    return [Evidence(
        id="regeneration", kind="regeneration", title=title, result=result,
        command_ids=[head.receipt_id],
        details={
            "generated_files": len(generated), "covered": len(covered),
            "unmapped": unmapped[:50], "failing_tests": head_failed[:50],
            "failing_mapped_tests": failing_mapped[:50], "snapshot_tests": len(tests_used),
            "head_tests_passed": head.passed, "head_tests_total": head.total,
            "mapping": (f"each file {RESOURCES_PREFIX}<testMethod>/... is the expected output the "
                        "SourceGenerator verifier compares against for test <testMethod> "
                        "(CSharpSourceGeneratorVerifier.Test uses Resources/<testMethod>); "
                        "'Name(a, b)' folders map to the theory case with those argument values, "
                        "plain folders to every case of that method. Pass = every generated file "
                        "maps to at least one test, and every head test passed."),
        },
        excerpt="\n".join([*head_failed[:5], *[f"unmapped: {p}" for p in unmapped[:5]]]))], True


# Compatibility -------------------------------------------------------------------------------


def compat_needed(ctx: ReviewContext) -> bool:
    if any(f.path.startswith(COMPAT_PREFIXES) and f.kind == "source" for f in ctx.intake.files):
        return True
    return any(r.category == "compatibility" for r in ctx.routing.routes)


def run_compatibility(ctx: ReviewContext) -> tuple[list[Evidence], list[Finding],
                                                   list[NotChecked], bool]:
    if not compat_needed(ctx):
        return [], [], [NotChecked(what=COMPAT_NOT_CHECKED,
                                   why="no runtime/public API change; byte diff not needed")], False
    if ctx.base_wt is None or ctx.head_wt is None:
        return [], [], [NotChecked(what=COMPAT_NOT_CHECKED, why="worktrees unavailable")], False
    with sandbox.lock(ctx.base_wt), sandbox.lock(ctx.head_wt):
        receipts, ev = compat.run_compat(ctx.base_wt, ctx.head_wt, ctx.run_dir)
    ctx.commands.extend(receipts)
    findings: list[Finding] = []
    if ev.result == "fail":
        d = ev.details
        parts = []
        if d.get("differing"):
            parts.append(f"{len(d['differing'])} sample(s) serialize to different bytes")
        if d.get("roundtrip_failures"):
            parts.append(f"{len(d['roundtrip_failures'])} base payload(s) fail to read on head")
        if d.get("api_removed"):
            parts.append(f"{len(d['api_removed'])} public API member(s) removed")
        src = [f.path for f in ctx.intake.files if f.path.startswith(COMPAT_PREFIXES)]
        findings.append(Finding(
            id="compat-break", lens="compatibility",
            claim="compatibility break: " + "; ".join(parts),
            location=src[0] if src else "src/MessagePack",
            severity="high", receipt_plan=ReceiptPlan(kind="byte-diff", detail=ev.title),
            verified=True, evidence_ids=[ev.id]))
    return [ev], findings, [], True


# Mapping lens findings to evidence -----------------------------------------------------------


def verify_findings(findings: list[Finding], evidence: list[Evidence],
                    mutants: list[Mutant]) -> list[Finding]:
    """Attach evidence to lens findings whose receipt plan an executed artifact confirms."""
    by_kind: dict[str, list[Evidence]] = {}
    for e in evidence:
        by_kind.setdefault(e.kind, []).append(e)
    test_ev = [e for e in by_kind.get("fail-before-pass-after", []) + by_kind.get("new-api", [])
               if e.result == "fail" and e.details.get("failed_on_head")]
    survivors_by_file: dict[str, list[Mutant]] = {}
    for m in mutants:
        if m.status == "survived":
            survivors_by_file.setdefault(m.file, []).append(m)
    out: list[Finding] = []
    for f in findings:
        if f.verified and f.evidence_ids:
            out.append(f)
            continue
        ids: list[str] = []
        plan = f.receipt_plan.kind
        if plan == "test" and f.lens in ("requirement-tests", "logic-edge"):
            text = f"{f.receipt_plan.detail} {f.location} {f.claim}"
            same_lens = [e for e in test_ev if e.details.get("lens") == f.lens]
            named = [e for e in same_lens
                     if any(_short(n) in text for n in e.details["failed_on_head"])]
            lens_plans = sum(1 for g in findings
                             if g.lens == f.lens and g.receipt_plan.kind == "test")
            if named:
                ids = [e.id for e in named]
            elif len(same_lens) == 1 and lens_plans == 1:
                ids = [same_lens[0].id]
        elif plan == "mutation":
            path = f.location.split(":", 1)[0]
            if survivors_by_file.get(path):
                ids = ["mutation"]
        elif plan == "byte-diff":
            ids = [e.id for e in by_kind.get("byte-diff", []) if e.result == "fail"]
        elif plan == "regeneration":
            ids = [e.id for e in by_kind.get("regeneration", []) if e.result == "fail"]
        if ids:
            f = f.model_copy(update={"verified": True, "evidence_ids": ids})
        out.append(f)
    return out


def _short(test_name: str) -> str:
    return test_name.split("(", 1)[0].rsplit(".", 1)[-1]


# Stage ---------------------------------------------------------------------------------------


def run_receipt_checks(ctx: ReviewContext) -> StageResult:
    res = StageResult()

    ev, gaps, ran = run_generated_tests(ctx)
    res.evidence += ev
    res.not_checked += gaps
    res.ran += sorted(ran)

    ev, mutants, survivor_findings, gaps, ran_mut = run_mutation(ctx)
    res.evidence += ev
    res.mutants += mutants
    res.not_checked += gaps
    if ran_mut:
        res.ran.append("mutation")

    ev, ran_regen = run_regeneration(ctx)
    res.evidence += ev
    if ran_regen:
        res.ran.append("regeneration")

    ev, compat_findings, gaps, ran_compat = run_compatibility(ctx)
    res.evidence += ev
    res.not_checked += gaps
    if ran_compat:
        res.ran.append("byte-diff")

    ctx.findings = verify_findings(ctx.findings, res.evidence, res.mutants)
    claimed = {f.location.split(":", 1)[0] for f in ctx.findings
               if f.lens == "test-quality" and f.verified and "mutation" in f.evidence_ids}
    compat_claimed = any(f.lens == "compatibility" and f.verified
                         and "compat-bytes" in f.evidence_ids for f in ctx.findings)
    res.findings = [*ctx.findings,
                    *[f for f in survivor_findings if f.location.split(":", 1)[0] not in claimed],
                    *([] if compat_claimed else compat_findings)]
    return res
