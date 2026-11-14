"""Shared data contract. Every pipeline stage writes these as JSON under runs/<change>/<ts>/.

The dashboard reads the same shapes (mirrored in dashboard/lib/types.ts). Change both together.
"""

from datetime import datetime, timezone
from typing import Literal

from pydantic import BaseModel, Field

FileKind = Literal["source", "test", "docs", "generated", "template"]
Category = Literal["correctness", "security", "compatibility", "tests", "docs", "generated"]
LensName = Literal[
    "requirement-tests", "logic-edge", "compatibility", "security", "product-context", "test-quality"
]
Severity = Literal["low", "medium", "high", "critical"]
VerdictKind = Literal["auto-ready", "needs-sign-off", "decision-A-B", "blocked"]
Depth = Literal["regeneration", "light", "deep"]  # light = build + existing tests + cheap lenses


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


# [1] Intake ---------------------------------------------------------------


class Hunk(BaseModel):
    id: str  # "<file>#<n>"
    file: str
    kind: FileKind
    old_start: int
    old_lines: int
    new_start: int
    new_lines: int
    header: str = ""  # the @@ line's trailing context (often the enclosing member)
    text: str  # unified diff body of this hunk, without the file header


class FileChange(BaseModel):
    path: str
    kind: FileKind
    status: Literal["added", "modified", "deleted", "renamed"]
    additions: int
    deletions: int
    area: str  # e.g. "source-generator/analyzer", "annotations/public-api"; see intake.area_for


class IntakeReport(BaseModel):
    change_id: str
    requirement_id: str
    base: str
    head: str
    head_sha: str
    files: list[FileChange]
    hunks: list[Hunk]
    counts: dict[str, int]  # files per kind + "total"
    summary: str  # e.g. "222 generated files routed to regeneration check; 15 reviewed"


# [2] Router ---------------------------------------------------------------


class RouteResult(BaseModel):
    hunk_id: str
    risk: int = Field(ge=1, le=10)
    category: Category
    confidence: float = Field(ge=0, le=1)
    router: str  # "rule" | "jev" | "llm" | "rule+jev" ...
    cost_tokens: int = 0
    ms: int = 0
    floor_applied: bool = False  # RuleRouter floor overrode a lower score
    reason: str = ""


class RoutingReport(BaseModel):
    router: str
    routes: list[RouteResult]
    depth_by_file: dict[str, Depth]
    total_tokens: int = 0
    total_ms: int = 0
    cost_usd: float = 0.0


# [3] Sandbox --------------------------------------------------------------


class CommandReceipt(BaseModel):
    """One executed command. Every executed artifact the review cites is one of these."""

    id: str  # stable within a run, e.g. "build-head", "test-base-req"
    cmd: list[str]
    cwd: str
    exit_code: int
    duration_s: float
    log_path: str  # relative to the run dir
    started_at: str = Field(default_factory=now_iso)


class TestCase(BaseModel):
    name: str
    outcome: Literal["passed", "failed", "skipped", "notexecuted"]
    duration_s: float = 0.0
    message: str = ""


class TestRun(BaseModel):
    receipt_id: str  # the CommandReceipt that ran it
    side: Literal["base", "head"]
    filter: str = ""
    compiled: bool
    total: int = 0
    passed: int = 0
    failed: int = 0
    skipped: int = 0
    cases: list[TestCase] = []


# [4] Lenses + [5] receipt checks ------------------------------------------


class ReceiptPlan(BaseModel):
    kind: Literal["test", "mutation", "byte-diff", "regeneration", "ledger-citation", "code-citation"]
    detail: str


class Finding(BaseModel):
    id: str
    lens: LensName
    claim: str
    location: str  # "path:line" or "path"
    severity: Severity
    receipt_plan: ReceiptPlan
    verified: bool = False  # True only once a receipt in evidence_ids backs it
    evidence_ids: list[str] = []  # CommandReceipt / Evidence ids
    verdict_depends_on_intent: bool = False


class Evidence(BaseModel):
    """A receipt check result: what was executed, what it showed."""

    id: str
    kind: Literal[
        "fail-before-pass-after", "new-api", "mutation", "regeneration", "byte-diff",
        "existing-tests", "build", "ledger-citation", "code-citation",
    ]
    title: str
    result: Literal["pass", "fail", "partial", "error", "skipped"]
    command_ids: list[str] = []
    details: dict = {}  # kind-specific payload, e.g. mutants list, byte diff hex, test names
    excerpt: str = ""  # short log excerpt shown in the dashboard


class Mutant(BaseModel):
    id: str
    file: str
    line: int
    operator: Literal["negate-condition", "remove-guard", "off-by-one", "swap-boolean", "delete-statement"]
    original: str
    mutated: str
    status: Literal["killed", "survived", "compile-error", "timeout"]
    killing_tests: list[str] = []


# [6] Questions + ledger ---------------------------------------------------


class Question(BaseModel):
    id: str
    change_id: str
    area: str
    lens: LensName
    text: str
    options: list[str]  # 2-3 answers; free text is always allowed
    why_it_matters: str  # how the answer flips the verdict
    status: Literal["pending", "answered"] = "pending"
    asked_at: str = Field(default_factory=now_iso)


class LedgerEntry(BaseModel):
    question: str
    answer: str
    area: str
    answered_by: str
    ts: str = Field(default_factory=now_iso)
    change_id: str
    question_id: str = ""


# [7] Verdict + trust receipt ----------------------------------------------


class CheckedItem(BaseModel):
    what: str
    by: str  # tool / lens / router
    evidence_id: str = ""
    result: Literal["pass", "fail", "partial", "error", "skipped"]


class NotChecked(BaseModel):
    what: str
    why: str


class Refusal(BaseModel):
    what: str
    why: str


class Interpretation(BaseModel):
    label: Literal["A", "B"]
    summary: str
    branch: str = ""


class TrustReceipt(BaseModel):
    change_id: str
    requirement_id: str
    requirement_title: str
    requirement_text: str
    run_ts: str
    head_sha: str
    verdict: VerdictKind
    verdict_reason: str
    risk: int = Field(ge=1, le=10)  # max routed risk over hunks
    areas: list[str]
    area_trust: dict[str, int] = {}  # area -> level 0..3 at review time
    checked: list[CheckedItem]
    findings: list[Finding]  # verified only
    hunches: list[Finding]  # unverified, shown separately
    refused: list[Refusal]
    not_checked: list[NotChecked]
    residual_risk: str
    routing: dict[str, Depth]  # file -> depth
    counts: dict[str, int]
    questions: list[str] = []  # Question ids raised by this run
    interpretations: list[Interpretation] = []  # present when verdict == decision-A-B
    narrative: str = ""  # optional "why this verdict" paragraph citing evidence ids
    mock_llm: bool = False  # True when lens output came from recorded fixtures


class Decision(BaseModel):
    change_id: str
    requirement_id: str
    who: str
    choice: Literal["A", "B", "sign-off"]
    evidence_ids: list[str]
    merge_sha: str = ""
    ts: str = Field(default_factory=now_iso)
