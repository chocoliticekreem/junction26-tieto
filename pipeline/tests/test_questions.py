from receipts.lenses.base import CandidateQuestion
from receipts.models import LedgerEntry
from receipts.questions import load_ledger, normalise, same_question, select_questions


def cq(text, area="codegen-templates", intent=True, i=0):
    return CandidateQuestion(id=f"q{i}", change_id="c", area=area, lens="product-context", text=text,
                             options=["yes", "no"], why_it_matters="flips", verdict_depends_on_intent=intent)


LEDGER = [LedgerEntry(question="Should the attribute be emitted unconditionally?", answer="yes",
                      area="codegen-templates", answered_by="Anson", change_id="ju26-issue-2297")]


def test_normalise_ignores_case_punctuation_whitespace():
    assert normalise("  Should it, be ON?? ") == "should it be on"


def test_same_question_matches_rewording_but_not_different_question():
    assert same_question("Should the attribute be emitted unconditionally?",
                         "should the attribute be emitted  unconditionally")
    assert same_question("Should the generated resolver attribute be emitted unconditionally on all targets?",
                         "Should the generated resolver attribute really be emitted unconditionally on all targets?")
    assert not same_question("Should the attribute be emitted unconditionally?",
                             "Should diagnostics for metadata types be reported?")


def test_does_not_reask_answered_question_in_same_area():
    out = select_questions([cq("should the attribute be EMITTED unconditionally")], LEDGER)
    assert out == []


def test_same_question_in_other_area_is_asked():
    out = select_questions([cq("Should the attribute be emitted unconditionally?", area="analyzer")], LEDGER)
    assert [q.area for q in out] == ["analyzer"]


def test_only_intent_dependent_questions_survive():
    out = select_questions([cq("Is this nice?", intent=False), cq("Opt-in or opt-out?", i=1)], [])
    assert [q.text for q in out] == ["Opt-in or opt-out?"]


def test_budget_caps_and_dedupes_within_change():
    cands = [cq("Opt-in or opt-out?", i=0), cq("opt in or opt out", i=1)] + [cq(f"Question number {n}?", i=n + 2) for n in range(5)]
    out = select_questions(cands, [], budget=3)
    assert len(out) == 3
    assert [q.text for q in out] == ["Opt-in or opt-out?", "Question number 0?", "Question number 1?"]
    assert all(type(q).__name__ == "Question" for q in out)


def test_ledger_as_dicts_and_convention_entries_without_question(tmp_path):
    p = tmp_path / "ledger.jsonl"
    p.write_text(
        '{"question": "Opt-in or opt-out?", "answer": "opt-out", "area": "codegen-templates"}\n'
        '{"area": "analyzer", "answer": "metadata diagnostics are deliberately not reported"}\n'
    )
    ledger = load_ledger(p)
    assert len(ledger) == 2
    assert select_questions([cq("opt-in or opt-out"), cq("New one?", i=1)], ledger)[0].text == "New one?"


def test_load_ledger_missing_file_is_empty(tmp_path):
    assert load_ledger(tmp_path / "nope.jsonl") == []
