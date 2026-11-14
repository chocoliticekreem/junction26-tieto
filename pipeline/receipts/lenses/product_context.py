"""product-context lens: does the change respect recorded conventions? Cites the ledger instead of
flagging, asks an intent question when the verdict depends on unrecorded intent (SPEC 5.6, 5.7)."""

from .. import config
from .base import AMBIGUITY_PROPERTIES, LensSpec
from .scope import AMBIGUITY, COMMON, product_scope

SYSTEM = COMMON + """
Your lens: PRODUCT CONTEXT. Question: does the change fit what this product deliberately does, as
recorded in the ledger and implied by the requirement?
- When something in the change would look like a problem to a context-free reviewer but a ledger
  entry says it is deliberate, emit it as a finding with receipt_plan.kind "ledger-citation",
  severity "low", and detail citing the entry id (e.g. "[L2] ...") - that records that you checked it
  and it is fine. Never flag a problem that a ledger entry declares deliberate.
- When whether the change is right depends on product intent that is NOT recorded (who the users are,
  what the team wants by default, whether behaviour should be opt-in), do NOT guess and do NOT emit a
  finding: emit a question (verdict_depends_on_intent true) with 2-3 options.
- Plain convention violations not covered by the ledger (naming, docs, analyzer release notes for new
  or changed diagnostics, README updates) are findings with kind "code-citation".
- """ + AMBIGUITY

SPEC = LensSpec(
    name="product-context",
    model=config.MODEL_LENS,
    effort=config.LENS_EFFORT["product-context"],
    system=SYSTEM,
    select=product_scope,
    extra_properties=AMBIGUITY_PROPERTIES,
)
