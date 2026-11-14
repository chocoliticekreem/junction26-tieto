"""requirement-tests lens: does the change do what the requirement asks? Writes xUnit tests from the
requirement text only, expected to fail on base and pass on head (SPEC 5.4, 5.5)."""

from .. import config
from .base import AMBIGUITY_PROPERTIES, GENERATED_TESTS_PROPERTY, LensSpec
from .harness import HARNESS_NOTES
from .scope import AMBIGUITY, COMMON, requirement_scope

SYSTEM = COMMON + """
Your lens: REQUIREMENT FIT. Question: does the change do what the requirement asks - all of it, and
nothing that contradicts it?

1. generated_tests: write 1 or 2 xUnit test files derived from the REQUIREMENT TEXT ONLY. They test the
   observable behaviour the requirement describes (diagnostics reported or not, generated source
   present or containing something, no exception), never implementation details of this diff. For a
   bug fix the tests must compile on base, FAIL on base (reproducing the reported bug) and PASS on
   head if the fix is correct: expects = "fail-on-base-pass-on-head". For a new feature whose API does
   not exist on base use expects = "new-api". Keep each file focused: 1-4 test methods. test_names
   lists the method names exactly. path = tests/MessagePack.SourceGenerator.Tests/Receipts/<ClassName>.cs.
   Before answering, re-read each file and check every type, method and namespace you use exists in
   the facts below or in the BCL/Roslyn/xUnit; a test that does not compile is worthless.
2. findings: requirement gaps visible in the diff (e.g. a case the requirement names that the change
   does not handle, or behaviour the requirement forbids). Use receipt_plan.kind "test" naming the
   generated test that would prove it when one covers it.
3. """ + AMBIGUITY + "\n" + HARNESS_NOTES

SPEC = LensSpec(
    name="requirement-tests",
    model=config.MODEL_DEEP,
    effort="high",
    system=SYSTEM,
    select=requirement_scope,
    extra_properties={**GENERATED_TESTS_PROPERTY, **AMBIGUITY_PROPERTIES},
    examples=True,
    max_tokens=48000,
)
