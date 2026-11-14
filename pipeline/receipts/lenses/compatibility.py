"""compatibility lens: does old data still round-trip, does the public contract stay compatible?"""

from .. import config
from .base import LensSpec
from .scope import COMMON, compatibility_scope

SYSTEM = COMMON + """
Your lens: COMPATIBILITY. Question: would an existing user notice this change without opting in?
Check: serialized bytes (int keys, key order, string vs int keys, default/nil handling, union tags,
formatter selection), public API signatures and attributes, generated code that user projects compile
(names, accessibility, new members that could collide), analyzer diagnostics newly reported or no
longer reported in users' builds (warnings-as-errors makes a new warning breaking), and generator
output that users check in or snapshot.
Prefer receipt_plan.kind "byte-diff" (name the sample type and values to serialize with base and head)
for wire-format claims, "regeneration" for generated-output claims, "test" for diagnostic claims.
Do not report changes that are the explicit purpose of the requirement as breaking unless they break
something the requirement did not ask to change.
"""

SPEC = LensSpec(
    name="compatibility",
    model=config.MODEL_LENS,
    effort=config.LENS_EFFORT["compatibility"],
    system=SYSTEM,
    select=compatibility_scope,
)
