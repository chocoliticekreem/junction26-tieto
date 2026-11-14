from receipts import oracle
from receipts.models import LedgerEntry, Question

REQ = ("The analyzer throws IndexOutOfRangeException on a closed generic struct from a referenced "
       "assembly. The generator must produce the resolver and the analyzer must not crash.")


def q(text, options, area="analyzer", why=""):
    return Question(id="q-product-context-1", change_id="c", area=area, lens="product-context",
                    text=text, options=options, why_it_matters=why)


def test_conventions_parse_all_three():
    convs = {c.key: c for c in oracle.load_conventions()}
    assert set(convs) == {"A", "B", "C"}
    assert convs["C"].used_by == ["mock-2283-bait"]
    assert "referenced assemblies" in convs["C"].entry
    assert set(convs["B"].used_by) == {"mock-2234-bait", "mock-2297-bait"}


def test_bait_question_answered_as_deliberate():
    question = q("Member-key diagnostics are skipped for types with no syntax in this compilation "
                 "(declared in referenced assemblies). Is that deliberate, or should the analyzer "
                 "report them?", ["Deliberate", "Report them"])
    text, basis = oracle.answer(question, "mock-2283-bait", REQ)
    assert basis == "convention C"
    assert text.startswith("Deliberate") and "referenced assemblies" in text


def test_non_convention_question_picks_option_closest_to_requirement():
    question = q("Which output is expected for the failing case?",
                 ["The generator produces the resolver and the analyzer does not crash",
                  "Emit a warning and skip"], area="tests")
    text, basis = oracle.answer(question, "mock-2283-logic", REQ)
    assert basis == "requirement"
    assert text.startswith("The generator produces the resolver")


def test_no_clear_option_means_no_preference():
    question = q("Should the log line be blue or green?", ["Blue", "Green"], area="docs")
    assert oracle.answer(question, "mock-2283-logic", REQ) == (oracle.NO_PREFERENCE, "none")


def test_negated_requirement_words_count_against_an_option():
    req = ("I would like all generated code to be decorated with the attribute. "
           "I do not feel like a setting is needed.")
    options = ["Decorate all generated code unconditionally", "Add an opt-out setting"]
    assert oracle.pick_option(options, req) == options[0]


def test_generic_vocabulary_does_not_trigger_a_convention():
    # Shares generic words (generated, attribute, guard, README) with convention B but none of its
    # anchors (SuppressSourceGeneration, opting out, MsgPack016...).
    question = q("Should the attribute still be emitted on netstandard2.0 by some other route, given "
                 "the README documents the guard for generated code?", ["Skip", "Emit"],
                 area="codegen-templates")
    assert oracle.matching_convention(question, "mock-2297-bait", oracle.load_conventions()) is None


def test_each_bait_convention_is_recognised_on_its_own_case():
    convs = oracle.load_conventions()
    cases = {
        "mock-2129-bait": ("The new attribute is marked [Conditional(\"NEVERDEFINED\")], so it is "
                           "not emitted into the user's assembly. Is that intended?", "A"),
        "mock-2234-bait": ("The new continue skips KeyAttribute-derived keys on suppressed types. Is "
                           "skipping MsgPack016 deliberate when SuppressSourceGeneration is true?", "B"),
        "mock-2283-bait": ("Member-key diagnostics are skipped for types declared in a referenced "
                           "assembly. Is it deliberate that the consuming project does not report "
                           "them, leaving it to the defining project?", "C"),
    }
    for case, (text, key) in cases.items():
        conv = oracle.matching_convention(q(text, ["Yes", "No"]), case, convs)
        assert conv is not None and conv.key == key, case


def test_answer_questions_appends_labelled_entries_and_skips_answered():
    written: list[LedgerEntry] = []
    q1 = q("Should the log line be blue or green?", ["Blue", "Green"], area="docs")
    q2 = q("Is the exit code 3 or 4?", ["3", "4"], area="tests")
    ledger = [{"area": "tests", "question": "Is the exit code 3 or 4?"}]
    out = oracle.answer_questions([q1, q2], "mock-2283-clean", REQ, ledger, written.append)
    assert len(written) == 1 and written[0].answered_by == oracle.ORACLE
    assert written[0].change_id == "mock-2283-clean" and written[0].question_id == q1.id
    assert out[0]["basis"] == "none"
