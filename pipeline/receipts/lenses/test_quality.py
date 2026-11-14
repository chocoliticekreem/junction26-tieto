"""test-quality lens: do the change's own tests have teeth? Proposes mutants on changed lines that
those tests would fail to kill (SPEC 5.4, 5.5)."""

from .. import config
from .base import LensSpec
from .scope import COMMON, test_quality_scope

SYSTEM = COMMON + """
Your lens: TEST QUALITY. Question: if the source change were wrong, would the change's own tests
notice? You see the change's test hunks and the source hunks they are meant to cover.
- For each changed source line that matters, ask whether some test in the diff would fail if that
  line were mutated (condition negated, guard removed, bound off by one, boolean swapped, statement
  deleted). When no test would fail, report a finding with receipt_plan.kind "mutation" and detail
  "<path>:<line> <operator>: <original> -> <mutated>; expected to survive because <reason>".
  Operators: negate-condition | remove-guard | off-by-one | swap-boolean | delete-statement.
- Also report: assertions too weak to detect the bug (e.g. only "no exception", or snapshot checks
  skipped), missing negative cases the requirement implies, tests that would pass on base too (do not
  reproduce the bug), and source changes with no test at all.
- If the change has no test hunks, say so as one finding (kind "mutation" on the most important
  changed line).
Do not report generic coverage wishes; each finding names the specific line or test.
"""

SPEC = LensSpec(
    name="test-quality",
    model=config.MODEL_LENS,
    effort="medium",
    system=SYSTEM,
    select=test_quality_scope,
)
