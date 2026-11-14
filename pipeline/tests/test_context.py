import pytest

from receipts import config, context
from receipts.models import LedgerEntry

SOURCES = {
    "README.md": (
        "# Lib\n\nLib is a fast serializer for C#. It ships with compression support built in.\n\n"
        "## Installation\n\nThis library is distributed via NuGet. Install the package first.\n\n"
        "## Analyzer\n\nThe analyzer only reports on types in your project. It must be installed separately.\n\n"
        "## Object Serialization\n\nLib can serialize your own public class or struct types.\n\n"
        "## Security\n\nUntrusted data should use the secure options by default.\n"
    ),
    "CONTRIBUTING.md": "# Contributing\n\n## How to Build\n\nOpen the solution in Visual Studio 2022.\n",
    "doc/analyzers/MsgPack004.md": (
        "# MsgPack004 Attribute public members\n\nPublic members require a Key or IgnoreMember attribute.\n"
    ),
    "src/MessagePack.SourceGenerator/AnalyzerReleases.Unshipped.md": (
        "### New Rules\n\nRule ID | Category | Severity | Notes\n--------|----------|----------|-------\n"
        "MsgPack004 | Usage | Error | Member needs Key\n"
    ),
    "git log": "Fix crash on nested generic types\nUpdate docs",
}


@pytest.fixture
def ctx_dir(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "CONTEXT", tmp_path / "context")
    return config.CONTEXT


def _bootstrap_offline(ctx_dir):
    entries = context.extractive_entries(SOURCES) + context.seeded_entries()
    ctx_dir.mkdir(parents=True, exist_ok=True)
    (ctx_dir / "REVIEW_CONTEXT.md").write_text(context.render(entries, "test"))


def _ledger(area, question="Is X intended?", answer="Yes"):
    return LedgerEntry(question=question, answer=answer, area=area, answered_by="sonion", change_id="c1")


def test_offline_fallback_produces_all_six_sections(ctx_dir):
    _bootstrap_offline(ctx_dir)
    text = (ctx_dir / "REVIEW_CONTEXT.md").read_text()
    for heading in ["Product", "Users", "Public contract", "Deliberate conventions", "Risk areas", "Decisions"]:
        assert f"## {heading}" in text
    assert "(source: doc/analyzers/MsgPack004.md)" in text
    assert "seeded convention for the bait bench case" in text
    assert "_Nothing recorded._" not in text.split("## Product")[1].split("## Users")[0]


def test_every_bullet_has_area_and_source(ctx_dir):
    _bootstrap_offline(ctx_dir)
    bullets = [ln for ln in (ctx_dir / "REVIEW_CONTEXT.md").read_text().splitlines() if ln.startswith("- ")]
    assert bullets
    for b in bullets:
        assert b.startswith("- [area: ") and "(source: " in b


def test_entries_for_areas_filters_and_strips_markers(ctx_dir):
    _bootstrap_offline(ctx_dir)
    got = context.entries_for_areas(["source-generator/analyzer"])
    assert any("MsgPack004" in e for e in got)
    assert any("IsInMetadata" in e for e in got)
    assert not any("bait" in e or "<!--" in e for e in got)
    assert not any("[area: serializer]" in e for e in got)
    assert context.entries_for_areas(["docs"]) == []


def test_ledger_append_is_retrieved_by_area_and_summarised(ctx_dir):
    _bootstrap_offline(ctx_dir)
    context.append_ledger(_ledger("analyzer", "Should metadata types be analysed?", "No, by design"))
    context.append_ledger(_ledger("security", "Is relaxing depth OK?", "No"))

    lines = (ctx_dir / "ledger.jsonl").read_text().splitlines()
    assert len(lines) == 2

    md = (ctx_dir / "REVIEW_CONTEXT.md").read_text()
    decisions = md.split("## Decisions (from answered questions)")[1]
    assert "Should metadata types be analysed? A: No, by design" in decisions
    assert "_No decisions recorded yet._" not in decisions

    analyzer = context.entries_for_areas(["analyzer"])
    assert sum("Should metadata types be analysed?" in e for e in analyzer) == 1
    assert not any("Is relaxing depth OK?" in e for e in analyzer)
    assert any("Is relaxing depth OK?" in e for e in context.entries_for_areas(["security"]))


def test_ledger_works_without_bootstrapped_markdown(ctx_dir):
    context.append_ledger(_ledger("analyzer"))
    assert (ctx_dir / "ledger.jsonl").exists()
    assert not (ctx_dir / "REVIEW_CONTEXT.md").exists()
    assert len(context.entries_for_areas(["analyzer"])) == 1


def test_llm_entries_drop_unknown_sources(monkeypatch):
    class Res:
        data = {"entries": [
            {"section": "Product", "area": "product", "text": "Real.", "source": "README.md"},
            {"section": "Product", "area": "product", "text": "Invented.", "source": "made-up.md"},
        ]}

    monkeypatch.setattr(context.llm, "call_json", lambda *a, **k: Res())
    got = context.llm_entries(SOURCES)
    assert [e.text for e in got] == ["Real."]


def test_ledger_lines_for_prompts_never_carry_change_ids(tmp_path, monkeypatch):
    from receipts import config, context
    from receipts.models import LedgerEntry

    monkeypatch.setattr(config, "CONTEXT", tmp_path)
    (tmp_path / "REVIEW_CONTEXT.md").write_text("# Review context\n\n## Decisions\n")
    context.append_ledger(LedgerEntry(question="Q?", answer="A.", area="analyzer",
                                      answered_by="x", change_id="mock-2283-bait"))
    lines = context.entries_for_areas(["analyzer"])
    assert lines and not any("mock-2283-bait" in line or "bait" in line for line in lines)
