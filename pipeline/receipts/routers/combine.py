"""route_all: RuleRouter first, then an optional second router, rule floors enforced.

Final risk per hunk:
  generated hunks        rule result only (verified by regeneration; never sent to another router)
  floor areas            max(rule floor, other risk), floor_applied=True when the floor won
  everything else        the other router's risk (the rule default is only a placeholder)
If the second router is not registered, fails to construct, or raises, the rule result is kept and
RouteResult.reason says why. After the first per-hunk failure the router is not called again in
this run (failures are systematic: missing key, network down).
"""

import importlib
import time

from .. import config
from ..models import Depth, FileChange, Hunk, RouteResult, RoutingReport
from .rule import RuleRouter, floor_for

# name -> (module relative to this package, class name). Missing modules are skipped silently.
OPTIONAL_ROUTERS: dict[str, tuple[str, str]] = {
    "llm": (".llm_router", "LLMRouter"),
    "jev": (".jev", "JevRouter"),
}

_extra: dict[str, type] = {}  # runtime additions (tests)


def registry() -> dict[str, type]:
    reg: dict[str, type] = {}
    for name, (mod, cls) in OPTIONAL_ROUTERS.items():
        try:
            reg[name] = getattr(importlib.import_module(mod, __package__), cls)
        except (ImportError, AttributeError):
            continue
    reg.update(_extra)
    return reg


def resolve_auto(reg: dict[str, type]) -> str:
    # Recorded runs use Jev as the primary router, so a keyless MOCK_LLM replay must pick it too:
    # another router would change the risk scores, the lens hunk selection and the fixture keys.
    if "jev" in reg and (config.JEV_API_KEY or config.MOCK_LLM):
        return "jev"
    if "llm" in reg and (config.ANTHROPIC_API_KEY or config.MOCK_LLM):
        return "llm"
    return "rule"


def _merge(rule_res: RouteResult, other: RouteResult, other_name: str) -> RouteResult:
    floor = floor_for(rule_res)
    floor_won = rule_res.floor_applied and floor > other.risk
    return RouteResult(
        hunk_id=rule_res.hunk_id,
        risk=max(floor, other.risk),
        category=rule_res.category if floor_won else other.category,
        confidence=other.confidence,
        router=f"rule+{other_name}",
        cost_tokens=other.cost_tokens,
        ms=other.ms,
        floor_applied=floor_won,
        reason=(f"{other_name} scored {other.risk}; rule floor {floor} applied" if floor_won
                else other.reason or f"{other_name} score"),
    )


def depth_by_file(files: list[FileChange], hunks: list[Hunk],
                  routes: list[RouteResult]) -> dict[str, Depth]:
    risk_by_id = {r.hunk_id: r.risk for r in routes}
    max_risk: dict[str, int] = {}
    for h in hunks:
        max_risk[h.file] = max(max_risk.get(h.file, 1), risk_by_id.get(h.id, 1))
    out: dict[str, Depth] = {}
    for f in files:
        if f.kind == "generated":
            out[f.path] = "regeneration"
        elif max_risk.get(f.path, 1) >= config.ROUTER_DEEP_THRESHOLD:
            out[f.path] = "deep"
        else:
            out[f.path] = "light"
    return out


def route_all(hunks: list[Hunk], requirement_text: str, context: dict,
              router_name: str = "rule", files: list[FileChange] | None = None) -> RoutingReport:
    """Score every hunk. `context` may hold "files" (list[FileChange]); per-hunk context is built
    here as {"file": FileChange, "area": str, **context}."""
    files = files if files is not None else context.get("files", [])
    by_path = {f.path: f for f in files}
    rule = RuleRouter()
    reg = registry()
    requested = router_name
    if router_name == "auto":
        router_name = resolve_auto(reg)

    other = None
    fallback_reason = ""
    if router_name not in ("rule", "none"):
        factory = reg.get(router_name)
        if factory is None:
            fallback_reason = f"router '{router_name}' not available; rule result used"
        else:
            try:
                other = factory()
            except Exception as e:  # noqa: BLE001 - any construction failure means fallback
                fallback_reason = f"router '{router_name}' failed to start ({e}); rule result used"

    routes: list[RouteResult] = []
    t0 = time.monotonic()
    for h in hunks:
        f = by_path.get(h.file)
        hctx = {**context, "file": f, "area": f.area if f else ""}
        rule_res = rule.route(h, requirement_text, hctx)
        if router_name == "none" and h.kind != "generated":
            rule_res = rule_res.model_copy(update={
                "risk": max(rule_res.risk, config.ROUTER_DEEP_THRESHOLD), "router": "none",
                "reason": "router=none: full review of every non-generated hunk"})
        if other is None or h.kind == "generated":
            if fallback_reason and h.kind != "generated":
                rule_res = rule_res.model_copy(
                    update={"reason": f"{rule_res.reason}; {fallback_reason}"})
            routes.append(rule_res)
            continue
        try:
            res = other.route(h, requirement_text, hctx)
            routes.append(_merge(rule_res, res, router_name))
        except Exception as e:  # noqa: BLE001 - a failing router must never fail the review
            fallback_reason = f"router '{router_name}' raised ({type(e).__name__}: {e}); rule result used"
            other = None
            routes.append(rule_res.model_copy(
                update={"reason": f"{rule_res.reason}; {fallback_reason}"}))

    total_tokens = sum(r.cost_tokens for r in routes)
    used = other is not None or any(r.router.startswith("rule+") for r in routes)
    usd_per_token = getattr(reg.get(router_name), "usd_per_token", 0.0) if used else 0.0
    if used:
        label = f"rule+{router_name}"
    elif router_name == "none":
        label = "none"
    else:
        label = "rule"
    if requested == "auto":
        label += " (auto)"
    return RoutingReport(
        router=label,
        routes=routes,
        depth_by_file=depth_by_file(files, hunks, routes),
        total_tokens=total_tokens,
        total_ms=int((time.monotonic() - t0) * 1000),
        cost_usd=round(total_tokens * usd_per_token, 6),
    )
