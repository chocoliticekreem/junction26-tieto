import json

import pytest

from receipts import models, verdict
from receipts.models import (
    CommandReceipt, Evidence, FileChange, Finding, Hunk, IntakeReport, Interpretation, ReceiptPlan,
    RouteResult, RoutingReport,
)
from receipts.verdict import ALL_LENSES, VerdictInputs, compose

ALL_RAN = [*ALL_LENSES, "fail-before-pass-after", "mutation", "byte-diff", "regeneration"]


def _inputs(area: str, risk: int = 3, path: str = "src/X/Y.cs", **kw) -> VerdictInputs:
    f = FileChange(path=path, kind="source", status="modified", additions=1, deletions=0, area=area)
    h = Hunk(id=f"{path}#1", file=path, kind="source", old_start=1, old_lines=1, new_start=1,
             new_lines=1, text="+x")
    intake = IntakeReport(change_id="c", requirement_id="REQ-1", base="b" * 40, head="c",
                          head_sha="h" * 40, files=[f], hunks=[h],
                          counts={"source": 1, "generated": 0, "total": 1, "not_generated": 1},
                          summary="0 generated files routed to regeneration check; 1 reviewed")
    routing = RoutingReport(router="rule", routes=[RouteResult(
        hunk_id=h.id, risk=risk, category="correctness", confidence=1, router="rule")],
        depth_by_file={path: "light"})
    cmds = [CommandReceipt(id="build-head", cmd=["dotnet", "build"], cwd=".", exit_code=0,
                           duration_s=1.0, log_path="logs/build-head.log")]
    defaults = dict(intake=intake, routing=routing, requirement_title="t", requirement_text="x",
                    run_ts="ts", commands=cmds, ran=list(ALL_RAN))
    defaults.update(kw)
    return VerdictInputs(**defaults)


@pytest.fixture
def high_trust(tmp_path, monkeypatch):
    monkeypatch.setattr(verdict.config, "METRICS", tmp_path)
    (tmp_path / "trust.json").write_text(json.dumps({
        "type-collector": 3, "public-api": 3, "serializer-format": 3, "security": 3,
        "serializer-runtime": 3}))


def _finding(severity: str, verified: bool) -> Finding:
    return Finding(id=f"F-{severity}", lens="logic-edge", claim="breaks", location="src/X/Y.cs:1",
                   severity=severity, receipt_plan=ReceiptPlan(kind="test", detail="t"),
                   verified=verified, evidence_ids=["ev1"] if verified else [])


def test_auto_ready_when_everything_green_and_trusted(high_trust):
    r = compose(_inputs("type-collector"))
    assert r.verdict == "auto-ready", r.verdict_reason


@pytest.mark.parametrize("area", ["public-api", "serializer-format", "security"])
def test_never_auto_ready_for_risky_areas(high_trust, area):
    r = compose(_inputs(area, risk=1))
    assert r.verdict == "needs-sign-off"
    assert "risky area" in r.verdict_reason


def test_default_trust_blocks_auto_ready(tmp_path, monkeypatch):
    monkeypatch.setattr(verdict.config, "METRICS", tmp_path)
    r = compose(_inputs("type-collector"))
    assert r.verdict == "needs-sign-off" and "trust below 3" in r.verdict_reason
    assert r.area_trust == {"type-collector": 1}


def test_high_risk_blocks_auto_ready(high_trust):
    assert compose(_inputs("type-collector", risk=5)).verdict == "needs-sign-off"


def test_high_hunch_blocks_auto_ready(high_trust):
    r = compose(_inputs("type-collector", findings=[_finding("high", verified=False)]))
    assert r.verdict == "needs-sign-off"
    assert len(r.hunches) == 1 and r.findings == []


@pytest.mark.parametrize("sev", ["high", "critical"])
def test_blocked_on_verified_high_finding(high_trust, sev):
    r = compose(_inputs("type-collector", findings=[_finding(sev, verified=True)]))
    assert r.verdict == "blocked"
    assert [f.id for f in r.findings] == [f"F-{sev}"]


def test_verified_flag_without_evidence_stays_a_hunch(high_trust):
    f = _finding("high", verified=True).model_copy(update={"evidence_ids": []})
    r = compose(_inputs("type-collector", findings=[f]))
    assert r.verdict != "blocked" and len(r.hunches) == 1


def test_medium_verified_finding_does_not_block(high_trust):
    r = compose(_inputs("type-collector", findings=[_finding("medium", verified=True)]))
    assert r.verdict == "auto-ready"


def test_decision_ab_when_interpretations(high_trust):
    r = compose(_inputs("type-collector", interpretations=[
        Interpretation(label="A", summary="unconditional"), Interpretation(label="B", summary="opt-out")]))
    assert r.verdict == "decision-A-B"


def test_red_evidence_blocks_auto_ready(high_trust):
    ev = Evidence(id="mut", kind="mutation", title="mutants", result="partial")
    assert compose(_inputs("type-collector", evidence=[ev])).verdict == "needs-sign-off"


def test_head_build_failure_blocks(high_trust):
    cmds = [CommandReceipt(id="build-head", cmd=["dotnet", "build"], cwd=".", exit_code=1,
                           duration_s=1.0, log_path="logs/build-head.log")]
    assert compose(_inputs("type-collector", commands=cmds)).verdict == "blocked"


def test_existing_test_regression_blocks(high_trust):
    base = models.TestRun(receipt_id="test-base-existing", side="base", compiled=True, total=1, passed=1,
                   cases=[models.TestCase(name="T.A", outcome="passed")])
    head = models.TestRun(receipt_id="test-head-existing", side="head", compiled=True, total=1, failed=1,
                   cases=[models.TestCase(name="T.A", outcome="failed")])
    r = compose(_inputs("type-collector", tests=[base, head]))
    assert r.verdict == "blocked" and "T.A" in r.verdict_reason


def test_new_test_failing_on_head_blocks(high_trust):
    base = models.TestRun(receipt_id="test-base-existing", side="base", compiled=True, total=1, passed=1,
                   cases=[models.TestCase(name="T.A", outcome="passed")])
    head = models.TestRun(receipt_id="test-head-existing", side="head", compiled=True, total=2, passed=1,
                   failed=1, cases=[models.TestCase(name="T.A", outcome="passed"),
                                    models.TestCase(name="T.New", outcome="failed")])
    r = compose(_inputs("type-collector", tests=[base, head]))
    assert r.verdict == "blocked" and "T.New" in r.verdict_reason


def test_failure_already_red_on_base_does_not_block(high_trust):
    base = models.TestRun(receipt_id="test-base-existing", side="base", compiled=True, total=1, failed=1,
                   cases=[models.TestCase(name="T.Flaky", outcome="failed")])
    head = models.TestRun(receipt_id="test-head-existing", side="head", compiled=True, total=1, failed=1,
                   cases=[models.TestCase(name="T.Flaky", outcome="failed")])
    r = compose(_inputs("type-collector", tests=[base, head]))
    assert r.verdict != "blocked"


def test_not_checked_never_empty_even_when_everything_ran(high_trust):
    r = compose(_inputs("type-collector"))
    assert r.not_checked
    assert any("NativeAOT" in n.what for n in r.not_checked)


def test_not_checked_lists_lenses_that_did_not_run(high_trust):
    r = compose(_inputs("type-collector", ran=[], lens_unavailable_reason="LLM unavailable: no key"))
    whats = {n.what: n.why for n in r.not_checked}
    for lens in ALL_LENSES:
        assert whats[f"{lens} lens"] == "LLM unavailable: no key"
    assert "mutation testing" in whats
    assert r.verdict == "needs-sign-off" and "no AI lens ran" in r.verdict_reason


def test_skip_mutation_is_declared(high_trust):
    r = compose(_inputs("type-collector", ran=list(ALL_LENSES), skip_mutation=True))
    assert any(n.what == "mutation testing" and "--skip-mutation" in n.why for n in r.not_checked)


def test_residual_risk_and_checked_are_populated(high_trust):
    r = compose(_inputs("type-collector"))
    assert "highest routed risk is 3/10" in r.residual_risk
    assert any(c.evidence_id == "build-head" for c in r.checked)
