import os
import subprocess
import sys
from pathlib import Path

PIPELINE = Path(__file__).resolve().parents[1]
PROBE = "from receipts import config; print(config.MOCK_LLM, repr(config.JEV_API_KEY))"


def _probe(env_file: Path, **env: str) -> str:
    base = {k: v for k, v in os.environ.items()
            if k not in ("MOCK_LLM", "JEV_API_KEY", "RECEIPTS_ENV_FILE")}
    out = subprocess.run([sys.executable, "-c", PROBE], cwd=PIPELINE, capture_output=True,
                         text=True, check=True,
                         env={**base, "RECEIPTS_ENV_FILE": str(env_file), **env})
    return out.stdout.strip()


def test_shell_mock_llm_beats_env_file(tmp_path):
    env_file = tmp_path / "receipts-test.envfile"
    env_file.write_text("MOCK_LLM=0\nJEV_API_KEY=from-file\n")
    assert _probe(env_file, MOCK_LLM="1") == "True 'from-file'"


def test_env_file_fills_unset_variables(tmp_path):
    env_file = tmp_path / "receipts-test.envfile"
    env_file.write_text("MOCK_LLM=1\n")
    assert _probe(env_file).startswith("True")
