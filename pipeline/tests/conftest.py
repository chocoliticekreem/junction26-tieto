import pytest

from receipts import config


@pytest.fixture(autouse=True)
def _isolated_spend_ledgers(tmp_path, monkeypatch):
    """No test may read or write the real spend ledgers in work/."""
    monkeypatch.setattr(config, "ANTHROPIC_SPEND_FILE", tmp_path / "anthropic-spend.json")
    monkeypatch.setattr(config, "ANTHROPIC_MAX_USD", 20.0)
    monkeypatch.setattr(config, "VERDA_SPEND_FILE", tmp_path / "verda-spend.json")
