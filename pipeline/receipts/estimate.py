"""Cost estimate for queued LLM requests (llm collect mode), computed before anything is sent.

Input tokens ~ prompt characters / CHARS_PER_TOKEN (system + user + JSON schema). Output tokens per purpose are
the mean of what earlier real calls produced: lens calls from runs/*/*/lenses.json, readers and
the orientation call from runs/*/*/readers.json, anything else from the recorded fixtures. Replayed
entries (from_fixture) are skipped so one real call counts once. Those runs used the historical
effort, so for a lens run at a lower effort the output figure is an upper bound.
"""

import json
from dataclasses import dataclass
from pathlib import Path

from . import config, llm

# Not the usual 4: on the 36 bench cases, chars/4 came out 1.71-1.80x below the input tokens the
# API reported per lens (diff hunks and C# tokenize densely), so the guard would under-estimate.
CHARS_PER_TOKEN = 2.3
DEFAULT_OUTPUT_TOKENS = 2000  # a purpose with no recorded call at all


@dataclass
class Line:
    purpose: str
    model: str
    effort: str
    requests: int
    input_tokens: int
    output_tokens: int
    output_basis: str  # "N recorded calls" or "no history"

    @property
    def standard_usd(self) -> float:
        return llm.cost_usd(self.model, self.input_tokens, self.output_tokens)

    @property
    def batch_usd(self) -> float:
        return round(self.standard_usd * llm.BATCH_DISCOUNT, 6)


def _read(path: Path) -> dict:
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return {}


def historical_output(run_roots: list[Path] | None = None,
                      fixtures: Path | None = None) -> dict[str, tuple[float, int]]:
    """purpose -> (mean output tokens, number of real calls averaged)."""
    roots = run_roots if run_roots is not None else sorted({config.RUNS, config.ROOT / "runs"})
    samples: dict[str, list[int]] = {}

    def add(purpose: str, rec: dict) -> None:
        if rec.get("output_tokens") and not rec.get("from_fixture"):
            samples.setdefault(purpose, []).append(int(rec["output_tokens"]))

    for root in roots:
        for f in root.glob("*/*/lenses.json"):
            for r in _read(f).get("lenses", []):
                add(f"lens-{r.get('lens')}", r)
        for f in root.glob("*/*/readers.json"):
            data = _read(f)
            for r in data.get("readers", []):
                add("readers", r)
            add("readers-orient", data.get("orientation") or {})
    fixtures = fixtures or config.FIXTURES_LLM
    for d in sorted(fixtures.iterdir()) if fixtures.exists() else []:
        if d.is_dir() and d.name not in samples:
            for f in d.glob("*.json"):
                out = _read(f).get("output_tokens")
                if out:
                    samples.setdefault(d.name, []).append(int(out))
    return {p: (sum(v) / len(v), len(v)) for p, v in samples.items()}


def estimate(requests: dict[str, dict], history: dict[str, tuple[float, int]]) -> list[Line]:
    """One line per (purpose, model, effort) over the queued requests ({custom_id: queue entry})."""
    lines: dict[tuple[str, str, str], Line] = {}
    for e in requests.values():
        p = e["params"]
        chars = len(p.get("system", "")) + sum(len(m["content"]) for m in p["messages"])
        chars += len(json.dumps(p.get("output_config", {}).get("format", {})))
        effort = p.get("output_config", {}).get("effort", "-")
        mean, n = history.get(e["purpose"], (DEFAULT_OUTPUT_TOKENS, 0))
        k = (e["purpose"], p["model"], effort)
        line = lines.setdefault(k, Line(e["purpose"], p["model"], effort, 0, 0, 0,
                                        f"{n} recorded calls" if n else "no history"))
        line.requests += 1
        line.input_tokens += round(chars / CHARS_PER_TOKEN)
        line.output_tokens += min(round(mean), p["max_tokens"])
    return sorted(lines.values(), key=lambda x: -x.standard_usd)


def totals(lines: list[Line]) -> dict:
    return {"requests": sum(x.requests for x in lines),
            "input_tokens": sum(x.input_tokens for x in lines),
            "output_tokens": sum(x.output_tokens for x in lines),
            "standard_usd": round(sum(x.standard_usd for x in lines), 2),
            "batch_usd": round(sum(x.batch_usd for x in lines), 2)}


def to_json(lines: list[Line]) -> dict:
    return {"lines": [{"purpose": x.purpose, "model": x.model, "effort": x.effort,
                       "requests": x.requests, "input_tokens": x.input_tokens,
                       "output_tokens": x.output_tokens, "output_basis": x.output_basis,
                       "standard_usd": round(x.standard_usd, 4), "batch_usd": round(x.batch_usd, 4)}
                      for x in lines], "totals": totals(lines)}


def format_table(lines: list[Line]) -> str:
    head = (f"{'purpose':<24} {'model':<26} {'effort':<7} {'req':>4} {'in tok':>9} {'out tok':>9} "
            f"{'standard $':>11} {'batch $':>9}  output basis")
    rows = [head, "-" * len(head)]
    for x in lines:
        rows.append(f"{x.purpose:<24} {x.model:<26} {x.effort:<7} {x.requests:>4} "
                    f"{x.input_tokens:>9,} {x.output_tokens:>9,} {x.standard_usd:>11.2f} "
                    f"{x.batch_usd:>9.2f}  {x.output_basis}")
    t = totals(lines)
    rows.append(f"{'total':<24} {'':<26} {'':<7} {t['requests']:>4} {t['input_tokens']:>9,} "
                f"{t['output_tokens']:>9,} {t['standard_usd']:>11.2f} {t['batch_usd']:>9.2f}")
    rows.append("standard $ = the same requests sent live (non-batch path); batch $ = Message "
                "Batches at 50%.")
    return "\n".join(rows)
