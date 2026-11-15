import pytest

from receipts import config, llm
from receipts.lenses import LENSES, run_all_lenses
from receipts.lenses.base import LensOutputError, lens_schema, parse_lens_output
from receipts.models import Hunk, RouteResult

AREAS = ["type-collector", "tests"]


def H(i, file, kind):
    return Hunk(id=f"{file}#{i}", file=file, kind=kind, old_start=1, old_lines=1, new_start=1, new_lines=1,
                text=f"-old{i}\n+new{i}")


HUNKS = [
    H(1, "src/MessagePack.SourceGenerator/CodeAnalysis/TypeCollector.cs", "source"),
    H(1, "tests/MessagePack.SourceGenerator.Tests/GenericsFormatterTests.cs", "test"),
    H(1, "README.md", "docs"),
    H(1, "tests/MessagePack.SourceGenerator.Tests/Resources/X/Y.g.cs", "generated"),
]
AREA_MAP = {h.file: a for h, a in zip(HUNKS, ["type-collector", "tests", "docs", "tests"])}


def finding(kind="test", **kw):
    return {"claim": "c", "location": "src/A.cs:3", "severity": "high",
            "receipt_plan": {"kind": kind, "detail": "d"}, "verdict_depends_on_intent": False, **kw}


@pytest.fixture
def isolated(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "FIXTURES_LLM", tmp_path)
    monkeypatch.setattr(config, "ANTHROPIC_API_KEY", "")
    return tmp_path


def test_parse_valid_output():
    data = {
        "findings": [finding()],
        "questions": [{"text": "Opt-in?", "options": ["a", "b"], "why_it_matters": "w", "area": "tests",
                       "verdict_depends_on_intent": True}],
        "generated_tests": [{"path": "tests/MessagePack.SourceGenerator.Tests/Receipts/T.cs", "content": "class T {}",
                             "test_names": ["A"], "expects": "fail-on-base-pass-on-head"}],
        "ambiguity": True,
        "interpretations": [{"label": "B", "summary": "b"}, {"label": "A", "summary": "a"}],
    }
    p = parse_lens_output("requirement-tests", data, AREAS, "chg")
    assert p.findings[0].id == "requirement-tests-1" and p.findings[0].lens == "requirement-tests"
    assert not p.findings[0].verified
    assert p.questions[0].change_id == "chg" and p.questions[0].verdict_depends_on_intent
    assert p.generated_tests[0].test_names == ["A"]
    assert [i.label for i in p.interpretations] == ["A", "B"]


@pytest.mark.parametrize("data", [
    {},
    {"findings": [finding(severity="catastrophic")]},
    {"findings": [finding(kind="vibes")]},
    {"findings": [{"claim": "no location"}]},
    {"findings": [], "questions": [{"text": "q", "options": ["only one"], "area": "tests"}]},
    {"findings": [], "questions": [{"text": "q", "options": ["1", "2", "3", "4"], "area": "tests"}]},
    {"findings": [], "generated_tests": [{"path": "src/Evil.cs", "content": "x", "test_names": ["a"], "expects": "new-api"}]},
    {"findings": [], "generated_tests": [{"path": "tests/MessagePack.SourceGenerator.Tests/../../src/X.cs",
                                          "content": "x", "test_names": ["a"], "expects": "new-api"}]},
    {"findings": [], "generated_tests": [{"path": "tests/MessagePack.SourceGenerator.Tests/Receipts/T.cs",
                                          "content": " ", "test_names": [], "expects": "new-api"}]},
    {"findings": [], "ambiguity": True, "interpretations": [{"label": "A", "summary": "only one"}]},
])
def test_parse_rejects_contract_violations(data):
    with pytest.raises(LensOutputError):
        parse_lens_output("requirement-tests", data, AREAS, "chg")


def test_schemas_are_strict_and_use_no_unsupported_keywords():
    def walk(node):
        if isinstance(node, dict):
            assert not {"minimum", "maximum", "minLength", "maxLength", "minItems", "maxItems"} & set(node)
            if node.get("type") == "object":
                assert node.get("additionalProperties") is False
                assert set(node["required"]) == set(node["properties"])
            for v in node.values():
                walk(v)
        elif isinstance(node, list):
            for v in node:
                walk(v)

    for spec in LENSES:
        walk(lens_schema(spec, AREAS))


def test_mock_without_fixtures_reports_not_checked_cleanly(isolated, monkeypatch):
    monkeypatch.setattr(config, "MOCK_LLM", True)
    out = run_all_lenses("REQ-1", "req", HUNKS, [], [], [], parallel=False, areas=AREA_MAP)
    assert out.findings == [] and out.generated_tests == [] and out.tokens == {}
    assert len(out.not_checked) == 6
    assert all("unavailable" in n.why and "MOCK_LLM=1" in n.why for n in out.not_checked)


def test_no_key_raises_llm_unavailable_directly(isolated):
    with pytest.raises(llm.LLMUnavailable):
        llm.call_json("lens-x", config.MODEL_LENS, "s", "u", {"type": "object"})


def test_minimal_context_and_routing(isolated, monkeypatch):
    """Each lens sees only its scope; generated hunks never; router scores never in the prompt."""
    prompts: dict[str, str] = {}

    def fake_call_json(purpose, model, system, user, schema, **kw):
        prompts[purpose] = user
        data = {"findings": [], "questions": []}
        if purpose == "lens-requirement-tests":
            data |= {"findings": [finding("test"), finding("code-citation")], "generated_tests": [],
                     "ambiguity": True, "interpretations": [{"label": "A", "summary": "a"}, {"label": "B", "summary": "b"}]}
        elif purpose == "lens-logic-edge":
            data |= {"generated_tests": []}
        elif purpose == "lens-product-context":
            data |= {"findings": [finding("ledger-citation")], "ambiguity": False, "interpretations": [],
                     "questions": [{"text": "Opt-out setting?", "options": ["yes", "no"], "why_it_matters": "w",
                                    "area": "type-collector", "verdict_depends_on_intent": True}]}
        return llm.LLMResult(data, model, 100, 10, 5, False)

    monkeypatch.setattr(llm, "call_json", fake_call_json)
    low = RouteResult(hunk_id=HUNKS[0].id, risk=2, category="correctness", confidence=0.9, router="jev")
    out = run_all_lenses("REQ-1", "the requirement", HUNKS, [low], [], [{"path": "ex.cs", "content": "EXAMPLE"}],
                         parallel=False, areas=AREA_MAP)

    assert all(".g.cs" not in p for p in prompts.values())
    assert all("router risk" not in p and "jev" not in p for p in prompts.values())
    assert "GenericsFormatterTests" not in prompts["lens-requirement-tests"]  # change's tests withheld
    assert "EXAMPLE" in prompts["lens-requirement-tests"] and "EXAMPLE" not in prompts["lens-compatibility"]
    assert "GenericsFormatterTests" in prompts["lens-test-quality"]
    # risk 2 < threshold: logic-edge and security have nothing in scope and say so
    assert "lens-logic-edge" not in prompts and "lens-security" not in prompts
    assert {n.what for n in out.not_checked} == {"logic-edge lens", "security lens"}

    assert [f.receipt_plan.kind for f in out.findings] == ["test"]
    assert [f.receipt_plan.kind for f in out.hunches] == ["code-citation"]
    assert [f.receipt_plan.kind for f in out.citations] == ["ledger-citation"]
    assert out.ambiguity and [i.label for i in out.interpretations] == ["A", "B"] and len(out.refusals) == 1
    assert [q.text for q in out.questions] == ["Opt-out setting?"]
    assert out.tokens["requirement-tests"]["cost_usd"] == llm.cost_usd(config.MODEL_DEEP, 100, 10)
    assert out.to_json()["total_cost_usd"] == out.total_cost_usd


def test_invalid_lens_output_is_not_checked_not_invented(isolated, monkeypatch):
    monkeypatch.setattr(llm, "call_json",
                        lambda purpose, model, *a, **k: llm.LLMResult({"oops": 1}, model, 5, 5, 1, False))
    out = run_all_lenses("REQ-1", "req", HUNKS, [], [], [], parallel=False, areas=AREA_MAP)
    assert out.findings == [] and len(out.not_checked) == 6
    assert all(n.why.startswith("invalid") for n in out.not_checked)
    assert out.tokens["security"]["input"] == 5  # tokens were spent and are still reported


def test_requirement_tests_prompt_tests_criteria_at_full_scope_and_anchors_gaps_in_code():
    # A criterion the change leaves out has no changed line. Anchored on a README line, its failing
    # test cannot back it (checks.test_backs refuses docs locations), so it stays a hunch.
    from receipts.lenses.requirement_tests import SYSTEM

    assert "acceptance criteria" in SYSTEM and "AS WRITTEN, at its full scope" in SYSTEM
    assert "even when the diff does not touch that file" in SYSTEM
    assert "Never put such a claim on a docs or README line" in SYSTEM
