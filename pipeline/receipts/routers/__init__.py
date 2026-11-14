"""[2] Routers. Each scores one hunk: risk 1..10 + category (SPEC 5.2).

Contract: `route(hunk, requirement_text, context) -> RouteResult`, where context is a dict with
"file" (models.FileChange of the hunk's file) and "area" (str). Routers may raise on failure
(e.g. llm.LLMUnavailable); combine.route_all falls back to the rule result and says so.

To add a router: create routers/<name>.py with a class that has `name`, `route(...)` and optionally
`usd_per_token`, then add it to OPTIONAL_ROUTERS in routers/combine.py (one line).
"""

from typing import Protocol

from ..models import Hunk, RouteResult


class Router(Protocol):
    name: str

    def route(self, hunk: Hunk, requirement_text: str, context: dict) -> RouteResult: ...
