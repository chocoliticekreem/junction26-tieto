"""The contract every sandbox adapter implements: one input, one result, receipts in run_dir."""

from dataclasses import dataclass, field
from pathlib import Path

from ..models import CommandReceipt, Evidence, Finding, NotChecked


@dataclass
class AdapterInput:
    repo: Path  # bare git repo holding base and head
    base_sha: str
    head_sha: str
    run_dir: Path  # logs/<id>.log and any inputs the adapter feeds to commands land here
    requirement_text: str


@dataclass
class AdapterResult:
    commands: list[CommandReceipt] = field(default_factory=list)
    evidence: list[Evidence] = field(default_factory=list)
    findings: list[Finding] = field(default_factory=list)  # verified only: each cites evidence
    not_checked: list[NotChecked] = field(default_factory=list)
    ran: list[str] = field(default_factory=list)  # check names that executed
    extra: dict = field(default_factory=dict)  # adapter-specific audit data (snapshots, timings)
