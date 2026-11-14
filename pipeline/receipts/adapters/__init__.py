"""Sandbox adapters keyed by change type. Each turns a base..head change into CommandReceipts and
Evidence that the verdict composer reads exactly like the .NET adapter's output.

The .NET adapter (sandbox.py, driven by review.py) predates this registry and stays there; a new
adapter is one module with `run(AdapterInput) -> AdapterResult` and one line in ADAPTERS.
"""

from collections.abc import Callable

from . import sql
from .base import AdapterInput, AdapterResult

ADAPTERS: dict[str, Callable[[AdapterInput], AdapterResult]] = {
    "sql": sql.run,
}


def change_type(paths: list[str]) -> str | None:
    """The adapter a change needs, from the paths it touches; None = no registered adapter."""
    if any(sql.is_migration(p) for p in paths):
        return "sql"
    return None


def get(kind: str) -> Callable[[AdapterInput], AdapterResult]:
    if kind not in ADAPTERS:
        raise KeyError(f"no sandbox adapter for change type {kind!r}; known: {sorted(ADAPTERS)}")
    return ADAPTERS[kind]


__all__ = ["ADAPTERS", "AdapterInput", "AdapterResult", "change_type", "get"]
