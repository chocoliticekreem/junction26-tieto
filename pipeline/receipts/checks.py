"""[5] Receipt checks (SPEC 5.5): every check executes something and returns Evidence.

  requirement tests  ctx.generated_tests run on base and head (fail-before-pass-after / new-api)
  mutation           mutants on added lines of risky source hunks vs the change's own tests;
                     survivors re-run against the full test project
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
from .intake import classify
from .models import Evidence, Finding, Mutant, NotChecked, ReceiptPlan, TestRun
from .mutants import OPERATORS, MutantSpec, generate
from .stages import ReviewContext, StageResult

MUTANT_TIMEOUT_S = 180
FIXTURES_REQ_TESTS = config.ROOT / "fixtures" / "requirement-tests"
TEST_PROJECT_DIR = str(Path(config.TEST_PROJECT).parent)
RESOURCES_PREFIX = f"{TEST_PROJECT_DIR}/Resources/"
COMPAT_PREFIXES = ("src/MessagePack/", "src/MessagePack.Annotations/")
COMPAT_NOT_CHECKED = "compatibility byte diff (base vs head serialized bytes)"
CODEGEN_AREAS = {"codegen-templates", "source-generator"}
CODEGEN_NOT_CHECKED = "generated formatter bytes (source generator output, base vs head)"
CODEGEN_WHY = ("generated formatter bytes not compared: the harness exercises runtime resolvers, "
               "not source-generated formatters")


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


def two_tier_status(own: tuple[str, list[str]],
                    suite: tuple[str, list[str]] | None) -> tuple[str, list[str]]:
    """Fate of a mutant from its run on the change's own tests and, for a survivor, its re-run on
    the full test project. Without a suite run (no own test class: the first run already was the
    full project) a survivor stays survived."""
    if own[0] != "survived" or suite is None:
        return own
    if suite[0] == "killed":
        return "killed-by-suite", suite[1]
    return suite


def mutation_findings(mutants: list[Mutant]) -> list[Finding]:
    """Medium finding per full-suite survivor; one low finding per file for mutants that only the
    wider suite catches (one per file keeps a list of lines from flooding the findings panel)."""
    out = [Finding(
        id=f"mutation-survivor-{m.id}", lens="test-quality",
        claim=f"no test in the suite detects {m.operator} at {m.file}:{m.line}",
        location=f"{m.file}:{m.line}", severity="medium",
        receipt_plan=ReceiptPlan(kind="mutation",
                                 detail=f"{m.original} -> {m.mutated or '(statement deleted)'}"),
        verified=True, evidence_ids=["mutation"]) for m in mutants if m.status == "survived"]
    by_file: dict[str, list[Mutant]] = {}
    for m in mutants:
        if m.status == "killed-by-suite":
            by_file.setdefault(m.file, []).append(m)
    for file, ms in by_file.items():
        lines = sorted({m.line for m in ms})
        where = f"{file}:{', '.join(str(n) for n in lines)}"
        out.append(Finding(
            id=f"mutation-suite-only-{Path(file).stem}", lens="test-quality",
            claim=(f"the change's own tests do not catch mutants at {where}; "
                   "the existing suite does"),
            location=f"{file}:{lines[0]}", severity="low",
            receipt_plan=ReceiptPlan(kind="mutation", detail="; ".join(
                f"{Path(m.file).name}:{m.line} {m.operator}: {m.original} -> "
                f"{m.mutated or '(statement deleted)'}" for m in ms)),
            verified=True, evidence_ids=["mutation"]))
    return out


def run_mutation(ctx: ReviewContext) -> tuple[list[Evidence], list[Mutant], list[NotChecked],
                                              bool]:
    """Findings come from mutation_findings once lens claims have taken their mutants."""
    if ctx.skip_mutation:
        return [], [], [], False
    if not ctx.head_build_ok or ctx.head_wt is None:
        return [], [], [NotChecked(what="mutation testing", why="head does not build")], False
    specs = mutation_targets(ctx)
    if not specs:
        risky = sum(1 for r in ctx.routing.routes if r.risk >= config.MUTATION_RISK_THRESHOLD
                    and r.category not in ("generated", "docs", "tests"))
        why = (f"{risky} source hunk(s) routed at risk >= {config.MUTATION_RISK_THRESHOLD}, but "
               "none has an added line the mutation operators apply to" if risky else
               f"no changed source hunk routed at risk >= {config.MUTATION_RISK_THRESHOLD} "
               "(mutation threshold)")
        return [], [], [NotChecked(what="mutation testing", why=why)], False

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
        return [ev], [], [], True

    wt = sandbox.worktree("mut", ctx.head_sha, name=f"mut-{ctx.change_id}")
    mutants: list[Mutant] = []
    command_ids: list[str] = []
    with sandbox.lock(wt):
        sandbox.reset(wt)
        for n, spec in enumerate(specs, start=1):
            rid = f"mutant-{n}"
            try:
                _apply(wt, spec)
                own = _mutant_run(ctx, wt, rid, flt, no_build=False)
                command_ids.append(rid)
                suite = None
                if own[0] == "survived" and flt:
                    # Same mutated build, whole test project: separates "the change's tests miss
                    # it" from "nothing in the suite detects it" (notes/stryker.md).
                    suite = _mutant_run(ctx, wt, f"{rid}-suite", None, no_build=True)
                    command_ids.append(f"{rid}-suite")
            finally:
                sandbox.reset(wt)
            status, killing = two_tier_status(own, suite)
            mutants.append(Mutant(id=f"m{n}", file=spec.file, line=spec.line,
                                  operator=spec.operator, original=spec.original.strip(),
                                  mutated=spec.mutated.strip(), status=status,
                                  killing_tests=killing[:10]))
        sandbox.reset(wt)

    counts = {s: sum(m.status == s for m in mutants) for s in MUTANT_STATUSES}
    if counts["killed"] + counts["killed-by-suite"] + counts["survived"] == 0:
        result = "error"
    elif counts["survived"]:
        result = "partial"
    else:
        result = "pass"
    files = sorted({m.file for m in mutants})
    title = (f"Mutation: {counts['killed']} killed by the change's tests, "
             f"{counts['killed-by-suite']} caught only by the existing suite, "
             f"{counts['survived']} survive the full suite, "
             f"{counts['compile-error']} compile errors, {counts['timeout']} timeouts "
             f"({len(mutants)} mutants on {', '.join(Path(f).name for f in files)})")
    survivors = [m for m in mutants if m.status == "survived"]
    ev = Evidence(
        id="mutation", kind="mutation", title=title, result=result, command_ids=command_ids,
        details={**counts, "total": len(mutants), "test_scope": scope, "test_filter": flt or "",
                 "suite_scope": (f"full {TEST_PROJECT_DIR}, re-run for each mutant the change's "
                                 "own tests miss" if flt else "same as test_scope"),
                 "risk_threshold": config.MUTATION_RISK_THRESHOLD,
                 "mutants_per_hunk_cap": config.MUTANTS_PER_HUNK,
                 "survivors": [f"{m.file}:{m.line} {m.operator}" for m in survivors],
                 "killed_by_suite": [f"{m.file}:{m.line} {m.operator}" for m in mutants
                                     if m.status == "killed-by-suite"]},
        excerpt="\n".join(f"survived the full suite: {m.operator} at {Path(m.file).name}:"
                          f"{m.line}: {m.original} -> {m.mutated or '(deleted)'}"
                          for m in survivors[:5]))
    return [ev], mutants, [], True


MUTANT_STATUSES = ("killed", "killed-by-suite", "survived", "compile-error", "timeout")


def _mutant_run(ctx: ReviewContext, wt: Path, rid: str, flt: str | None,
                no_build: bool) -> tuple[str, list[str]]:
    run = sandbox.test(wt, ctx.run_dir, rid, "head", filter=flt, commands=ctx.commands,
                       timeout=MUTANT_TIMEOUT_S, no_build=no_build)
    ctx.tests.append(run)
    receipt = next(c for c in reversed(ctx.commands) if c.id == rid)
    log = (ctx.run_dir / receipt.log_path).read_text(errors="replace")
    return mutant_status(run, receipt.exit_code, log)


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
    failing_files = sorted(p for p, cs in mapping.items() if any(c.outcome != "passed" for c in cs))
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
            "failing_mapped_tests": failing_mapped[:50], "failing_files": failing_files[:50],
            "snapshot_tests": len(tests_used),
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
    """The harness serializes through the runtime library, so it only runs when that changed."""
    return any(f.path.startswith(COMPAT_PREFIXES) and f.kind == "source"
               for f in ctx.intake.files)


def codegen_changed(ctx: ReviewContext) -> bool:
    return any(f.area in CODEGEN_AREAS and f.kind in ("source", "template")
               for f in ctx.intake.files)


def run_compatibility(ctx: ReviewContext) -> tuple[list[Evidence], list[Finding],
                                                   list[NotChecked], bool]:
    gaps: list[NotChecked] = []
    if codegen_changed(ctx):
        gaps.append(NotChecked(what=CODEGEN_NOT_CHECKED, why=CODEGEN_WHY))
    if not compat_needed(ctx):
        compat_routes = any(r.category == "compatibility" for r in ctx.routing.routes)
        why = ("compatibility-relevant hunks changed, but none in src/MessagePack or "
               "src/MessagePack.Annotations; the harness only compares the runtime library"
               if compat_routes else "no runtime/public API change; byte diff not needed")
        return [], [], [*gaps, NotChecked(what=COMPAT_NOT_CHECKED, why=why)], False
    if ctx.base_wt is None or ctx.head_wt is None:
        return [], [], [*gaps, NotChecked(what=COMPAT_NOT_CHECKED,
                                          why="worktrees unavailable")], False
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
    return [ev], findings, gaps, True


# Mapping lens findings to evidence -----------------------------------------------------------


def verify_findings(findings: list[Finding], evidence: list[Evidence],
                    mutants: list[Mutant]) -> list[Finding]:
    """Attach evidence to lens findings whose receipt plan an executed artifact confirms."""
    """Each mapping ties the finding's own claim to an artifact that shows THAT problem; a failing
    artifact about something else never verifies it (it stays an unverified hunch)."""
    by_kind: dict[str, list[Evidence]] = {}
    for e in evidence:
        by_kind.setdefault(e.kind, []).append(e)
    test_ev = [e for e in by_kind.get("fail-before-pass-after", []) + by_kind.get("new-api", [])
               if e.result == "fail" and e.details.get("failed_on_head")]
    out: list[Finding] = []
    for f in findings:
        if f.verified and f.evidence_ids:
            out.append(f)
            continue
        ids: list[str] = []
        plan = f.receipt_plan.kind
        if plan == "test" and f.lens in ("requirement-tests", "logic-edge"):
            ids = [e.id for e in test_ev if test_backs(f, e)]
        elif plan == "mutation":
            backing = matching_survivors(f, mutants)
            if backing:
                ids = ["mutation"]
                # A surviving mutant proves a test gap, not a defect in the code; one the wider
                # suite catches is only a gap in the change's own tests.
                cap = ("medium" if any(m.status == "survived" for m in backing) else "low")
                if SEVERITY_RANK[f.severity] > SEVERITY_RANK[cap]:
                    f = f.model_copy(update={"severity": cap})
        elif plan == "byte-diff":
            ids = [e.id for e in by_kind.get("byte-diff", []) if byte_diff_backs(f, e)]
        elif plan == "regeneration":
            ids = [e.id for e in by_kind.get("regeneration", []) if regeneration_backs(f, e)]
        if ids:
            f = f.model_copy(update={"verified": True, "evidence_ids": ids})
        out.append(f)
    return out


def _names(text: str, word: str) -> bool:
    return bool(word) and re.search(rf"(?<![\w]){re.escape(word)}(?![\w])", text) is not None


def test_backs(f: Finding, e: Evidence) -> bool:
    """A failing generated test backs a test-plan finding only when the finding's receipt plan names
    that exact test (method name, whole word) and the test ran and failed on head. A test exercises
    code, so it never proves a claim about documentation."""
    if e.details.get("lens") != f.lens or classify(f.location.split(":", 1)[0]) == "docs":
        return False
    return any(_names(f.receipt_plan.detail, _short(n)) for n in e.details["failed_on_head"])


SUFFIXES = ("Formatter", "Resolver", "Attribute", "Helper", "Extensions")


def _compat_tokens(path: str) -> set[str]:
    stem = Path(path).stem.split(".")[0]
    tokens = {stem.lower()}
    for s in SUFFIXES:
        if stem.endswith(s) and len(stem) > len(s):
            tokens.add(stem[: -len(s)].lower())
    return tokens


def byte_diff_backs(f: Finding, e: Evidence) -> bool:
    """A failing byte diff backs a compatibility finding only when what differs (a sample case, a
    base payload that no longer reads, a removed public API member) traces to the runtime or
    annotations file the finding names."""
    path = f.location.split(":", 1)[0]
    if e.result != "fail" or f.lens != "compatibility" or not path.startswith(COMPAT_PREFIXES):
        return False
    tokens = _compat_tokens(path)
    d = e.details
    cases = [c["case"] for c in d.get("differing", [])] + [
        c["case"] for c in d.get("roundtrip_failures", [])]
    if any(c.split("/", 1)[0].lower() in tokens for c in cases):
        return True
    return any(t in line.lower() for line in d.get("api_removed", []) for t in tokens)


def regeneration_backs(f: Finding, e: Evidence) -> bool:
    """A failing regeneration check backs a finding only when a snapshot test that verifies a
    generated file failed on head and the finding names that test, its snapshot folder or file."""
    path = f.location.split(":", 1)[0]
    if e.result != "fail" or not (path.startswith("src/MessagePack.SourceGenerator/")
                                  or path.startswith(RESOURCES_PREFIX)):
        return False
    text = f"{f.location} {f.receipt_plan.detail}"
    failing = e.details.get("failing_mapped_tests", [])
    if any(_names(text, _method_and_args(t)[0]) for t in failing):
        return True
    return any(p in text for p in e.details.get("failing_files", []))


def refuted_findings(findings: list[Finding], evidence: list[Evidence]) -> dict[str, str]:
    """Unverified test-plan findings whose own generated test ran and passed on head: the claimed
    defect did not show. Finding id -> the evidence id that refutes it."""
    tests = [e for e in evidence if e.kind in ("fail-before-pass-after", "new-api")
             and e.result in ("pass", "partial") and e.details.get("test_names")]
    out: dict[str, str] = {}
    for f in findings:
        if f.verified or f.receipt_plan.kind != "test":
            continue
        text = f"{f.receipt_plan.detail} {f.location} {f.claim}"
        same_lens = [e for e in tests if e.details.get("lens") == f.lens]
        # Only a test the finding names refutes it; removing a claim needs a direct link.
        named = [e for e in same_lens if any(_short(n) in text for n in e.details["test_names"])]
        if named:
            out[f.id] = named[0].id
    return out


MUTANT_LINE_WINDOW = 2
SEVERITY_RANK = {"low": 0, "medium": 1, "high": 2, "critical": 3}
OWN_TESTS_MISS = ("survived", "killed-by-suite")


def matching_survivors(f: Finding, mutants: list[Mutant]) -> list[Mutant]:
    """Mutants the change's own tests miss (survived, or caught only by the wider suite) that
    back a mutation-plan finding: same file, within MUTANT_LINE_WINDOW lines of a line the finding
    names, and the same operator when the finding names one."""
    path = f.location.split(":", 1)[0]
    text = f"{f.location} {f.receipt_plan.detail}"
    lines = {int(n) for n in re.findall(re.escape(Path(path).name) + r":(\d+)", text)}
    ops = {op for op in OPERATORS if op in f.receipt_plan.detail}
    return [m for m in mutants
            if m.status in OWN_TESTS_MISS and m.file == path
            and any(abs(m.line - n) <= MUTANT_LINE_WINDOW for n in lines)
            and (not ops or m.operator in ops)]


def _short(test_name: str) -> str:
    return test_name.split("(", 1)[0].rsplit(".", 1)[-1]


# Stage ---------------------------------------------------------------------------------------


def run_receipt_checks(ctx: ReviewContext) -> StageResult:
    res = StageResult()

    ev, gaps, ran = run_generated_tests(ctx)
    res.evidence += ev
    res.not_checked += gaps
    res.ran += sorted(ran)

    ev, mutants, gaps, ran_mut = run_mutation(ctx)
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
    refuted = refuted_findings(ctx.findings, res.evidence)
    for e in res.evidence:
        gone = [f for f in ctx.findings if refuted.get(f.id) == e.id]
        if gone:
            e.details["refutes"] = [{"id": f.id, "lens": f.lens, "claim": f.claim} for f in gone]
            e.title += f"; refutes {len(gone)} lens claim(s): {', '.join(f.id for f in gone)}"
    ctx.findings = [f for f in ctx.findings if f.id not in refuted]
    claimed = {m.id for f in ctx.findings
               if f.verified and "mutation" in f.evidence_ids
               for m in matching_survivors(f, res.mutants)}
    compat_claimed = any(f.lens == "compatibility" and f.verified
                         and "compat-bytes" in f.evidence_ids for f in ctx.findings)
    res.findings = [*ctx.findings,
                    *mutation_findings([m for m in res.mutants if m.id not in claimed]),
                    *([] if compat_claimed else compat_findings)]
    return res
