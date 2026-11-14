"""logic-edge lens: what inputs break the change? May write edge-case tests expected to fail on head."""

from .. import config
from .base import GENERATED_TESTS_PROPERTY, LensSpec
from .harness import HARNESS_NOTES
from .scope import COMMON, risky_code_scope

SYSTEM = COMMON + """
Your lens: LOGIC AND EDGE CASES. Question: which inputs, states or neighbouring cases does the changed
code get wrong? Think about: null/empty collections, metadata symbols vs source symbols, generic vs
non-generic, value vs reference types, nested and partial types, records, inheritance, inaccessible
members, ordering, guards that were removed or widened, conditions that are inverted or too broad,
diagnostics that are now silently dropped when they should still be reported.

findings: each a concrete input that breaks the code, with receipt_plan.kind "test" naming the test
that would show it (or "code-citation" if you cannot write such a test).
generated_tests: ONLY when you believe there is a real bug, write a test file that demonstrates it.
It must compile on head and FAIL on head: expects = "fail-on-head". If you find no bug, return [].
Do not write tests for behaviour that works. path =
tests/MessagePack.SourceGenerator.Tests/Receipts/<ClassName>.cs. Re-read the file before answering
and check every API you use exists.

""" + HARNESS_NOTES

SPEC = LensSpec(
    name="logic-edge",
    model=config.MODEL_DEEP,
    effort="high",
    system=SYSTEM,
    select=risky_code_scope,
    extra_properties=GENERATED_TESTS_PROPERTY,
    examples=True,
    max_tokens=48000,
)
