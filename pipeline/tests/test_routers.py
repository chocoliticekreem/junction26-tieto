import pytest

from receipts.models import FileChange, Hunk, RouteResult
from receipts.routers import combine
from receipts.routers.rule import RuleRouter


def _hunk(path: str, kind: str, n: int = 1) -> Hunk:
    return Hunk(id=f"{path}#{n}", file=path, kind=kind, old_start=1, old_lines=1, new_start=1,
                new_lines=1, text="+x")


def _file(path: str, kind: str, area: str) -> FileChange:
    return FileChange(path=path, kind=kind, status="modified", additions=1, deletions=0, area=area)


@pytest.mark.parametrize("kind,area,risk,category,floor", [
    ("generated", "tests", 1, "generated", False),
    ("docs", "docs", 1, "docs", False),
    ("test", "tests", 3, "tests", False),
    ("source", "public-api", 6, "compatibility", True),
    ("source", "serializer-format", 6, "compatibility", True),
    ("source", "security", 6, "security", True),
    ("source", "type-collector", 4, "correctness", False),
    ("template", "codegen-templates", 4, "correctness", False),
])
def test_rule_router(kind, area, risk, category, floor):
    r = RuleRouter().route(_hunk("f", kind), "req", {"area": area})
    assert (r.risk, r.category, r.floor_applied) == (risk, category, floor)


@pytest.mark.parametrize("path,kind,risk,floor", [
    ("src/MessagePack.SourceGenerator/CodeAnalysis/TypeCollector.cs", "source", 5, True),
    ("src/MessagePack.SourceGenerator/Analyzers/MsgPack00xMessagePackAnalyzer.cs", "source", 5, True),
    ("src/MessagePack.SourceGenerator/Transforms/FormatterTemplate.cs", "source", 4, False),
    ("src/MessagePack.SourceGenerator/MessagePackGenerator.cs", "source", 4, False),
])
def test_generator_analysis_floor(path, kind, risk, floor):
    area = "type-collector" if "CodeAnalysis" in path else "analyzer"
    r = RuleRouter().route(_hunk(path, kind), "req", {"area": area})
    assert (r.risk, r.category, r.floor_applied) == (risk, "correctness", floor)
    if floor:
        assert r.reason.startswith("code generator analysis")


class LowRouter:
    name = "low"
    usd_per_token = 1e-6

    def route(self, hunk, requirement_text, context):
        return RouteResult(hunk_id=hunk.id, risk=2, category="correctness", confidence=0.9,
                           router=self.name, cost_tokens=100)


class BoomRouter:
    name = "boom"

    def route(self, hunk, requirement_text, context):
        raise RuntimeError("network down")


FILES = [
    _file("src/MessagePack.Annotations/Attributes.cs", "source", "public-api"),
    _file("src/MessagePack.SourceGenerator/CodeAnalysis/TypeCollector.cs", "source", "type-collector"),
    _file("tests/X/Resources/a.g.cs", "generated", "tests"),
]
HUNKS = [_hunk(FILES[0].path, "source"), _hunk(FILES[1].path, "source"),
         _hunk(FILES[2].path, "generated")]


def test_floor_beats_lower_router_score(monkeypatch):
    monkeypatch.setitem(combine._extra, "low", LowRouter)
    rep = combine.route_all(HUNKS, "req", {}, "low", files=FILES)
    by_id = {r.hunk_id: r for r in rep.routes}
    api = by_id[HUNKS[0].id]
    assert api.risk == 6 and api.floor_applied and api.router == "rule+low"
    tc = by_id[HUNKS[1].id]
    assert tc.risk == 5 and tc.floor_applied and tc.category == "correctness"
    gen = by_id[HUNKS[2].id]
    assert gen.router == "rule" and gen.risk == 1  # generated never goes to the other router
    assert rep.router == "rule+low"
    assert rep.total_tokens == 200 and rep.cost_usd == pytest.approx(0.0002)
    assert rep.depth_by_file == {FILES[0].path: "deep", FILES[1].path: "deep",
                                 FILES[2].path: "regeneration"}


def test_missing_router_falls_back_to_rule():
    rep = combine.route_all(HUNKS, "req", {}, "does-not-exist", files=FILES)
    assert rep.router == "rule"
    src = [r for r in rep.routes if r.hunk_id != HUNKS[2].id]
    assert all(r.router == "rule" and "not available" in r.reason for r in src)


def test_raising_router_falls_back_to_rule(monkeypatch):
    monkeypatch.setitem(combine._extra, "boom", BoomRouter)
    rep = combine.route_all(HUNKS, "req", {}, "boom", files=FILES)
    assert all(r.router == "rule" for r in rep.routes)
    assert "network down" in rep.routes[0].reason
    assert rep.routes[0].risk == 6


def test_auto_without_keys_or_routers_is_rule(monkeypatch):
    monkeypatch.setattr(combine.config, "JEV_API_KEY", "")
    monkeypatch.setattr(combine.config, "ANTHROPIC_API_KEY", "")
    monkeypatch.setattr(combine.config, "MOCK_LLM", False)
    rep = combine.route_all(HUNKS, "req", {}, "auto", files=FILES)
    assert rep.router == "rule (auto)"


def test_none_router_sends_every_reviewed_hunk_deep():
    rep = combine.route_all(HUNKS, "req", {}, "none", files=FILES)
    assert rep.depth_by_file[FILES[1].path] == "deep"
    assert rep.depth_by_file[FILES[2].path] == "regeneration"
