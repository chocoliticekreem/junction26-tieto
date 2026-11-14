"""Run folder layout. The dashboard depends on these exact names.

runs/<change-id>/<run-ts>/
    intake.json        IntakeReport
    routing.json       RoutingReport
    commands.json      list[CommandReceipt]
    tests.json         list[TestRun]
    evidence.json      list[Evidence]
    findings.json      list[Finding] (all lens output, verified or not)
    mutants.json       list[Mutant]
    questions.json     list[Question]
    receipt.json       TrustReceipt  (written last; its presence marks the run complete)
    logs/<id>.log      raw command logs, cited by CommandReceipt.log_path
    generated/         files the lenses wrote (e.g. requirement tests), cited by evidence
runs/<change-id>/latest   one line: the run-ts of the newest complete run

Pending questions = questions.json entries whose id has no context/ledger.jsonl entry.
"""

import json
from datetime import datetime, timezone
from pathlib import Path

from pydantic import BaseModel

from . import config


def portable(text: str) -> str:
    """Machine paths -> placeholders, so run artifacts never carry a home directory."""
    prefixes = {str(config.RUNS): "<RUNS>", str(config.WORK): "<WORK>",
                str(config.MOCK_REPO): "<MOCK_REPO>", str(config.ROOT): "<ROOT>",
                str(Path.home()): "~"}
    for prefix in sorted(prefixes, key=len, reverse=True):
        text = text.replace(prefix, prefixes[prefix])
    return text


def portable_file(path: Path) -> None:
    if path.exists():
        path.write_text(portable(path.read_text(errors="replace")))


def new_run_dir(change_id: str) -> Path:
    ts = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    d = config.RUNS / change_id / ts
    (d / "logs").mkdir(parents=True, exist_ok=True)
    return d


def write(run_dir: Path, name: str, obj) -> Path:
    if isinstance(obj, BaseModel):
        payload = obj.model_dump(mode="json")
    elif isinstance(obj, list):
        payload = [o.model_dump(mode="json") if isinstance(o, BaseModel) else o for o in obj]
    else:
        payload = obj
    p = run_dir / name
    p.write_text(portable(json.dumps(payload, indent=2, ensure_ascii=False)))
    return p


def mark_latest(run_dir: Path) -> None:
    (run_dir.parent / "latest").write_text(run_dir.name + "\n")


def latest_run(change_id: str) -> Path | None:
    f = config.RUNS / change_id / "latest"
    if not f.exists():
        return None
    return config.RUNS / change_id / f.read_text().strip()
