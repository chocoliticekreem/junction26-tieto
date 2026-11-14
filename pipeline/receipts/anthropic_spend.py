"""Hard cap on real Anthropic spend: a ledger in config.ANTHROPIC_SPEND_FILE shared by every process.

Before anything is sent, the caller reserves the worst case: for a live call, prompt chars /
CHARS_PER_TOKEN input tokens plus max_tokens output at list price; for a Message Batch, the same per
request at batch price. A reservation that would take spend + live reservations past
config.ANTHROPIC_MAX_USD raises LLMUnavailable("budget: ..."), so the step shows as not checked.
After the call (or when the batch results arrive) the reservation is replaced by the cost of the
usage the API reported.

A live call's reservation belongs to its process and is dropped when that process is dead. A batch
reservation is kept until its results are settled, because the batch keeps running (and billing)
on Anthropic's side after the process that submitted it dies; llm.run_batch resumes and settles it.
"""

import fcntl
import json
import os
import uuid
from contextlib import contextmanager
from datetime import datetime, timezone

from . import config

CHARS_PER_TOKEN = 2.3  # same calibration as estimate.py: a high input estimate, never a low one


def _llm():
    from . import llm

    return llm


def request_chars(params: dict) -> int:
    """Characters of a Messages API body that become input tokens (system, messages, schema)."""
    fmt = params.get("output_config", {}).get("format", {})
    return (len(params.get("system", "")) + sum(len(m["content"]) for m in params["messages"])
            + len(json.dumps(fmt)))


def worst_usd(params: dict, batch: bool = False) -> float:
    llm = _llm()
    usd = llm.cost_usd(params["model"], round(request_chars(params) / CHARS_PER_TOKEN),
                       params["max_tokens"])
    return round(usd * (llm.BATCH_DISCOUNT if batch else 1.0), 6)


@contextmanager
def _locked():
    path = config.ANTHROPIC_SPEND_FILE
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path.with_suffix(".lock"), "a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            led = json.loads(path.read_text()) if path.exists() else {}
            led.setdefault("total_usd", 0.0)
            led.setdefault("calls", [])
            led.setdefault("reservations", {})
            yield led
            led["max_usd"] = config.ANTHROPIC_MAX_USD
            tmp = path.with_suffix(".tmp")
            tmp.write_text(json.dumps(led, indent=1))
            tmp.replace(path)
        finally:
            fcntl.flock(lock, fcntl.LOCK_UN)


def _alive(pid: int) -> bool:
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def _live(led: dict) -> dict:
    return {k: v for k, v in led["reservations"].items()
            if v.get("batch_id") or _alive(v["pid"])}


def spent_usd() -> float:
    path = config.ANTHROPIC_SPEND_FILE
    return json.loads(path.read_text()).get("total_usd", 0.0) if path.exists() else 0.0


def available_usd() -> float:
    """Budget left for a new reservation: cap - spend - live reservations."""
    with _locked() as led:
        live = _live(led)
        led["reservations"] = live
        return config.ANTHROPIC_MAX_USD - led["total_usd"] - sum(v["usd"] for v in live.values())


def reserve(worst: float, what: str) -> str:
    """Hold `worst` USD against the cap; raise LLMUnavailable when it does not fit."""
    with _locked() as led:
        live = _live(led)
        held = sum(v["usd"] for v in live.values())
        led["reservations"] = live
        if led["total_usd"] + held + worst > config.ANTHROPIC_MAX_USD:
            raise _llm().LLMUnavailable(
                f"budget: Anthropic spend ${led['total_usd']:.4f} + ${held:.4f} held + {what} "
                f"worst case ${worst:.4f} would pass ANTHROPIC_MAX_USD={config.ANTHROPIC_MAX_USD:g}")
        rid = uuid.uuid4().hex[:12]
        live[rid] = {"pid": os.getpid(), "usd": round(worst, 6), "what": what}
        return rid


def attach_batch(rid: str, batch_id: str) -> None:
    """Mark a reservation as a submitted batch, so it outlives the submitting process."""
    with _locked() as led:
        if rid in led["reservations"]:
            led["reservations"][rid]["batch_id"] = batch_id


def settle(rid: str | None, what: str, usd: float, **detail) -> None:
    """Replace reservation `rid` (if any) with the actual cost `usd`."""
    with _locked() as led:
        if rid:
            led["reservations"].pop(rid, None)
        led["total_usd"] = round(led["total_usd"] + usd, 6)
        led["calls"].append({"at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
                             "what": what, "cost_usd": round(usd, 6), **detail})


def batch_reservation(batch_id: str) -> str | None:
    path = config.ANTHROPIC_SPEND_FILE
    if not path.exists():
        return None
    res = json.loads(path.read_text()).get("reservations", {})
    return next((k for k, v in res.items() if v.get("batch_id") == batch_id), None)
