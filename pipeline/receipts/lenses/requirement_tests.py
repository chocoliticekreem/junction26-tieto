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
   present or containing something, no exception), never implementation details of this diff.
   Work from the acceptance criteria (the requirement's "Acceptance criteria" list, else the
   behaviours it asks for): for each criterion that states observable behaviour of the code, write
   one test method that checks the criterion AS WRITTEN, at its full scope, whatever the diff
   covers. A scope word sets the assertion: "all X" or "every X" asserts over every X the code
   produces, not only the X this change touches. Skip criteria about process or optional choices.
   For a bug fix the tests must compile on base, FAIL on base (reproducing the reported bug) and PASS
   on head if the fix is correct: expects = "fail-on-base-pass-on-head". For a new feature whose API
   does not exist on base use expects = "new-api". Keep each file focused: 1-5 test methods. test_names
   lists the method names exactly. path = tests/MessagePack.SourceGenerator.Tests/Receipts/<ClassName>.cs.
   Before answering, re-read each file and check every type, method and namespace you use exists in
   the facts below or in the BCL/Roslyn/xUnit; a test that does not compile is worthless.
2. findings: requirement gaps visible in the diff (e.g. a criterion or case the requirement names
   that the change does not handle, or behaviour the requirement forbids). Use receipt_plan.kind
   "test" naming the generated test that would prove it when one covers it. A claim about what the
   code does or fails to do is located at the source or template file that produces that
   behaviour, even when the diff does not touch that file (a criterion the change leaves out has
   no changed line). Never put such a claim on a docs or README line: a test proves code
   behaviour, not documentation, so a docs-located claim stays an unverified hunch.
3. """ + AMBIGUITY + "\n" + HARNESS_NOTES

SPEC = LensSpec(
    name="requirement-tests",
    model=config.MODEL_DEEP,
    effort=config.LENS_EFFORT["requirement-tests"],
    system=SYSTEM,
    select=requirement_scope,
    extra_properties={**GENERATED_TESTS_PROPERTY, **AMBIGUITY_PROPERTIES},
    examples=True,
    max_tokens=48000,
)
