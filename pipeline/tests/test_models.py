import pytest
from pydantic import ValidationError

from receipts.models import RouteResult


def test_route_result_rejects_out_of_range_risk():
    with pytest.raises(ValidationError):
        RouteResult(hunk_id="a#1", risk=11, category="correctness", confidence=0.5, router="rule")


def test_route_result_accepts_valid():
    r = RouteResult(hunk_id="a#1", risk=6, category="compatibility", confidence=0.9, router="rule")
    assert r.floor_applied is False
