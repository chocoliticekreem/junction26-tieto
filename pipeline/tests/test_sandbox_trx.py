from pathlib import Path

from receipts.sandbox import parse_trx

FIXTURE = Path(__file__).parent / "fixtures" / "sample.trx"


def test_parse_trx_counts_and_messages():
    run = parse_trx(FIXTURE, "test-head-existing", "head")
    assert run.compiled is True
    assert (run.total, run.passed, run.failed, run.skipped) == (3, 1, 1, 1)
    by_name = {c.name: c for c in run.cases}
    assert by_name["GenericsFormatterTests.Passes"].duration_s == 0.1235
    assert by_name["GenericsFormatterTests.Fails"].outcome == "failed"
    assert "expected 1, actual 2" in by_name["GenericsFormatterTests.Fails"].message
    assert by_name["GenericsFormatterTests.Skipped"].outcome == "notexecuted"
