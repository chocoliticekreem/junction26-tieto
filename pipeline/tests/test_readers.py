"""Requirement-reading panel: vote merge, deterministic pair choice, ledger suppression, no diff."""

import pytest

from receipts import llm, stages
from receipts.lenses import readers
from receipts.models import Hunk, LedgerEntry

REQ = "REQ-9"
REQ_TEXT = ("Add the attribute to generated code. I do not feel like a setting is needed, "
            "but a setting would be fine to have too.")
DIFF_MARKER = "+UNIQUE_DIFF_LINE_ExcludeFromCodeCoverage_42"
HUNKS = [Hunk(id="f#1", file="src/X.tt", kind="template", old_start=1, old_lines=1, new_start=1,
              new_lines=2, text=f" a\n{DIFF_MARKER}")]
AREAS = {"src/X.tt": "codegen-templates"}

ALWAYS = {"summary": "Always add the attribute to every generated type, no setting",
          "code_difference": "templates emit the attribute unconditionally"}
OPT_OUT = {"summary": "Add the attribute by default with an opt-out setting",
           "code_difference": "a new boolean option on the resolver attribute gates the emission"}
SCOPE = {"summary": "Only formatter classes get the attribute, resolvers stay unannotated",
         "code_difference": "resolver template is left unchanged"}


def _run(name, readings, flagged, quote=""):
    return readers.ReaderRun(reader=name, status="ok", flagged=flagged,
                             data={"readings": readings, "materially_different": flagged,
                                   "quote": quote})


def test_two_of_three_is_ambiguity():
    runs = [_run("maintainer", [ALWAYS, OPT_OUT], True), _run("issue-author", [ALWAYS], False),
            _run("qa-engineer", [OPT_OUT, ALWAYS], True)]
    votes, ambiguity, best, support = readers.merge(runs)
    assert (votes, ambiguity) == (2, True)
    assert best == 0  # both pairs match each other; tie broken by reader order
    assert support == {"maintainer": 1, "qa-engineer": 1}


def test_one_of_three_is_no_ambiguity():
    runs = [_run("maintainer", [ALWAYS, OPT_OUT], True), _run("issue-author", [ALWAYS], False),
            _run("qa-engineer", [ALWAYS, OPT_OUT], False)]
    votes, ambiguity, best, _ = readers.merge(runs)
    assert (votes, ambiguity, best) == (1, False, None)


def test_unavailable_reader_never_counts_as_a_vote():
    down = readers.ReaderRun(reader="issue-author", status="unavailable", detail="no key")
    runs = [_run("maintainer", [ALWAYS, OPT_OUT], True), down, _run("qa-engineer", [ALWAYS], False)]
    assert readers.merge(runs)[:2] == (1, False)


def test_pair_choice_prefers_the_shared_pair_and_is_deterministic():
    # Reader 0 forks on scope; readers 1 and 2 share the setting fork, so their pair wins.
    runs = [_run("maintainer", [ALWAYS, SCOPE], True), _run("issue-author", [ALWAYS, OPT_OUT], True),
            _run("qa-engineer", [OPT_OUT, ALWAYS], True)]
    first = readers.merge(runs)
    assert all(readers.merge(runs) == first for _ in range(5))
    votes, ambiguity, best, support = first
    assert (votes, ambiguity, best) == (3, True, 1)
    assert support["issue-author"] == support["qa-engineer"] > support["maintainer"]


def test_pair_similarity_is_order_insensitive():
    assert readers.pair_similarity((ALWAYS, OPT_OUT), (OPT_OUT, ALWAYS)) == pytest.approx(1.0)
    assert readers.pair_similarity((ALWAYS, OPT_OUT), (ALWAYS, SCOPE)) < 1.0


@pytest.fixture
def fake_llm(monkeypatch):
    """Readers 1 and 3 flag the setting fork unless a ledger answer is in the prompt; the
    orientation call says the change implements the SECOND reading."""
    calls: list[tuple[str, str, str]] = []

    def fake_call_json(purpose, model, system, user, schema, **kw):
        calls.append((purpose, system, user))
        if purpose == "readers-orient":
            return llm.LLMResult({"implements": "second", "why": "no option in the diff"}, model,
                                 100, 10, 1, False)
        answered = "answered_questions_data" in user
        flag = not answered and "issue author" not in system and "filed this issue" not in system
        data = ({"readings": [OPT_OUT, ALWAYS], "materially_different": True,
                 "quote": "I do not feel like a setting is needed, but a setting would be fine "
                          "to have too."}
                if flag else {"readings": [ALWAYS], "materially_different": False, "quote": ""})
        return llm.LLMResult(data, model, 300, 200, 1, False)

    monkeypatch.setattr(llm, "call_json", fake_call_json)
    return calls


def test_panel_orients_a_to_the_change_and_quotes_the_requirement(fake_llm):
    res = readers.run_panel(REQ, REQ_TEXT, [], HUNKS, AREAS)
    assert res.ambiguity and res.votes == 2
    assert [i.label for i in res.interpretations] == ["A", "B"]
    assert res.interpretations[0].summary.startswith(ALWAYS["summary"])  # swapped by orientation
    assert res.interpretations[1].summary.startswith(OPT_OUT["summary"])
    assert res.refusal.why.startswith("2 of 3 independent readers found two readings: A: Always")
    assert ("the requirement says: 'I do not feel like a setting is needed, but a setting would "
            "be fine to have too'") in res.refusal.why
    assert res.to_json()["vote"] == {"flagged": 2, "of": 3, "ambiguity": True,
                                     "text": "2 of 3 independent readers found two readings"}


def test_unoriented_change_is_not_labelled_as_reading_a(fake_llm, monkeypatch):
    real = llm.call_json

    def both(purpose, model, system, user, schema, **kw):
        if purpose == "readers-orient":
            return llm.LLMResult({"implements": "both", "why": "ships both modes"}, model,
                                 1, 1, 1, False)
        return real(purpose, model, system, user, schema, **kw)

    monkeypatch.setattr(llm, "call_json", both)
    res = readers.run_panel(REQ, REQ_TEXT, [], HUNKS, AREAS)
    a, b = res.interpretations
    assert a.summary.startswith("Branch A is the change as written")
    assert f"Closest reading: {OPT_OUT['summary']}" in a.summary  # reader order kept
    assert b.summary.startswith(ALWAYS["summary"])
    assert "both: ships both modes" in res.refusal.why


def test_reader_prompts_contain_no_diff_text(fake_llm):
    readers.run_panel(REQ, REQ_TEXT, [], HUNKS, AREAS)
    reader_calls = [c for c in fake_llm if c[0] == "readers"]
    assert len(reader_calls) == 3
    assert len({c[1] for c in reader_calls}) == 3  # three distinct framings
    for _, system, user in reader_calls:
        assert DIFF_MARKER not in user and DIFF_MARKER not in system
        assert "src/X.tt" not in user and "hunks_data" not in user
    orient = [c for c in fake_llm if c[0] == "readers-orient"]
    assert len(orient) == 1 and DIFF_MARKER in orient[0][2]  # only the orientation sees the diff


def test_ledger_answer_suppresses_ambiguity(fake_llm):
    answer = LedgerEntry(question="Should the attribute be unconditional or behind an opt-out?",
                         answer="Unconditional, no setting", area="codegen-templates",
                         answered_by="Anson", change_id="c", question_id="q1")
    convention = {"area": "codegen-templates", "text": "Generated code targets netstandard2.0"}
    res = readers.run_panel(REQ, REQ_TEXT, [convention, answer.model_dump()], HUNKS, AREAS)
    assert not res.ambiguity and res.interpretations == [] and res.refusal is None
    assert res.ledger_answers == 1
    user = next(c[2] for c in fake_llm if c[0] == "readers")
    assert "Unconditional, no setting" in user
    assert "netstandard2.0" not in user  # convention bullets are not requirement answers


def test_panel_vote_replaces_lens_ambiguity_in_run_lenses(tmp_path, monkeypatch):
    """A lens flagging ambiguity no longer decides: with 1 of 3 readers flagging there is no A/B."""
    from test_stage_lenses import _ctx

    from receipts import config

    monkeypatch.setattr(config, "CONTEXT", tmp_path / "context")
    config.CONTEXT.mkdir()
    (config.CONTEXT / "REVIEW_CONTEXT.md").write_text("# Review context\n")
    seen = {"n": 0}

    def fake_call_json(purpose, model, system, user, schema, **kw):
        if purpose == "readers":
            seen["n"] += 1
            flag = seen["n"] == 1
            data = ({"readings": [ALWAYS, OPT_OUT], "materially_different": True, "quote": ""}
                    if flag else {"readings": [ALWAYS], "materially_different": False, "quote": ""})
            return llm.LLMResult(data, model, 1, 1, 1, False)
        data = {"findings": [], "questions": []}
        if purpose in ("lens-requirement-tests", "lens-product-context"):
            data |= {"ambiguity": True, "interpretations": [{"label": "A", "summary": "a"},
                                                            {"label": "B", "summary": "b"}]}
        if purpose in ("lens-requirement-tests", "lens-logic-edge"):
            data |= {"generated_tests": []}
        return llm.LLMResult(data, model, 1, 1, 1, False)

    monkeypatch.setattr(llm, "call_json", fake_call_json)
    ctx = _ctx(tmp_path)
    res = stages.run_lenses(ctx)
    assert res.interpretations == []
    assert not any("(A or B)" in r.what for r in res.refused)
    import json

    lenses = json.loads((ctx.run_dir / "lenses.json").read_text())
    assert lenses["ambiguity"] is True and lenses["readers_ambiguity"] is False
    assert json.loads((ctx.run_dir / "readers.json").read_text())["vote"]["flagged"] == 1
