"""[4] Lenses: six independent reviewers, each one LLM call with minimal context (SPEC 5.4).

Entry point:

    run_all_lenses(req_id, req_text, hunks, routes, ledger_entries, test_dir_examples,
                   *, change_id="", areas=None, parallel=True) -> LensOutput

  hunks              list[models.Hunk] from intake (generated hunks are never sent to a lens)
  routes             list[models.RouteResult] (may be empty: then every non-generated hunk is in scope)
  ledger_entries     LedgerEntry objects or dicts, already filtered to the touched areas
  test_dir_examples  [{"path", "content"}] 1-2 short existing test files (see default_test_dir_examples)
  areas              {file path: area}; defaults to intake.area_for

A lens that cannot run (no key and no fixture, refusal, invalid output) is reported in
`not_checked`, never invented. Findings come back unverified; step [5] attaches receipts.
"""

from concurrent.futures import ThreadPoolExecutor
from dataclasses import asdict, dataclass, field

from ..models import Finding, Hunk, Interpretation, NotChecked, Question, Refusal, RouteResult
from ..questions import select_questions
from . import compatibility, logic_edge, product_context, requirement_tests, security, test_quality
from .base import EXECUTABLE_KINDS, CandidateQuestion, GeneratedTest, LensRun, run_lens

LENSES = [requirement_tests.SPEC, logic_edge.SPEC, compatibility.SPEC, security.SPEC,
          product_context.SPEC, test_quality.SPEC]
AMBIGUITY_LENSES = {"requirement-tests", "product-context"}
EXAMPLE_FILES = [
    "tests/MessagePack.SourceGenerator.Tests/MsgPack012InaccessibleDataTypeTests.cs",
    "tests/MessagePack.SourceGenerator.Tests/MultipleTypesTests.cs",
]


@dataclass
class LensOutput:
    findings: list[Finding] = field(default_factory=list)  # have an executable receipt plan; unverified
    hunches: list[Finding] = field(default_factory=list)  # code-citation only: shown as unverified hunches
    citations: list[Finding] = field(default_factory=list)  # ledger-citation: checked, deliberate per ledger
    questions: list[Question] = field(default_factory=list)  # after budget + dedupe
    candidate_questions: list[CandidateQuestion] = field(default_factory=list)  # everything lenses proposed
    generated_tests: list[GeneratedTest] = field(default_factory=list)
    ambiguity: bool = False
    interpretations: list[Interpretation] = field(default_factory=list)
    refusals: list[Refusal] = field(default_factory=list)
    not_checked: list[NotChecked] = field(default_factory=list)
    runs: list[LensRun] = field(default_factory=list)
    tokens: dict[str, dict] = field(default_factory=dict)  # lens -> {input, output, cost_usd, model, from_fixture}

    @property
    def total_cost_usd(self) -> float:
        return round(sum(t["cost_usd"] for t in self.tokens.values()), 6)

    def to_json(self) -> dict:
        def dump(xs):
            return [x.model_dump(mode="json") for x in xs]

        return {
            "findings": dump(self.findings), "hunches": dump(self.hunches), "citations": dump(self.citations),
            "questions": dump(self.questions), "candidate_questions": dump(self.candidate_questions),
            "generated_tests": dump(self.generated_tests), "ambiguity": self.ambiguity,
            "interpretations": dump(self.interpretations), "refusals": dump(self.refusals),
            "not_checked": dump(self.not_checked), "runs": [asdict(r) for r in self.runs],
            "tokens": self.tokens, "total_cost_usd": self.total_cost_usd,
        }


def _default_area(path: str) -> str:
    try:
        from ..intake import area_for
    except ImportError:
        return "tests" if path.startswith("tests/") else path.split("/")[1] if path.count("/") > 1 else "other"
    return area_for(path)


def default_test_dir_examples(read_file) -> list[dict]:
    """`read_file(repo_relative_path) -> str` (e.g. `git show <base>:<path>`)."""
    return [{"path": p, "content": read_file(p)} for p in EXAMPLE_FILES]


def run_all_lenses(req_id: str, req_text: str, hunks: list[Hunk], routes: list[RouteResult],
                   ledger_entries: list, test_dir_examples: list, *, change_id: str = "",
                   areas: dict[str, str] | None = None, parallel: bool = True) -> LensOutput:
    hunks = [h for h in hunks if h.kind != "generated"]
    routes_by_id = {r.hunk_id: r for r in routes}
    areas = areas or {h.file: _default_area(h.file) for h in hunks}
    change_id = change_id or req_id

    def one(spec):
        return run_lens(spec, req_id, req_text, hunks, routes_by_id, areas, ledger_entries,
                        test_dir_examples, change_id)

    if parallel:
        with ThreadPoolExecutor(max_workers=len(LENSES)) as pool:
            results = list(pool.map(one, LENSES))
    else:
        results = [one(s) for s in LENSES]

    out = LensOutput()
    interps_by_lens: dict[str, list[Interpretation]] = {}
    for run, parsed in results:
        out.runs.append(run)
        if run.input_tokens:
            out.tokens[run.lens] = {"model": run.model, "input": run.input_tokens, "output": run.output_tokens,
                                    "cost_usd": run.cost_usd, "ms": run.ms, "from_fixture": run.from_fixture}
        if run.status == "skipped":
            out.not_checked.append(NotChecked(what=f"{run.lens} lens", why=run.detail))
            continue
        if parsed is None:
            out.not_checked.append(NotChecked(what=f"{run.lens} lens", why=f"{run.status}: {run.detail}"))
            continue
        for f in parsed.findings:
            kind = f.receipt_plan.kind
            (out.findings if kind in EXECUTABLE_KINDS else out.citations if kind == "ledger-citation"
             else out.hunches).append(f)
        out.candidate_questions.extend(parsed.questions)
        out.generated_tests.extend(parsed.generated_tests)
        if parsed.ambiguity and run.lens in AMBIGUITY_LENSES:
            interps_by_lens[run.lens] = parsed.interpretations

    # requirement-tests wins when both lenses flag ambiguity: it reasoned from the requirement text alone.
    for lens in ("requirement-tests", "product-context"):
        if lens in interps_by_lens:
            out.ambiguity, out.interpretations = True, interps_by_lens[lens]
            flagged = " and ".join(sorted(interps_by_lens))
            out.refusals.append(Refusal(
                what=f"Which reading of {req_id} is intended (A or B)",
                why=f"{flagged} found two plausible readings that lead to different code; "
                    "a named human picks one.",
            ))
            break

    ordered = sorted(out.candidate_questions, key=lambda q: q.lens != "product-context")
    out.questions = select_questions(ordered, ledger_entries)
    return out
