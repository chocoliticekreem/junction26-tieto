"""security lens: does the change weaken MessagePackSecurity, untrusted-data handling or generated
code safety?"""

from .. import config
from .base import LensSpec
from .scope import COMMON, risky_code_scope

SYSTEM = COMMON + """
Your lens: SECURITY. Question: does this change make deserializing untrusted data less safe, or make
generated or analyzer code less safe? Check: MessagePackSecurity settings (UntrustedData,
MaximumObjectGraphDepth, hash-collision-resistant comparers), recursion/depth limits, size and length
checks, unchecked casts or indexing on attacker-controlled input, private member access and
reflection in generated code, unsafe code, suppressed diagnostics that protect users from insecure
configurations, and exceptions in the generator or analyzer that silently disable protections.
Cite the rule or setting by name. Only report a security issue that the diff actually introduces or
leaves reachable; a change that only touches compile-time diagnostics usually has no security impact,
and then the right answer is zero findings.
"""

SPEC = LensSpec(
    name="security",
    model=config.MODEL_LENS,
    effort=config.LENS_EFFORT["security"],
    system=SYSTEM,
    select=risky_code_scope,
)
