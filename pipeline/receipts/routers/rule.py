"""RuleRouter: deterministic scoring from file kind and code area. Always runs first (SPEC 5.2)."""

from ..models import Hunk, RouteResult

FLOOR_RISK = 6
# Areas whose hunks can never be scored below FLOOR_RISK by any router.
FLOOR_AREAS = {
    "public-api": "compatibility",
    "serializer-format": "compatibility",
    "serializer-runtime": "compatibility",
    "security": "security",
    # The source generator's emit code and templates define the wire format of generated formatters.
    "codegen-templates": "compatibility",
    "source-generator": "compatibility",
}
# Generated code correctness depends on the generator's analysis, so its hunks reach mutation.
GENERATOR_ANALYSIS_FLOOR = 5
GENERATOR_ANALYSIS_PREFIXES = (
    "src/MessagePack.SourceGenerator/CodeAnalysis/",
    "src/MessagePack.SourceGenerator/Analyzers/",
)
DEFAULT_SOURCE_RISK = 4
TEST_RISK = 3


class RuleRouter:
    name = "rule"
    usd_per_token = 0.0

    def route(self, hunk: Hunk, requirement_text: str, context: dict) -> RouteResult:
        area = context.get("area", "")
        if hunk.kind == "generated":
            return RouteResult(hunk_id=hunk.id, risk=1, category="generated", confidence=1.0,
                               router=self.name, reason="generated file: verified by regeneration")
        if hunk.kind == "docs":
            return RouteResult(hunk_id=hunk.id, risk=1, category="docs", confidence=0.9,
                               router=self.name, reason="documentation only")
        if area in FLOOR_AREAS:
            return RouteResult(hunk_id=hunk.id, risk=FLOOR_RISK, category=FLOOR_AREAS[area],
                               confidence=0.8, router=self.name, floor_applied=True,
                               reason=f"{area} code: risk floor {FLOOR_RISK}")
        if hunk.kind == "source" and hunk.file.startswith(GENERATOR_ANALYSIS_PREFIXES):
            return RouteResult(hunk_id=hunk.id, risk=GENERATOR_ANALYSIS_FLOOR,
                               category="correctness", confidence=0.7, router=self.name,
                               floor_applied=True,
                               reason="code generator analysis: generated code correctness "
                                      "depends on it")
        if hunk.kind == "test":
            return RouteResult(hunk_id=hunk.id, risk=TEST_RISK, category="tests", confidence=0.7,
                               router=self.name, reason="test code")
        return RouteResult(hunk_id=hunk.id, risk=DEFAULT_SOURCE_RISK, category="correctness",
                           confidence=0.5, router=self.name,
                           reason=f"{hunk.kind} code in {area or 'unmapped area'}: default risk")


def floor_for(rule_result: RouteResult) -> int:
    """The minimum risk any other router may assign; 1 when no floor applies."""
    return rule_result.risk if rule_result.floor_applied else 1
