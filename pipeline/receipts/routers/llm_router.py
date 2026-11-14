"""LLMRouter: Claude Haiku 4.5 scores one hunk (risk 1..10 + category) via llm.call_json.

The prompt holds the requirement and the hunk only (SPEC 5.2). Output is validated against
models.RouteResult; anything outside the schema raises RouterOutputError instead of being coerced.
"""

from pydantic import ValidationError

from .. import config, llm
from ..models import Hunk, RouteResult

PURPOSE = "router-llm"
MAX_HUNK_CHARS = 60_000  # Haiku's window is far larger; this keeps per-hunk cost bounded
CATEGORIES = ["correctness", "security", "compatibility", "tests", "docs", "generated"]

SCHEMA = {
    "type": "object",
    "properties": {
        "risk": {"type": "integer", "description": "1 (trivial) .. 10 (very likely to hide a defect)"},
        "category": {"type": "string", "enum": CATEGORIES},
        "confidence": {"type": "number", "description": "0..1, how sure you are of the risk score"},
        "reason": {"type": "string", "description": "one sentence"},
    },
    "required": ["risk", "category", "confidence", "reason"],
    "additionalProperties": False,
}

SYSTEM = """You are a code-review router. You score ONE diff hunk for how much review depth it needs.
You see only the requirement the change claims to implement and the hunk itself.

risk (integer 1-10):
  1-2  formatting, comments, docs, renames with no behaviour change
  3-4  small, local behaviour change that is easy to verify
  5-6  behaviour change in logic that other code depends on, or a change to tests that guard behaviour
  7-8  change to public API, serialization format, security-relevant handling, or error handling paths
  9-10 change that can silently corrupt data, break wire compatibility, or weaken security
category: the single main risk dimension of the hunk:
  correctness | security | compatibility | tests | docs | generated
confidence: 0..1, calibrated (0.5 = coin flip).
Judge only what the hunk shows. Do not invent context."""


class RouterOutputError(ValueError):
    """The router model returned output that does not match the RouteResult schema."""


def truncate(text: str, limit: int) -> str:
    if len(text) <= limit:
        return text
    omitted = len(text) - limit
    return text[:limit] + f"\n[... truncated by receipts: {omitted} chars omitted to fit the router input limit ...]\n"


def render_hunk(hunk: Hunk, limit: int) -> str:
    head = f"File: {hunk.file} (kind: {hunk.kind})\n@@ -{hunk.old_start},{hunk.old_lines} +{hunk.new_start},{hunk.new_lines} @@ {hunk.header}".rstrip()
    return head + "\n" + truncate(hunk.text, limit)


def parse_route(data: dict, hunk_id: str, router: str, tokens: int, ms: int) -> RouteResult:
    """Validate raw router output. Rejects missing fields, out-of-range values and unknown categories."""
    if not isinstance(data, dict):
        raise RouterOutputError(f"router output is not an object: {type(data).__name__}")
    missing = [k for k in ("risk", "category", "confidence") if k not in data]
    if missing:
        raise RouterOutputError(f"router output missing {missing}")
    if isinstance(data["risk"], bool) or not isinstance(data["risk"], int):
        raise RouterOutputError(f"risk must be an integer, got {data['risk']!r}")
    try:
        return RouteResult(
            hunk_id=hunk_id,
            risk=data["risk"],
            category=data["category"],
            confidence=data["confidence"],
            router=router,
            cost_tokens=tokens,
            ms=ms,
            reason=str(data.get("reason", ""))[:500],
        )
    except ValidationError as e:
        raise RouterOutputError(f"router output rejected: {e.errors()[0]['msg']} ({data!r})") from e


class LLMRouter:
    name = "llm"
    model = config.MODEL_ROUTER
    usd_per_token = llm.PRICES_PER_MTOK[config.MODEL_ROUTER][0] / 1_000_000  # input-dominated

    def route(self, hunk: Hunk, requirement_text: str, context: dict | None = None) -> RouteResult:
        user = (
            f"<requirement>\n{requirement_text.strip()}\n</requirement>\n\n"
            f"<hunk>\n{render_hunk(hunk, MAX_HUNK_CHARS)}\n</hunk>"
        )
        res = llm.call_json(PURPOSE, self.model, SYSTEM, user, SCHEMA)
        return parse_route(res.data, hunk.id, self.name, res.input_tokens + res.output_tokens, res.ms)
