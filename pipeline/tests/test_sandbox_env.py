import subprocess
from pathlib import Path

import pytest

from receipts import compat, config, runstore, sandbox

SECRETS = {"ANTHROPIC_API_KEY": "sk-ant-fake", "JEV_API_KEY": "jev-fake", "GITHUB_TOKEN": "ghp",
           "AWS_SECRET_ACCESS_KEY": "aws", "MY_PASSWORD": "pw", "JEVURL": "x"}


@pytest.fixture
def secret_env(monkeypatch, tmp_path):
    for k, v in SECRETS.items():
        monkeypatch.setenv(k, v)
    monkeypatch.setenv("DOTNET_ROOT", "/opt/dotnet")
    monkeypatch.setenv("NUGET_PACKAGES", "/opt/nuget")
    monkeypatch.setenv("UNRELATED_SETTING", "1")
    calls: list[dict] = []

    def fake_run(cmd, **kw):
        calls.append(kw["env"])
        return subprocess.CompletedProcess(cmd, 0, stdout="{}", stderr="")

    monkeypatch.setattr(subprocess, "run", fake_run)
    return calls


def _assert_clean(env: dict):
    assert not set(SECRETS) & set(env)
    assert "UNRELATED_SETTING" not in env
    assert env["DOTNET_ROOT"] == "/opt/dotnet" and env["NUGET_PACKAGES"] == "/opt/nuget"
    assert env["DOTNET_CLI_TELEMETRY_OPTOUT"] == "1" and "PATH" in env


def test_dotnet_runner_gets_no_api_keys(secret_env, tmp_path):
    sandbox.run_cmd("build-head", [config.DOTNET, "build"], tmp_path, tmp_path)
    assert len(secret_env) == 1
    _assert_clean(secret_env[0])


def test_compat_harness_gets_no_api_keys(secret_env, tmp_path):
    compat._run([config.DOTNET, "run"], tmp_path, tmp_path, "compat-bytes-head")
    assert len(secret_env) == 1
    _assert_clean(secret_env[0])


def test_run_logs_and_receipts_carry_no_home_path(secret_env, tmp_path, monkeypatch):
    monkeypatch.setattr(config, "WORK", tmp_path / "work")
    wt = config.WORK / "head-abc"
    home_dotnet = str(Path.home() / ".dotnet" / "dotnet")
    r = sandbox.run_cmd("build-head", [home_dotnet, "build"], wt.parent, tmp_path)
    log = (tmp_path / r.log_path).read_text()
    assert str(Path.home()) not in log and str(Path.home()) not in r.model_dump_json()
    assert r.cmd[0] == "~/.dotnet/dotnet" and r.cwd == "<WORK>"


def test_runstore_write_replaces_home_and_work(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "WORK", tmp_path / "w")
    runstore.write(tmp_path, "x.json", {"a": f"{tmp_path / 'w'}/head-1/src", "b": str(Path.home())})
    text = (tmp_path / "x.json").read_text()
    assert "<WORK>/head-1/src" in text and str(Path.home()) not in text
